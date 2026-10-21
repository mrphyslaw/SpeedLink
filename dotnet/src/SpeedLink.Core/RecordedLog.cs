using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace SpeedLink.Core;

public sealed record RecordedSample(double ScanSeconds, double? SampleSeconds, double? Value, string Status)
{
    public double? AgeSeconds => SampleSeconds is double time ? ScanSeconds - time : null;
}
public sealed record RecordedChannel(string Id, string Name, string Unit, IReadOnlyList<RecordedSample> Samples)
{
    public int AvailableCount => Samples.Count(p => p.Value.HasValue);
    public double? MaximumAgeSeconds => Samples.Select(p => p.AgeSeconds).Max();
    public double MeasuredHz
    {
        get
        {
            var times = Samples.Where(p => p.Value.HasValue && p.SampleSeconds.HasValue).Select(p => p.SampleSeconds!.Value).Distinct().ToArray();
            return times.Length > 1 && times[^1] > times[0] ? (times.Length - 1) / (times[^1] - times[0]) : 0;
        }
    }
    // Nearest preceding recorded observation: no interpolation across missing data.
    public RecordedSample? At(double seconds)
    {
        int low = 0, high = Samples.Count - 1, found = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (Samples[middle].ScanSeconds <= seconds) { found = middle; low = middle + 1; } else high = middle - 1;
        }
        if (found < 0) return null;
        var sample = Samples[found];
        return sample.Value.HasValue && sample.SampleSeconds is double time && seconds - time > LiveLogger.MaximumSampleAge.TotalSeconds
            ? sample with { Value = null, Status = "Stale at cursor" } : sample;
    }
    public IReadOnlyList<IReadOnlyList<RecordedSample>> Plot(double start, double end, int columns)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start || columns is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(end));
        var segments = new List<IReadOnlyList<RecordedSample>>(); List<RecordedSample>? segment = null;
        var bucket = new List<(int Index, RecordedSample Sample)>(); int bucketId = -1, index = 0; double? previous = null;
        void Flush()
        {
            if (bucket.Count == 0) return;
            var chosen = new[] { bucket[0], bucket.MinBy(p => p.Sample.Value!.Value), bucket.MaxBy(p => p.Sample.Value!.Value), bucket[^1] };
            foreach (var point in chosen.DistinctBy(p => p.Index).OrderBy(p => p.Index)) segment!.Add(point.Sample);
            bucket.Clear();
        }
        foreach (var point in Samples)
        {
            index++;
            if (point.Value is null || point.SampleSeconds is not double time)
            { Flush(); segment = null; previous = null; bucketId = -1; continue; }
            if (time < start || time > end) continue;
            if (previous is double prior && time - prior > LiveLogger.MaximumSampleAge.TotalSeconds)
            { Flush(); segment = null; bucketId = -1; }
            if (segment is null) { segment = []; segments.Add(segment); }
            int key = Math.Min(columns - 1, (int)((time - start) / (end - start) * columns));
            if (key != bucketId) { Flush(); bucketId = key; }
            bucket.Add((index, point)); previous = time;
        }
        Flush(); return segments;
    }
}

/// <summary>Reads SpeedLink wide CSV recordings only. Never opens an adapter or modifies the recording.</summary>
public sealed class RecordedLog
{
    public const long MaximumFileBytes = 128 * 1024 * 1024;
    public const int MaximumCells = 1_000_000;
    public IReadOnlyList<RecordedChannel> Channels { get; }
    public IReadOnlyList<string> Warnings { get; }
    public DateTimeOffset StartedUtc { get; }
    public double DurationSeconds { get; }
    public int Rows { get; }
    private RecordedLog(IReadOnlyList<RecordedChannel> channels, IReadOnlyList<string> warnings, DateTimeOffset origin, double end, int rows)
    { Channels = channels; Warnings = warnings; StartedUtc = origin; DurationSeconds = end; Rows = rows; }

