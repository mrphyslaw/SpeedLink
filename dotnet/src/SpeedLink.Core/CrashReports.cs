using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SpeedLink.Core;

public sealed record CrashReport(int SchemaVersion, string Id, DateTimeOffset TimeUtc, string Version,
    string Runtime, string OperatingSystem, int ProcessBits, string Origin, bool Fatal, string ExceptionType,
    string Message, string Stack, string Activity);

/// <summary>Small local reports only: no process dumps, ROM images or automatic uploads.</summary>
public sealed class CrashReports(params string[] roots)
{
    public const int RetainedReports = 10;
    public const int MaximumBytes = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public IReadOnlyList<string> Roots { get; } = roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static string Bounded(string value, int length) => value[..Math.Min(value.Length, length)];
    public string? Capture(Exception error, string version, string origin, bool fatal, string activity, DiagnosticRedactor? redactor = null)
    {
        redactor ??= new();
        // Do not serialize exception Data, object graphs, arguments, source files or inner payloads.
        var report = new CrashReport(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            Bounded(version, 100), RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, IntPtr.Size * 8,
            Bounded(origin, 100), fatal, error.GetType().FullName ?? "Exception",
            Bounded(redactor.Text(Bounded(error.Message, 4096)), 4096),
            Bounded(redactor.Text(new StackTrace(error, false).ToString()), 8192), Bounded(redactor.Text(activity), 200));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        if (bytes.Length > MaximumBytes) return null;
        foreach (string root in Roots)
        {
            string? pending = null;
            try
            {
                Directory.CreateDirectory(root); DiagnosticSession.RejectLink(root);
                string destination = Path.Combine(root, report.Id + ".json"); pending = destination + ".pending";
                using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { output.Write(bytes); output.Flush(true); }
                File.Move(pending, destination); pending = null;
                // A retention failure must not lose a successfully written crash report.
                try { Prune(root); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
                return destination;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
            finally { if (pending is not null) try { File.Delete(pending); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { } }
        }
        return null;
    }
    private static void Prune(string root)
    {
        foreach (string path in Directory.EnumerateFiles(root, "*.json").Where(IsReportName).OrderByDescending(File.GetLastWriteTimeUtc).Skip(RetainedReports))
        { DiagnosticSession.RejectLink(path); File.Delete(path); File.Delete(path + ".seen"); }
    }
    private static bool IsReportName(string path) => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _);
    private bool Contains(string path) => IsReportName(path) && Roots.Contains(Path.GetDirectoryName(Path.GetFullPath(path))!, StringComparer.OrdinalIgnoreCase);
    public CrashReport Read(string path)
    {
        if (!Contains(path)) throw new InvalidDataException("Not a saved crash report.");
        DiagnosticSession.RejectLink(Path.GetDirectoryName(path)!); DiagnosticSession.RejectLink(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumBytes) throw new InvalidDataException("Crash report exceeds its size limit.");
        var report = JsonSerializer.Deserialize<CrashReport>(file) ?? throw new InvalidDataException("Empty crash report.");
        if (report.SchemaVersion != 1 || report.Id != Path.GetFileNameWithoutExtension(path)) throw new InvalidDataException("Invalid crash report identity.");
        return report;
    }
    public IReadOnlyList<string> List(bool unseenOnly = false)
    {
        var reports = new List<string>();
        foreach (string root in Roots)
            try
            {
                if (!Directory.Exists(root)) continue;
                DiagnosticSession.RejectLink(root);
                foreach (string path in Directory.EnumerateFiles(root, "*.json").Where(IsReportName))
                    try { _ = Read(path); if (!unseenOnly || !File.Exists(path + ".seen")) reports.Add(path); }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return reports.OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
    }
    public void Acknowledge(string path)
    {
        _ = Read(path);
        using var file = new FileStream(path + ".seen", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Flush(true);
    }
    public void Export(string path, string destination)
    {
        var report = Read(path);
        string pending = destination + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, true))
                {
                    using (var output = zip.CreateEntry("crash.json").Open()) JsonSerializer.Serialize(output, report, JsonOptions);
                    using var summary = new StreamWriter(zip.CreateEntry("summary.txt").Open());
                    summary.Write($"SpeedLink {report.Version}\n{report.TimeUtc:O}\n{report.Origin} · {(report.Fatal ? "Closed unexpectedly" : "Background task error")}\n{report.ExceptionType}: {report.Message}\nActivity: {report.Activity}\n\nNo ROM images, recordings, memory dumps or automatic uploads.\n");
                }
                file.Flush(true);
            }
            File.Move(pending, destination);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
