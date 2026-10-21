using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public class LoggerDefinitionTests
{
    private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "LoggerDefinitions", name);
    private static LoggerDefinition Profile() => LoggerDefinition.Load(Data("L38KEH000.logger.json"));
    private static JsonNode Editable() => JsonNode.Parse(File.ReadAllText(Data("L38KEH000.logger.json")))!;
    private static LoggerDefinition Parse(JsonNode node) => LoggerDefinition.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()));
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(actual - expected), 0, Math.Max(1e-10, Math.Abs(expected) * 2e-12));
    private static LoggerCalibration Calibration(JsonElement node) => LoggerCalibration.ExplicitMap(
        node.GetProperty("map_gain_kpa_per_v").GetDouble(), node.GetProperty("map_offset_kpa").GetDouble());

    [Fact]
    public void All38ChannelsHaveEvidenceAndPendingEcuChecks()
    {
        var profile = Profile();
        Assert.Equal(38, profile.ChannelIds.Count);
        Assert.Equal(34, profile.QueryIds.Count);
        using var document = JsonDocument.Parse(File.ReadAllBytes(Data("L38KEH000.logger.json")));
        var channels = document.RootElement.GetProperty("channels").EnumerateArray().ToArray();
        Assert.Equal(new[] { 5, 12, 19, 2 }, new[] { "Boost", "Fuel", "General", "Ignition" }
            .Select(group => channels.Count(c => c.GetProperty("group").GetString() == group)).ToArray());
        foreach (var channel in channels)
        {
            Assert.Equal("pending", channel.GetProperty("verification").GetProperty("ecu").GetString());
            Assert.True(channel.GetProperty("verification").GetProperty("ecuChecks").GetArrayLength() >= 2);
        }
        Assert.False(document.RootElement.GetProperty("validation").GetProperty("realEcuTested").GetBoolean());
        Assert.False(document.RootElement.GetProperty("validation").GetProperty("loggerAdapterBenchTested").GetBoolean());
    }

    [Fact]
    public void All298NativeVectorsDecodeInCSharp()
    {
        var profile = Profile();
        using var document = JsonDocument.Parse(File.ReadAllBytes(Data("L38KEH000.vectors.json")));
        var calibration = Calibration(document.RootElement.GetProperty("calibration"));
        var covered = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        var now = DateTimeOffset.UnixEpoch.AddSeconds(1);
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            string id = vector.GetProperty("channel").GetString()!;
            double result;
            if (vector.GetProperty("type").GetString() == "decode")
                result = profile.Decode(id, Convert.FromHexString(vector.GetProperty("responseHex").GetString()!), calibration);
            else
            {
                var inputs = vector.GetProperty("inputs").EnumerateObject().ToDictionary(p => p.Name,
                    p => new LoggerSample(p.Value.GetDouble(), now));
                result = profile.Calculate(id, inputs, now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
            }
            Near(vector.GetProperty("expected").GetDouble(), result);
            covered.Add(id); count++;
        }
        Assert.Equal(298, count);
        Assert.True(covered.SetEquals(profile.ChannelIds));
    }

    [Fact]
    public void SharedQueriesAndReadOnlyServiceArePreserved()
    {
        var profile = Profile();
        Assert.Equal(profile.RequestFor("map_sensor_voltage"), profile.RequestFor("manifold_absolute_pressure"));
        Assert.Equal(profile.RequestFor("clutch_switch"), profile.RequestFor("neutral_switch"));
        var distinct = new HashSet<string>();
        foreach (string id in profile.ChannelIds.Except(["boost_pressure", "injector_duty_cycle"]))
        {
            byte[] request = profile.RequestFor(id);
            Assert.Equal(3, request.Length); Assert.Equal(0x22, request[0]);
            distinct.Add(Convert.ToHexString(request));
        }
        Assert.Equal(34, distinct.Count);
        Assert.Throws<InvalidOperationException>(() => profile.RequestFor("boost_pressure"));
    }

    [Fact]
    public void EveryQueriedChannelRejectsTruncatedExtraWrongIdAndNegativeReplies()
    {
        var profile = Profile();
        using var document = JsonDocument.Parse(File.ReadAllBytes(Data("L38KEH000.vectors.json")));
        var calibration = Calibration(document.RootElement.GetProperty("calibration"));
        var seen = new HashSet<string>();
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            if (vector.GetProperty("type").GetString() != "decode") continue;
            string id = vector.GetProperty("channel").GetString()!;
            if (!seen.Add(id)) continue;
            byte[] valid = Convert.FromHexString(vector.GetProperty("responseHex").GetString()!);
            byte[] wrongId = (byte[])valid.Clone(); wrongId[2] ^= 0x80;
            byte[] wrongService = (byte[])valid.Clone(); wrongService[0] = 0x63;
            foreach (byte[] invalid in new[] { Array.Empty<byte>(), valid[..^1], valid.Concat(new byte[] { 0 }).ToArray(), wrongId, wrongService })
                Assert.Throws<TransportException>(() => profile.Decode(id, invalid, calibration));
            Assert.Throws<NegativeResponseException>(() => profile.Decode(id, [0x7F, 0x22, 0x31], calibration));
            Assert.Throws<NegativeResponseException>(() => profile.Decode(id, [0x7F, 0x22, 0x78], calibration));
        }
        Assert.Equal(36, seen.Count);
    }

    [Fact]
    public void SignedAnglesAndTrimUseCorrectUnits()
    {
        var profile = Profile();
        Near(-1, profile.Decode("actual_valve_timing", Convert.FromHexString("6216CDFFF0")));
        Near(-25, profile.Decode("short_term_fuel_trim_secondary", Convert.FromHexString("62163EF800")));
        Near(-.25, profile.ConvertDisplay("short_term_fuel_trim_secondary", -25, "correction_fraction"));
    }

    [Fact]
    public void WidebandLambdaConsumesFullPayloadAndNeverUsesCurrentAsLambda()
    {
        var profile = Profile();
        Near(1, profile.Decode("actual_lambda", Convert.FromHexString("62003480001234")));
        Near(1, profile.Decode("actual_lambda", Convert.FromHexString("6200348000FFFF")));
        Assert.Throws<TransportException>(() => profile.Decode("actual_lambda", Convert.FromHexString("6200348000")));
    }

    [Fact]
    public void ClutchAndNeutralMasksIgnoreUnrelatedFlags()
    {
        var profile = Profile();
        foreach (ushort raw in new ushort[] { 0, 1, 2, 3, 0x400, 0x401, 0x402, 0x403, 0x1000, 0x1403, 0xFFFF })
        {
            byte[] response = [0x62, 0xA2, 0x11, (byte)(raw >> 8), (byte)raw];
            Near((raw & 1) == 0 ? 0 : 1, profile.Decode("clutch_switch", response));
            Near((raw & 0x400) == 0 ? 0 : 1, profile.Decode("neutral_switch", response));
        }
    }

    [Fact]
    public void MapNeedsExplicitCalibrationAndSupportsPressureAbove255Kpa()
    {
        var profile = Profile();
        byte[] response = Convert.FromHexString("6209000A00");
        Near(2.5, profile.Decode("map_sensor_voltage", response));
        Assert.Throws<InvalidOperationException>(() => profile.Decode("manifold_absolute_pressure", response));
        Near(200, profile.Decode("manifold_absolute_pressure", response, LoggerCalibration.ExplicitMap(75, -12.5)));
        Assert.True(profile.Decode("manifold_absolute_pressure", Convert.FromHexString("620900119A"), LoggerCalibration.ExplicitMap(75, -12.5)) > 255);
        Assert.Throws<InvalidDataException>(() => LoggerCalibration.ExplicitMap(0, 0));
        Assert.Throws<InvalidDataException>(() => LoggerCalibration.ExplicitMap(double.NaN, 0));
    }

    [Fact]
    public void AfrNeedsExplicitFuelStoichiometry()
    {
        var profile = Profile();
        Assert.Throws<InvalidOperationException>(() => profile.ConvertDisplay("actual_lambda", .8, "AFR"));
        Near(11.76, profile.ConvertDisplay("actual_lambda", .8, "AFR", new Dictionary<string, double> { ["stoichiometric_afr"] = 14.7 }));
        Near(7.84, profile.ConvertDisplay("actual_lambda", .8, "AFR", new Dictionary<string, double> { ["stoichiometric_afr"] = 9.8 }));
        Assert.Throws<InvalidOperationException>(() => profile.ConvertDisplay("actual_lambda", .8, "AFR", new Dictionary<string, double> { ["stoichiometric_afr"] = double.PositiveInfinity }));
    }

    [Fact]
    public void CalculationsPreserveVacuumAndUse360DegreeInjectionWindow()
    {
        var profile = Profile(); var now = DateTimeOffset.UnixEpoch;
        Near(-60, profile.Calculate("boost_pressure", new Dictionary<string, LoggerSample>
        { ["manifold_absolute_pressure"] = new(40, now), ["barometric_pressure"] = new(100, now) }, now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50)));
        Near(60, profile.Calculate("injector_duty_cycle", new Dictionary<string, LoggerSample>
        { ["injection_pulsewidth"] = new(6, now), ["engine_rpm"] = new(6000, now) }, now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50)));
        Near(120, profile.Calculate("injector_duty_cycle", new Dictionary<string, LoggerSample>
        { ["injection_pulsewidth"] = new(12, now), ["engine_rpm"] = new(6000, now) }, now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void CalculationsRejectMissingInvalidStaleFutureAndSkewedSamples()
    {
        var profile = Profile(); var now = DateTimeOffset.UnixEpoch.AddSeconds(10);
        var inputs = new Dictionary<string, LoggerSample> { ["injection_pulsewidth"] = new(6, now), ["engine_rpm"] = new(6000, now) };
        foreach (var invalid in new[] { new LoggerSample(6000, now, false), new(double.NaN, now), new(6000, now.AddSeconds(-2)),
                     new(6000, now.AddSeconds(1)), new(6000, now.AddMilliseconds(-100)) })
        {
            inputs["engine_rpm"] = invalid;
            Assert.Throws<TransportException>(() => profile.Calculate("injector_duty_cycle", inputs, now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50)));
        }
        inputs.Remove("engine_rpm");
        Assert.Throws<TransportException>(() => profile.Calculate("injector_duty_cycle", inputs, now, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void MalformedDefinitionsCannotChangeServicesLengthsMasksOrDependencies()
    {
        var mutations = new Action<JsonNode>[] {
            n => n["schemaVersion"] = 2,
            n => n["protocol"]!["sessionChanges"] = new JsonArray("1087"),
            n => n["protocol"]!["service"] = "0x27",
            n => n["queries"]!["0900"]!["requestHex"] = "270100",
            n => n["queries"]!["0900"]!["dataBytes"] = 3,
            n => n["channels"]![0]!["decode"]!["byteLength"] = 4,
            n => n["channels"]![0]!["decode"]!["operation"] = "evaluate",
            n => n["channels"]![1]!["calculation"]!["inputs"]![0] = "boost_pressure",
            n => n["channels"]![1]!["calculation"]!["inputs"]![0] = "missing",
            n => n["codeFingerprints"] = new JsonArray()
        };
        foreach (var mutate in mutations) { var node = Editable(); mutate(node); Assert.Throws<InvalidDataException>(() => Parse(node)); }
        var duplicate = File.ReadAllText(Data("L38KEH000.logger.json")).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,");
        Assert.Throws<InvalidDataException>(() => LoggerDefinition.Parse(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void RomBindingChecksIdentityCodeAndCalibrationIndependently()
    {
        var node = Editable();
        byte[] image = Enumerable.Repeat((byte)0xA5, 1_048_576).ToArray();
        Encoding.ASCII.GetBytes("SW-L38KEH000.HEX").CopyTo(image, 0x100);
        foreach (var (offset, value) in new[] { (0xB1644, 230f), (0xB1648, .2352941185f), (0xB164C, 1.647f) })
            BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
        foreach (var fingerprint in node["codeFingerprints"]!.AsArray())
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var range in fingerprint!["ranges"]!.AsArray())
            {
                int start = Convert.ToInt32(range!["start"]!.GetValue<string>()[2..], 16), length = range["length"]!.GetValue<int>();
                hash.AppendData(image.AsSpan(start, length));
            }
            fingerprint["sha256"] = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        var profile = Parse(node);
        Near((double)230f * .2352941185f, profile.BindRom(image).Values["map_gain_kpa_per_v"]);
        byte[] changedCalibration = (byte[])image.Clone();
        BinaryPrimitives.WriteInt32BigEndian(changedCalibration.AsSpan(0xB1644, 4), BitConverter.SingleToInt32Bits(300f));
        Near((double)300f * .2352941185f, profile.BindRom(changedCalibration).Values["map_gain_kpa_per_v"]);
        byte[] changedCode = (byte[])image.Clone(); changedCode[0x91C90] ^= 1;
        Assert.Throws<InvalidDataException>(() => profile.BindRom(changedCode));
        byte[] wrongId = (byte[])image.Clone(); wrongId[0x103] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => profile.BindRom(wrongId));
        Assert.Throws<InvalidDataException>(() => profile.BindRom(image[..^1]));
    }
}
