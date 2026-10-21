using System.Text.Json;
using System.Xml;

namespace SpeedLink.Core;

public enum DefinitionKind { Editor, Logger }
public sealed record DefinitionEntry(DefinitionKind Kind, string Calibration, string Version, int Controls,
    string Sha256, bool Imported, string? InstalledSha256, string? Problem)
{
    public string Source => Imported ? "Imported" : "Bundled";
}
public sealed record DefinitionCandidate(DefinitionEntry Entry, byte[] Content);

/// <summary>A local overlay over embedded definitions. No firmware, executable plugins or ECU operations.</summary>
public sealed class DefinitionLibrary
{
    public const int MaximumXmlBytes = 8_000_000;
    public string Folder { get; }
    private static readonly Lazy<IReadOnlyDictionary<(DefinitionKind, string), byte[]>> bundled = new(() =>
    {
        var assembly = typeof(DefinitionLibrary).Assembly;
        var result = new Dictionary<(DefinitionKind, string), byte[]>();
        foreach (var kind in Enum.GetValues<DefinitionKind>())
            foreach (var profile in RomInspector.KnownProfiles)
            {
                string name = kind == DefinitionKind.Editor ? $"SpeedLink.Core.Tuning.{profile.Calibration}.xml" : $"SpeedLink.Core.Logging.{profile.Calibration}.logger.json";
                using var input = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Bundled definition is missing.");
                using var memory = new MemoryStream(); input.CopyTo(memory); result.Add((kind, profile.Calibration), memory.ToArray());
            }
        return result;
    });
    public DefinitionLibrary(string folder) => Folder = Path.GetFullPath(folder);
    public static byte[] BundledBytes(DefinitionKind kind, string calibration) => (byte[])bundled.Value[(kind, calibration)].Clone();
    private string FilePath(DefinitionKind kind, string calibration)
    {
        if (!RomInspector.KnownProfiles.Any(p => p.Calibration == calibration)) throw new InvalidDataException("This ROM layout needs application support before a definition can be imported.");
        return Path.Combine(Folder, kind == DefinitionKind.Editor ? calibration + ".xml" : calibration + ".logger.json");
    }
    private static int Limit(DefinitionKind kind) => kind == DefinitionKind.Editor ? MaximumXmlBytes : 1_048_576;
    private static byte[] Read(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 2 || stream.Length > limit) throw new InvalidDataException("Definition file exceeds its size limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Definition changed while reading.");
        return bytes;
    }
    public DefinitionCandidate Preview(string path)
    {
        var kind = Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? DefinitionKind.Editor :
            Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase) ? DefinitionKind.Logger :
            throw new InvalidDataException("Choose a ROM editor XML or logger JSON definition.");
        return Parse(kind, Read(path, Limit(kind)));
    }
    private static DefinitionCandidate Parse(DefinitionKind kind, byte[] bytes)
    {
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Unknown definition type.");
        if (bytes.Length < 2 || bytes.Length > Limit(kind)) throw new InvalidDataException("Definition file exceeds its size limit.");
        try
        {
            string calibration, version; int controls;
            if (kind == DefinitionKind.Editor)
            {
                using var stream = new MemoryStream(bytes, writable: false); var definition = TuningDefinition.Parse(stream);
                DefinitionPolicy.ValidateEditor(definition); calibration = definition.Calibration; version = definition.Version; controls = definition.Tables.Count;
            }
            else
            {
                var definition = LoggerDefinition.Parse(bytes); DefinitionPolicy.ValidateLogger(definition, bytes);
                calibration = definition.SoftwareIds.Single(); version = definition.DefinitionVersion; controls = definition.Channels.Count;
            }
            return new(new(kind, calibration, version, controls, RomInspector.Hash(bytes), true, RomInspector.Hash(bytes), null), (byte[])bytes.Clone());
        }
        catch (Exception error) when (error is JsonException or XmlException or ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new InvalidDataException("The definition is invalid or uses an unsupported format: " + error.Message, error); }
    }
    public DefinitionSnapshot Load()
    {
        var entries = new List<DefinitionEntry>(); var editors = new List<TuningDefinition>(); var loggers = new List<LoggerDefinition>();
        foreach (var pair in bundled.Value.OrderBy(p => p.Key.Item1).ThenBy(p => p.Key.Item2, StringComparer.Ordinal))
        {
            var (kind, calibration) = pair.Key; var bytes = pair.Value; string? installedHash = null, problem = null; bool imported = false;
            try
            {
                string path = FilePath(kind, calibration);
                if (File.Exists(path))
                {
                    var installed = Read(path, Limit(kind)); installedHash = RomInspector.Hash(installed); var candidate = Parse(kind, installed);
                    if (candidate.Entry.Calibration != calibration) throw new InvalidDataException("Installed filename does not match its ROM identity.");
                    bytes = installed; imported = true;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                problem = "Imported definition could not be loaded; using bundled copy. " + error.Message;
                try { installedHash = CurrentToken(FilePath(kind, calibration), Limit(kind)); }
                catch (Exception tokenError) when (tokenError is IOException or UnauthorizedAccessException) { }
            }
            if (kind == DefinitionKind.Editor)
            {
                using var stream = new MemoryStream(bytes, writable: false); var definition = TuningDefinition.Parse(stream); editors.Add(definition);
                entries.Add(new(kind, calibration, definition.Version, definition.Tables.Count, RomInspector.Hash(bytes), imported, installedHash, problem));
            }
            else
            {
                var definition = LoggerDefinition.Parse(bytes); loggers.Add(definition);
                entries.Add(new(kind, calibration, definition.DefinitionVersion, definition.Channels.Count, RomInspector.Hash(bytes), imported, installedHash, problem));
            }
        }
        return new(editors.AsReadOnly(), loggers.AsReadOnly(), entries.AsReadOnly());
    }
    private FileStream Lock()
    {
        Directory.CreateDirectory(Folder);
        try { return new(Path.Combine(Folder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("Another SpeedLink window is updating definitions. Close it and try again.", error); }
    }
    private static void CheckCurrent(string path, string? expected, int limit)
    {
        string? actual = CurrentToken(path, limit);
        if (actual != expected) throw new IOException("Installed definition changed. Refresh the list before trying again.");
    }
    private static string? CurrentToken(string path, int limit)
    {
        if (!File.Exists(path)) return null;
        try { return RomInspector.Hash(Read(path, limit)); }
        catch (InvalidDataException)
        {
            var info = new FileInfo(path); return $"oversize:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
    }
    public void Install(DefinitionCandidate candidate, string? expectedInstalledHash)
    {
        var checkedCandidate = Parse(candidate.Entry.Kind, (byte[])candidate.Content.Clone());
        if (checkedCandidate.Entry.Calibration != candidate.Entry.Calibration || checkedCandidate.Entry.Sha256 != candidate.Entry.Sha256)
            throw new InvalidDataException("Definition changed after preview. Import it again.");
        string path = FilePath(candidate.Entry.Kind, candidate.Entry.Calibration);
        using var ownership = Lock(); CheckCurrent(path, expectedInstalledHash, Limit(candidate.Entry.Kind));
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(checkedCandidate.Content); stream.Flush(true); }
            if (RomInspector.Hash(Read(temp, Limit(candidate.Entry.Kind))) != checkedCandidate.Entry.Sha256) throw new IOException("Installed copy failed verification.");
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous"); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void UseBundled(DefinitionEntry entry)
    {
        string path = FilePath(entry.Kind, entry.Calibration);
        using var ownership = Lock(); CheckCurrent(path, entry.InstalledSha256, Limit(entry.Kind));
        if (File.Exists(path)) File.Move(path, path + ".previous", overwrite: true);
    }
}

public sealed record DefinitionSnapshot(IReadOnlyList<TuningDefinition> Editors, IReadOnlyList<LoggerDefinition> Loggers, IReadOnlyList<DefinitionEntry> Entries)
{
    public LoggerDefinition? LoggerFor(string? calibration) => Loggers.SingleOrDefault(p => p.MatchesCalibrationId(calibration));
    public (LoggerDefinition Definition, LoggerCalibration Calibration) BindLoggerRom(ReadOnlySpan<byte> image) => LoggerDefinition.BindKnownRom(image, Loggers);
}

internal static class DefinitionPolicy
{
    internal static void ValidateEditor(TuningDefinition definition)
    {
        var profile = RomInspector.KnownProfiles.SingleOrDefault(p => p.Calibration == definition.Calibration)
            ?? throw new InvalidDataException("This ROM layout needs application support before its editor definition can be imported.");
        if (definition.Identity != profile.Identity || definition.IdentityAddress != profile.IdentityOffset)
            throw new InvalidDataException("Definition identity must match the supported ROM layout.");
        var canonical = TuningDefinition.Known.Single(d => d.Calibration == definition.Calibration);
        var allowed = new byte[RomInspector.RomSize];
        foreach (var table in canonical.Tables) AddMask(allowed, table);
        foreach (var table in definition.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Name) || string.IsNullOrWhiteSpace(table.Category) ||
                Overlaps(table, 0x8000, 4) ||
                Overlaps(table, profile.IdentityOffset, profile.Identity.Length) || Overlaps(table, profile.CorrectionAddress, 4) ||
                profile.LayoutWords.Any(w => Overlaps(table, w.Address, 4)) ||
                profile.LoopAddress is int loop && Overlaps(table, loop, profile.LoopSize!.Value))
                throw new InvalidDataException("Definition overlaps protected ROM metadata or has an empty table name/category.");
            var requested = new byte[table.End - table.Address]; AddMask(requested, table, table.Address);
            for (int i = Math.Max(profile.ChecksumStart, table.Address); i < table.End; i++)
                if ((requested[i - table.Address] & ~allowed[i]) != 0)
                    throw new InvalidDataException("This definition adds program-area storage outside the supported editing layout. It needs updated application layout support.");
        }
    }
    private static bool Overlaps(TuningTable table, int start, int length) => table.Address < start + length && start < table.End;
    private static void AddMask(byte[] target, TuningTable table, int origin = 0)
    {
        if (table.Bits.Length == 0) { target.AsSpan(table.Address - origin, table.End - table.Address).Fill(255); return; }
        ulong mask = table.Bits.Aggregate(0UL, (bits, bit) => bits | (1UL << bit));
        for (int b = 0; b < table.Width; b++) target[table.Address + b - origin] |= (byte)(mask >> (8 * (table.BigEndian ? table.Width - b - 1 : b)));
    }
    internal static void ValidateLogger(LoggerDefinition definition, byte[] bytes)
    {
        if (definition.SoftwareIds.Count != 1) throw new InvalidDataException("Import one ROM logger profile at a time.");
        var canonical = LoggerDefinition.Known.SingleOrDefault(d => d.SoftwareIds.SequenceEqual(definition.SoftwareIds))
            ?? throw new InvalidDataException("This ROM needs application logger support before its definition can be imported.");
        if (definition.QueryIds.Except(canonical.QueryIds, StringComparer.Ordinal).Any())
            throw new InvalidDataException("This definition uses identifiers outside the application's read-only allowlist.");
        using var imported = JsonDocument.Parse(bytes);
        using var original = JsonDocument.Parse(DefinitionLibrary.BundledBytes(DefinitionKind.Logger, definition.SoftwareIds.Single()));
        if (!JsonElement.DeepEquals(imported.RootElement.GetProperty("codeFingerprints"), original.RootElement.GetProperty("codeFingerprints")))
            throw new InvalidDataException("Logger ROM compatibility fingerprints must match the supported layout.");
        if (!definition.ChannelIds.Contains("engine_rpm") || string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.DefinitionVersion) ||
            definition.Channels.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Group)))
            throw new InvalidDataException("Logger profile needs named parameters and engine speed.");
        var bindings = imported.RootElement.GetProperty("calibration").GetProperty("bindings");
        if (!bindings.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "map_gain_kpa_per_v", "map_offset_kpa" }))
            throw new InvalidDataException("Logger MAP calibration bindings are missing or unsupported.");
        foreach (var binding in bindings.EnumerateObject())
        {
            string operation = binding.Value.GetProperty("operation").GetString()!; var sources = binding.Value.GetProperty("sources");
            if (sources.GetArrayLength() is < 1 or > 8 || operation is not ("read" or "product") || operation == "read" && sources.GetArrayLength() != 1)
                throw new InvalidDataException("Invalid logger calibration binding.");
            foreach (var source in sources.EnumerateArray())
            {
                string address = source.GetProperty("offset").GetString()!;
                if (!address.StartsWith("0x", StringComparison.Ordinal) || !int.TryParse(address.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int offset) ||
                    offset < 0 || offset > RomInspector.RomSize - 4 || source.GetProperty("storage").GetString() != "float32" || source.GetProperty("byteOrder").GetString() != "big")
                    throw new InvalidDataException("Invalid logger calibration address or encoding.");
            }
        }
    }
}
