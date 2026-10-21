using System.Text;
using System.Text.Json.Nodes;
using SpeedLink.Core;
namespace SpeedLink.Tests;

public class KnownAndUnknownRomTests
{
    [Fact]
    public void test_all_known_layouts_validate_and_correct_only_their_sum_word()
    {
        foreach (var profile in Fixture.Profiles)
        {
            var data = Fixture.Image(profile);
            var inspector = Fixture.Inspector();
            Assert.True(inspector.Inspect(data).ProfileValid);
            data[0x60000] ^= 1;
            Assert.False(inspector.Inspect(data).ChecksumValid);
            var corrected = inspector.CorrectedCopy(data);
            Assert.True(inspector.Inspect(corrected).ProfileValid);
            var at = profile.CorrectionAddress;
            Assert.Equal(data[..at], corrected[..at]);
            Assert.Equal(data[(at + 4)..], corrected[(at + 4)..]);
        }
    }
    [Fact]
    public void test_unknown_image_is_dumped_reopened_and_never_assigned_a_checksum()
    {
        using var temp = new TempFolder();
        var data = Enumerable.Repeat((byte)0xFF, RomInspector.RomSize).ToArray();
        using var ecu = new TestEcu(data);
        var info = Fixture.Inspector().Inspect(data);
        Assert.False(info.ProfileRecognized);
        Assert.Null(info.Checksum);
        Assert.Null(info.ChecksumValid);
        Assert.False(info.CanCorrectChecksum);
        var result = Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path);
        var loaded = Fixture.Store().Load(result.Path);
        Assert.Equal(data, loaded.Data);
        Assert.Equal("UNKNOWN", loaded.Manifest["identity"]!["calibration"]!.GetValue<string>());
        Assert.Throws<InvalidDataException>(() => Fixture.Inspector().CorrectedCopy(loaded.Data));
    }
    [Fact]
    public void test_all_known_images_can_also_be_dumped()
    {
        using var temp = new TempFolder();
        foreach (var profile in Fixture.Profiles)
        {
            var data = Fixture.Image(profile);
            using var ecu = new TestEcu(data);
            var identity = ReadWorkflow.Identify(ecu);
            Assert.Equal(profile.Calibration, identity.Calibration);
            var result = Fixture.Reader().ReadBackup(ecu, identity, temp.Path);
            Assert.True(result.Inspection.ProfileValid);
            Assert.Equal(data, Fixture.Store().Load(result.Path).Data);
        }
    }
    [Fact]
    public void test_bad_checksum_and_unrecognized_boot_do_not_block_dump()
    {
        using var temp = new TempFolder();
        var data = Fixture.Rom;
        data[0x100] ^= 1;
        data[0x60000] ^= 1;
        using var ecu = new TestEcu(data);
        var result = Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path);
        Assert.Equal(data, File.ReadAllBytes(result.Path));
        Assert.False(result.Inspection.BootRecognized);
        Assert.False(result.Inspection.ChecksumValid ?? false);
        Assert.False(result.Inspection.CanCorrectChecksum);
    }
    [Fact]
    public void test_unreported_calibration_does_not_require_a_definition()
    {
        using var temp = new TempFolder();
        var data = Enumerable.Repeat((byte)0xFF, RomInspector.RomSize).ToArray();
        using var ecu = new TestEcu(data) { ModifyResponse = (request, response) => Convert.ToHexString(request) == "0904" ? throw new NegativeResponseException(0x09, 0x12) : response };
        var identity = ReadWorkflow.Identify(ecu);
        Assert.Null(identity.Calibration);
        var result = Fixture.Reader().ReadBackup(ecu, identity, temp.Path);
        Assert.Equal(data, Fixture.Store().Load(result.Path).Data);
    }
    [Fact]
    public void test_changed_checksum_code_disables_correction()
    {
        foreach (var profile in Fixture.Profiles.Where(p => p.LoopAddress is not null))
        {
            var data = Fixture.Image(profile);
            data[profile.LoopAddress!.Value] ^= 1;
            var info = Fixture.Inspector().Inspect(data);
            Assert.False(info.ChecksumLayoutRecognized);
            Assert.Null(info.ChecksumValid);
            Assert.Throws<InvalidDataException>(() => Fixture.Inspector().CorrectedCopy(data));
        }
    }
    [Fact]
    public void test_build_identity_mismatch_and_ambiguous_images_are_not_guessed()
    {
        var first = Fixture.Profiles[0];
        var second = Fixture.Profiles[1];
        var data = Fixture.Image(first);
        data[first.IdentityOffset + first.Identity.Length - 1] ^= 1;
        Assert.False(Fixture.Inspector().Inspect(data).IdentityValid);
        Assert.Throws<InvalidDataException>(() => Fixture.Inspector().CorrectedCopy(data));
        data = Fixture.Image(first);
        Encoding.ASCII.GetBytes(second.Identity).CopyTo(data, second.IdentityOffset);
        Assert.False(Fixture.Inspector().Inspect(data).ProfileRecognized);
        Assert.Null(Fixture.Inspector().Inspect(data).Checksum);
    }
}
public class PortContractTests
{
    [Fact]
    public void test_explicit_transfer_rates_initialize_and_restore()
    {
        foreach (var rate in new[] { 115200, 230400, 500000, 1000000, 2000000 })
        {
            var serial = new FakeSerial();
            var link = new VLinkerReadTransport(serial, new TestClock(), rate);
            link.Initialize();
            Assert.Equal(rate, link.Baud);
            link.Dispose();
            Assert.Equal(115200, link.Baud);
            Assert.Null(link.CloseWarning);
        }
    }
    [Fact]
    public void test_invalid_transfer_rate_is_rejected_before_io()
    {
        foreach (var rate in new[] { 0, 38400, 999999, 3000000 })
        {
            var serial = new FakeSerial();
            Assert.Throws<ArgumentOutOfRangeException>(() => new VLinkerReadTransport(serial, new TestClock(), rate));
            Assert.Empty(serial.Writes);
        }
    }
    [Fact]
    public void test_unreported_calibration_over_real_driver_still_dumps()
    {
        using var temp = new TempFolder();
        var data = Enumerable.Repeat((byte)0xFF, RomInspector.RomSize).ToArray();
        using var ecu = new TestEcu(data) { ModifyResponse = (request, response) => Convert.ToHexString(request) == "0904" ? throw new NegativeResponseException(0x09, 0x12) : response };
        var serial = new FakeSerial { ResponseOverride = request => { try { return ecu.Exchange(request); } catch (NegativeResponseException error) { return new byte[] { 0x7F, error.Service, error.Code }; } } };
        using var link = new VLinkerReadTransport(serial, new TestClock());
        link.Initialize();
        var identity = ReadWorkflow.Identify(link);
        Assert.Null(identity.Calibration);
        Assert.False(link.Failed);
        var result = Fixture.Reader().ReadBackup(link, identity, temp.Path);
        Assert.Equal(data, Fixture.Store().Load(result.Path, "hardware").Data);
        Assert.Equal(530, ecu.Requests.Count(request => request[0] == 0x23));
    }
    [Fact]
    public void test_baud_transition_handles_extra_adapter_prompt()
    {
        var serial = new FakeSerial { IdentityPrefix = ">\r" };
        var link = new VLinkerReadTransport(serial, new TestClock());
        link.Initialize();
        Assert.Equal(2000000, link.Baud);
        link.Dispose();
        Assert.Equal(115200, link.Baud);
        Assert.Null(link.CloseWarning);
        var bad = new FakeSerial { IdentityPrefix = "JUNK>\r" };
        using var invalid = new VLinkerReadTransport(bad, new TestClock());
        Assert.Throws<TransportException>(invalid.Initialize);
        Assert.False(invalid.Ready);
    }
    [Fact]
    public void test_win32_serial_structures_match_documented_abi()
    {
        Assert.Equal((28, 20, 12), SpeedLink.Hardware.SerialConnection.InteropLayout);
    }
    [Fact]
    public void test_reference_vectors()
    {
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ReferenceVectors.json")))!;
        foreach (var row in golden["profiles"]!.AsArray())
        {
            var profile = Fixture.Profiles.Single(p => p.Calibration == row!["calibration"]!.GetValue<string>());
            var data = Fixture.Image(profile);
            Assert.Equal(row!["sha256"]!.GetValue<string>(), RomInspector.Hash(data));
            data[0x60000] ^= 1;
            Assert.Equal(row["correctedSHA256"]!.GetValue<string>(), RomInspector.Hash(Fixture.Inspector().CorrectedCopy(data)));
        }
        foreach (var row in golden["keys"]!.AsArray())
            Assert.Equal(row!["key"]!.GetValue<string>(), Convert.ToHexStringLower(SecurityAccess.SeedKey(Fixture.Hex(row["seed"]!.GetValue<string>()))));
    }
    [Fact]
    public void test_canonical_json_unicode_and_ascii_escapes()
    {
        var value = new Dictionary<string, object?> { ["z"] = "vLinker FS ·", ["a"] = new object?[] { 1, true, null, "😀\n<>&+\"" } };
        Assert.Equal("{\"a\":[1,true,null,\"\\ud83d\\ude00\\n<>&+\\\"\"],\"z\":\"vLinker FS \\u00b7\"}", Encoding.ASCII.GetString(CanonicalJson.Bytes(value)));
    }
}