    public static RecordedLog Load(string path, CancellationToken cancellation = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("This log is too large to open (128 MiB limit).");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return Read(reader, cancellation);
    }
    public static RecordedLog Read(TextReader reader, CancellationToken cancellation = default)
    {
        using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        string[] header;
        try { header = parser.ReadFields() ?? throw new InvalidDataException("This log is empty."); }
        catch (MalformedLineException error) { throw new InvalidDataException("Malformed CSV header.", error); }
        if (header.Length < 6 || header.Length > 195 || (header.Length - 3) % 3 != 0 ||
            header[0] != "Timestamp UTC" || header[1] != "Elapsed (s)" || header[2] != "Scan")
            throw new InvalidDataException("Open a SpeedLink log.csv recording with per-channel timestamps and status.");
        int count = (header.Length - 3) / 3;
        var channels = new List<(string Id, string Name, string Unit, List<RecordedSample> Points)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int c = 3; c < header.Length; c += 3)
        {
            const string suffix = " sample UTC";
            if (!header[c + 1].EndsWith(suffix, StringComparison.Ordinal)) throw new InvalidDataException("Missing channel sample-time column.");
            string id = header[c + 1][..^suffix.Length]; int unit = header[c].LastIndexOf(" (", StringComparison.Ordinal);
            if (!Regex.IsMatch(id, "^[a-z0-9_]{1,80}$", RegexOptions.CultureInvariant) || !ids.Add(id) ||
                header[c + 2] != id + " status" || unit < 1 || !header[c].EndsWith(')'))
                throw new InvalidDataException("Invalid or duplicate channel columns.");
            channels.Add((id, header[c][..unit], header[c][(unit + 2)..^1], []));
        }
        var warnings = new List<string>(); DateTimeOffset? origin = null; double last = -1; long previousScan = 0; int rows = 0;
        double Number(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new InvalidDataException($"Invalid numeric value near row {rows + 1}.");
            return value;
        }
        DateTimeOffset Time(string text) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time.ToUniversalTime() : throw new InvalidDataException($"Invalid timestamp near row {rows + 1}.");
        while (!parser.EndOfData)
        {
            cancellation.ThrowIfCancellationRequested();
            string[] fields;
            try { fields = parser.ReadFields()!; }
            catch (MalformedLineException) when (parser.EndOfData && rows > 0)
            { warnings.Add("Incomplete final CSV row was skipped. Earlier complete rows are available."); break; }
            catch (MalformedLineException error) { throw new InvalidDataException("Malformed CSV recording.", error); }
            if (fields.Length != header.Length)
            {
                if (parser.EndOfData && rows > 0 && fields.Length < header.Length)
                { warnings.Add("Incomplete final CSV row was skipped. Earlier complete rows are available."); break; }
                throw new InvalidDataException("CSV row has the wrong number of fields.");
            }
            if ((long)(rows + 1) * count > MaximumCells) throw new InvalidDataException("This log has too many samples to open (one million channel observations).");
            DateTimeOffset utc = Time(fields[0]); double elapsed = Number(fields[1]);
            if (elapsed < 0 || elapsed < last || elapsed > (double)utc.Ticks / TimeSpan.TicksPerSecond || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long scan) || scan <= previousScan)
                throw new InvalidDataException("Recorded scan times or sequence numbers move backwards.");
            origin ??= utc - TimeSpan.FromSeconds(elapsed);
            if (Math.Abs((utc - origin.Value).TotalSeconds - elapsed) > .01) throw new InvalidDataException("Recorded clocks do not agree.");
            var pending = new RecordedSample[count];
            for (int c = 0; c < count; c++)
            {
                int column = 3 + c * 3; string status = fields[column + 2];
                if (status.Length is 0 or > 4096) throw new InvalidDataException("Invalid channel status.");
                double? value = fields[column].Length == 0 ? null : Number(fields[column]);
                double? sample = fields[column + 1].Length == 0 ? null : (Time(fields[column + 1]) - origin.Value).TotalSeconds;
                if (value.HasValue && (status != "OK" || sample is null) || sample > elapsed + .001)
                    throw new InvalidDataException("Channel value, timestamp and status do not agree.");
                if (sample < 0 || sample is double current && channels[c].Points.LastOrDefault(p => p.SampleSeconds.HasValue)?.SampleSeconds is double prior && current < prior)
                    throw new InvalidDataException("Channel sample time moves backwards.");
                pending[c] = new(elapsed, sample, value, status);
            }
            for (int c = 0; c < count; c++) channels[c].Points.Add(pending[c]);
            last = elapsed; previousScan = scan; rows++;
        }
        if (rows == 0 || origin is null) throw new InvalidDataException("This log contains no complete scans.");
        return new(channels.Select(c => new RecordedChannel(c.Id, c.Name, c.Unit, c.Points.AsReadOnly())).ToArray(),
            warnings.AsReadOnly(), origin.Value, last, rows);
    }
}
