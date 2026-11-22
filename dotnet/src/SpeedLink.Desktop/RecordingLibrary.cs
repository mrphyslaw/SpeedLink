using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SpeedLink.Desktop;

internal sealed record RecordingLibraryEntry(string CsvPath, DateTimeOffset StartedUtc, TimeSpan? Duration,
    string Calibration, string Status, bool HasSamples)
{
    public string StartTime => StartedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string DurationText => Duration?.ToString("g", CultureInfo.CurrentCulture) ?? "—";
}

internal static class RecordingLibrary
{
    internal static IReadOnlyList<RecordingLibraryEntry> Scan(string root, CancellationToken cancellation)
    {
        if (!Directory.Exists(root)) return [];
        var entries = new List<RecordingLibraryEntry>();
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            cancellation.ThrowIfCancellationRequested();
            string csv = Path.Combine(folder, "log.csv");
            if (File.Exists(csv)) entries.Add(ReadEntry(csv, Path.Combine(folder, "session.json")));
        }
        return entries.OrderByDescending(entry => entry.StartedUtc).ThenBy(entry => entry.CsvPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static RecordingLibraryEntry ReadEntry(string csv, string manifest)
    {
        var fallback = new RecordingLibraryEntry(csv, new(File.GetLastWriteTimeUtc(csv)), null, "Unknown", "Session details unavailable", true);
        if (!File.Exists(manifest)) return fallback;
        try
        {
            using var stream = File.Open(manifest, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > 128 * 1024) return fallback;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return fallback;
            DateTimeOffset? started = ReadDate(root, "startedUtc"), ended = ReadDate(root, "endedUtc");
            var duration = started is { } start && ended is { } end && end >= start ? end - start : (TimeSpan?)null;
            string calibration = root.TryGetProperty("identity", out var identity) && identity.ValueKind == JsonValueKind.Object
                ? ReadText(identity, "calibration") ?? fallback.Calibration
                : fallback.Calibration;
            bool hasSamples = !root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Number || !rows.TryGetInt64(out long count) || count > 0;
            string status = ReadText(root, "status") switch
            {
                "Stopped" => "Saved",
                "Recording or interrupted" or "Stopped or interrupted" => "Interrupted",
                string value when value.StartsWith("Stopped after error:", StringComparison.Ordinal) => "Stopped after error",
                _ => fallback.Status
            };
            return new(csv, started ?? fallback.StartedUtc, duration, calibration, hasSamples ? status : "No samples recorded", hasSamples);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return fallback;
        }
    }

    private static string? ReadText(JsonElement source, string property)
        => source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ReadDate(JsonElement source, string property)
        => source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date) ? date : null;
}
