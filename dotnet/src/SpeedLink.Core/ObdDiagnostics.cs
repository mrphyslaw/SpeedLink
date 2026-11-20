using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SpeedLink.Core;

public interface IObdTransport : IReadTransport
{
    byte[] ExchangeObd(byte[] request, bool clearConfirmed = false);
}

[JsonConverter(typeof(JsonStringEnumConverter<ObdStatus>))]
public enum ObdStatus { Supported, Unsupported, Rejected, CommunicationError, NotAttempted }
public sealed record ObdResult<T>(ObdStatus Status, T? Value, string? Detail = null);
public sealed record ObdCode(string Code, string Description);
public sealed record ObdMonitor(string Name, bool Supported, bool? Complete);
public sealed record ObdReadiness(bool MilOn, int StoredCodeCount, string IgnitionType, IReadOnlyList<ObdMonitor> Monitors);
public sealed record ObdFrameValue(byte Pid, string Name, double? Value, string Unit, ObdStatus Status, string? Detail = null);
public sealed record ObdFreezeFrame(string? TriggerCode, IReadOnlyList<ObdFrameValue> Values, string Detail);
public sealed record ObdScan(DateTimeOffset CapturedUtc, string Adapter, EcuIdentity? Identity,
    ObdResult<ObdReadiness> Readiness, ObdResult<IReadOnlyList<ObdCode>> Stored,
    ObdResult<IReadOnlyList<ObdCode>> Pending, ObdResult<IReadOnlyList<ObdCode>> Permanent,
    ObdResult<ObdFreezeFrame> FreezeFrame, bool ConnectionUsable);
public sealed record ObdClearResult(bool Acknowledged, bool OutcomeUncertain, string Detail,
    ObdScan Before, ObdScan? After, string BeforeReport, string Journal, bool ConnectionUsable);
public sealed class ObdPreflightException(string message, bool connectionUsable) : TransportException(message)
{
    public bool ConnectionUsable { get; } = connectionUsable;
}

public static class ObdProtocol
{
    public static IReadOnlyList<byte> FramePids { get; } = Array.AsReadOnly<byte>([0x04, 0x05, 0x06, 0x07, 0x0B, 0x0C, 0x0D, 0x0F, 0x10, 0x11]);
    public static void CheckAllowed(ReadOnlySpan<byte> request, bool clearConfirmed = false)
    {
        bool read = request.Length == 1 && request[0] is 3 or 7 or 0x0A ||
            request.Length == 2 && request[0] == 1 && request[1] is 0 or 1 or 0x0C ||
            request.Length == 3 && request[0] == 2 && request[2] == 0 &&
            (request[1] is 0 or 2 || FramePids.Contains(request[1]));
        if (!read && !(clearConfirmed && request.SequenceEqual(new byte[] { 4 })))
            throw new TransportException("Request is outside the engine OBD diagnostics command set. Clearing requires explicit confirmation.");
    }
    internal static void Reply(ReadOnlySpan<byte> reply, byte service, int size, byte? pid = null, byte? frame = null)
    {
        if (reply.Length < size || reply.Length > Math.Max(size, 7) || reply[0] != service + 0x40 ||
            pid is { } p && reply[1] != p || frame is { } f && reply[2] != f ||
            reply[size..].ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Malformed OBD reply, wrong service/identifier, or unexpected trailing bytes.");
    }
    public static string DecodeCode(byte high, byte low) => $"{"PCBU"[high >> 6]}{(high >> 4) & 3}{high & 15:X}{low:X2}";
    public static IReadOnlyList<ObdCode> Codes(byte service, byte[] reply, Func<string, string?>? describe = null)
    {
        if (service is not (3 or 7 or 0x0A) || reply.Length < 2 || reply[0] != service + 0x40)
            throw new InvalidDataException("Invalid CAN OBD trouble-code response.");
        int size = 2 + 2 * reply[1];
        if (reply.Length < size || reply.Length > Math.Max(size, 7) || reply.AsSpan(size).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("Trouble-code count does not match the CAN OBD response length.");
        var codes = new List<ObdCode>();
        for (int i = 2; i < size; i += 2)
        {
            if (reply[i] == 0 && reply[i + 1] == 0) throw new InvalidDataException("Empty code inside the reported trouble-code count.");
            string code = DecodeCode(reply[i], reply[i + 1]);
            codes.Add(new(code, describe?.Invoke(code) ?? "Description unavailable in bundled definitions"));
        }
        return codes;
    }
    public static ObdReadiness Readiness(byte[] reply)
    {
        Reply(reply, 1, 6, 1);
        byte a = reply[2], b = reply[3], c = reply[4], d = reply[5];
        bool diesel = (b & 8) != 0;
        var monitors = new List<ObdMonitor>();
        string[] continuous = ["Misfire", "Fuel system", "Comprehensive components"];
        for (int i = 0; i < 3; i++) { bool supported = (b & 1 << i) != 0; monitors.Add(new(continuous[i], supported, supported ? (b & 1 << (i + 4)) == 0 : null)); }
        string?[] others = diesel
            ? ["NMHC catalyst", "NOx / SCR", null, "Boost pressure", null, "Exhaust gas sensor", "Particulate filter", "EGR / VVT"]
            : ["Catalyst", "Heated catalyst", "Evaporative system", "Secondary air", "A/C refrigerant", "Oxygen sensor", "Oxygen sensor heater", "EGR / VVT"];
        for (int i = 0; i < 8; i++) if (others[i] is { } name) { bool supported = (c & 1 << i) != 0; monitors.Add(new(name, supported, supported ? (d & 1 << i) == 0 : null)); }
        return new((a & 128) != 0, a & 127, diesel ? "Compression ignition" : "Spark ignition", monitors);
    }
    public static double Rpm(byte[] reply) { Reply(reply, 1, 4, 0x0C); return (reply[2] * 256 + reply[3]) / 4d; }
    public static uint FrameSupport(byte[] reply)
    { Reply(reply, 2, 7, 0, 0); return (uint)reply[3] << 24 | (uint)reply[4] << 16 | (uint)reply[5] << 8 | reply[6]; }
    public static bool Supports(uint bitmap, byte pid) => pid is >= 1 and <= 32 && (bitmap & (1u << (32 - pid))) != 0;
    public static ObdFrameValue FrameValue(byte pid, byte[] reply)
    {
        int width = pid is 0x0C or 0x10 ? 2 : 1;
        Reply(reply, 2, 3 + width, pid, 0);
        int a = reply[3], word = width == 2 ? a * 256 + reply[4] : a;
        var (name, value, unit) = pid switch
        {
            4 => ("Calculated load", a * 100d / 255, "%"),
            5 => ("Coolant temperature", a - 40d, "degC"),
            6 => ("Short-term fuel trim (bank 1)", a * 100d / 128 - 100, "%"),
            7 => ("Long-term fuel trim (bank 1)", a * 100d / 128 - 100, "%"),
            0x0B => ("Intake manifold absolute pressure", (double)a, "kPa"),
            0x0C => ("Engine speed", word / 4d, "rpm"),
            0x0D => ("Vehicle speed", (double)a, "km/h"),
            0x0F => ("Intake air temperature", a - 40d, "degC"),
            0x10 => ("Mass airflow", word / 100d, "g/s"),
            0x11 => ("Throttle position", a * 100d / 255, "%"),
            _ => throw new InvalidDataException("Unsupported freeze-frame PID.")
        };
        return new(pid, name, value, unit, ObdStatus.Supported);
    }
}

public sealed class ObdDiagnostics(IObdTransport transport, EcuIdentity? identity = null, Func<string, string?>? describe = null)
{
    public ObdScan Scan(CancellationToken cancel = default)
    {
        bool usable = true;
        ObdResult<T> Query<T>(byte[] request, Func<byte[], T> decode)
        {
            cancel.ThrowIfCancellationRequested();
            if (!usable) return new(ObdStatus.NotAttempted, default, "Reconnect after the preceding communication failure.");
            try { return new(ObdStatus.Supported, decode(transport.ExchangeObd(request))); }
            catch (NegativeResponseException e) { return new(e.Code is 0x11 or 0x12 or 0x31 ? ObdStatus.Unsupported : ObdStatus.Rejected, default, e.Message); }
            catch (Exception e) when (e is TransportException or InvalidDataException)
            { usable = false; return new(ObdStatus.CommunicationError, default, e.Message); }
        }
        var readiness = Query(new byte[] { 1, 1 }, ObdProtocol.Readiness);
        var stored = Query(new byte[] { 3 }, r => ObdProtocol.Codes(3, r, describe));
        var pending = Query(new byte[] { 7 }, r => ObdProtocol.Codes(7, r, describe));
        var permanent = Query(new byte[] { 0x0A }, r => ObdProtocol.Codes(0x0A, r, describe));
        var support = Query(new byte[] { 2, 0, 0 }, ObdProtocol.FrameSupport);
        ObdResult<ObdFreezeFrame> freeze;
        if (support.Status != ObdStatus.Supported) freeze = new(support.Status, null, support.Detail);
        else if (!ObdProtocol.Supports(support.Value, 2)) freeze = new(ObdStatus.Supported, new(null, [], "No freeze-frame trigger PID advertised."));
        else
        {
            var trigger = Query(new byte[] { 2, 2, 0 }, r => { ObdProtocol.Reply(r, 2, 5, 2, 0); return (r[3], r[4]); });
            if (trigger.Status != ObdStatus.Supported) freeze = new(trigger.Status, null, trigger.Detail);
            else if (trigger.Value == (0, 0)) freeze = new(ObdStatus.Supported, new(null, [], "ECU reports no stored freeze-frame DTC."));
            else
            {
                var values = new List<ObdFrameValue>();
                foreach (byte pid in ObdProtocol.FramePids.Where(p => ObdProtocol.Supports(support.Value, p)))
                {
                    var value = Query(new byte[] { 2, pid, 0 }, r => ObdProtocol.FrameValue(pid, r));
                    values.Add(value.Value ?? new(pid, $"PID {pid:X2}", null, "", value.Status, value.Detail));
                }
                freeze = new(ObdStatus.Supported, new(ObdProtocol.DecodeCode(trigger.Value.Item1, trigger.Value.Item2), values, "ECU freeze frame 0; only supported basic parameters are requested."));
            }
        }
        return new(DateTimeOffset.UtcNow, transport.Adapter, identity, readiness, stored, pending, permanent, freeze, usable);
    }

    public ObdClearResult Clear(string reportDirectory, bool confirmed, CancellationToken cancel = default)
    {
        if (!confirmed) throw new InvalidOperationException("Clearing codes requires explicit confirmation.");
        var before = Scan(cancel);
        if (!before.ConnectionUsable || before.Stored.Status != ObdStatus.Supported)
            throw new ObdPreflightException("Read stored codes successfully before clearing. No clear command was sent." + (before.ConnectionUsable ? "" : " Reconnect before trying again."), before.ConnectionUsable);
        // Publish a durable before-report before any state-changing request.
        Directory.CreateDirectory(reportDirectory);
        string stem = Path.Combine(reportDirectory, "obd-clear-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        string report = stem + "-before.json", journal = stem + ".jsonl";
        ObdReports.SaveNew(report, before);
        bool firstRecord = true;
        void Record(string stage, string? detail = null)
        {
            byte[] line = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, stage, detail }) + "\n");
            using var log = new FileStream(journal, firstRecord ? FileMode.CreateNew : FileMode.Append, FileAccess.Write, FileShare.Read, 1, FileOptions.WriteThrough);
            log.Write(line); log.Flush(true); firstRecord = false;
        }
        string? journalProblem = null;
        void Note(string stage, string? detail = null)
        {
            try { Record(stage, detail); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { journalProblem = " Clear journal could not be updated: " + e.Message; }
        }
        double rpm;
        try { rpm = ObdProtocol.Rpm(transport.ExchangeObd([1, 0x0C])); }
        catch (NegativeResponseException e) { throw new ObdPreflightException("ECU declined the engine-speed check. No clear command was sent. " + e.Message, true); }
        catch (Exception e) when (e is TransportException or InvalidDataException) { throw new ObdPreflightException("Engine speed could not be confirmed. No clear command was sent. Reconnect before trying again. " + e.Message, false); }
        if (rpm != 0) throw new ObdPreflightException("Engine speed must be zero to clear codes. No clear command was sent.", true);
        cancel.ThrowIfCancellationRequested();
        Record("clear.prepared", "Engine stopped; before-report retained. Request will be sent once.");
        try
        {
            byte[] reply = transport.ExchangeObd([4], clearConfirmed: true);
            ObdProtocol.Reply(reply, 4, 1);
        }
        catch (NegativeResponseException e)
        { Note("clear.rejected", e.Message); return new(false, false, "ECU rejected clearing. " + e.Message + journalProblem, before, null, report, journal, true); }
        catch (Exception e) when (e is TransportException or InvalidDataException)
        { Note("clear.unknown", e.Message); return new(false, true, "Clear result is unknown. Reconnect and read codes; the clear request was not repeated. " + e.Message + journalProblem, before, null, report, journal, false); }
        Note("clear.acknowledged");
        ObdScan? after;
        try { after = Scan(cancel); }
        catch (OperationCanceledException) { Note("rescan.cancelled"); return new(true, false, "ECU acknowledged clearing. Rescan was stopped; read codes again to check current status." + journalProblem, before, null, report, journal, true); }
        string detail = after.ConnectionUsable ? "ECU acknowledged clearing. Current codes and readiness were read again; faults may return and permanent codes may remain." : "ECU acknowledged clearing; the following scan was incomplete. Reconnect and read codes again.";
        try { ObdReports.SaveNew(stem + "-after.json", after); Note("rescan.saved", detail); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { detail += " After-report could not be saved: " + e.Message; Note("rescan.save_failed", e.Message); }
        return new(true, false, detail + journalProblem, before, after, report, journal, after.ConnectionUsable);
    }
}

public static class ObdReports
{
    public static void SaveNew(string path, ObdScan scan)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, scope = "Engine ECU OBD over 500 kbit/s CAN (7E0/7E8)", scan }, new JsonSerializerOptions { WriteIndented = true });
        path = Path.GetFullPath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, path, overwrite: false);
        }
        finally { try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
}

public static partial class ObdDescriptions
{
    [GeneratedRegex("^([PCBU][0-3][0-9A-F]{3}) (.+?)(?: - (?:DTC .+|Reporting Level|Monitor .+))?$")]
    private static partial Regex Title();
    public static Func<string, string?> From(IEnumerable<TuningDefinition> definitions, string? calibration)
    {
        var all = definitions.ToArray();
        var matching = all.Where(d => calibration is not null && (calibration == d.Calibration || calibration == d.Identity)).ToArray();
        bool exact = matching.Length == 1;
        var rows = (exact ? matching : all).SelectMany(d => d.Tables).Select(t => Title().Match(t.Name)).Where(m => m.Success)
            .Where(m => exact || m.Groups[1].Value.StartsWith("P0", StringComparison.Ordinal))
            .GroupBy(m => m.Groups[1].Value).Select(g => (Code: g.Key, Names: g.Select(m => m.Groups[2].Value).Distinct().ToArray()))
            .Where(g => g.Names.Length == 1).ToDictionary(g => g.Code, g => g.Names[0], StringComparer.Ordinal);
        return code => rows.GetValueOrDefault(code);
    }
}
