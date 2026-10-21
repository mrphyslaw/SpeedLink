using System.Globalization;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public class RecordedLogTests
{
    private static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Csv(params (double Time, double? Value, string Status)[] points)
    {
        var lines = new List<string> { string.Join(',', new[] { "Timestamp UTC", "Elapsed (s)", "Scan", "Engine, RPM (rpm)", "engine_rpm sample UTC", "engine_rpm status" }.Select(Q)) };
        int scan = 0;
        foreach (var p in points)
            lines.Add(string.Join(',', new[] { DateTimeOffset.UnixEpoch.AddSeconds(p.Time).ToString("O"), p.Time.ToString("R", CultureInfo.InvariantCulture), (++scan).ToString(CultureInfo.InvariantCulture),
                p.Value?.ToString("R", CultureInfo.InvariantCulture) ?? "", p.Value.HasValue ? DateTimeOffset.UnixEpoch.AddSeconds(p.Time - .01).ToString("O") : "", p.Status }.Select(Q)));
        return string.Join('\n', lines) + "\n";
    }
    [Fact]
    public void ReadsQuotedNamesTimesUnitsMissingValuesAndSampleAges()
    {
        var log = RecordedLog.Read(new StringReader(Csv((1, 2000, "OK"), (2, null, "Unavailable, test\nnext line"), (3, 3000, "OK"))));
        Assert.Equal(3, log.Rows); Assert.Equal("Engine, RPM", log.Channels[0].Name); Assert.Equal("rpm", log.Channels[0].Unit);
        Assert.Equal(2, log.Channels[0].AvailableCount); Assert.Equal(.5, log.Channels[0].MeasuredHz, 9);
        Assert.Equal(.01, log.Channels[0].MaximumAgeSeconds!.Value, 9);
        Assert.Null(log.Channels[0].At(.5)); Assert.Equal(2000, log.Channels[0].At(1.5)!.Value);
        Assert.Null(log.Channels[0].At(2.5)!.Value); Assert.Equal(2, log.Channels[0].Plot(0, 3, 100).Count);
    }
    [Theory]
    [InlineData("\"partial")]
    [InlineData("\"2026-01-01\",\"4\"")]
    public void PreservesCompleteRowsAndWarnsAboutAnIncompleteTail(string tail)
    {
        var log = RecordedLog.Read(new StringReader(Csv((1, 2000, "OK")) + tail));
        Assert.Equal(1, log.Rows); Assert.Single(log.Warnings);
    }
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,5")]
    public void InvalidNumbersAreRejected(string number)
    {
        string csv = Csv((1, 2000, "OK")).Replace("\"2000\"", Q(number));
        Assert.Throws<InvalidDataException>(() => RecordedLog.Read(new StringReader(csv)));
    }
    [Fact]
    public void BadHeadersDuplicatesContradictoryStatusesAndBackwardsTimeAreRejected()
    {
        foreach (string invalid in new[] { "a,b,c\n", "\"unterminated", Csv((1, 2000, "Error")), Csv((2, 2000, "OK"), (1, 3000, "OK")),
            Csv((1, 2000, "OK")).Replace("engine_rpm status", "other status"),
            Csv((1, 2000, "OK")).Replace("1970-01-01T00:00:00.9900000+00:00", "1970-01-01T00:00:02.0000000+00:00") })
            Assert.Throws<InvalidDataException>(() => RecordedLog.Read(new StringReader(invalid)));
    }
    [Fact]
    public void PlotReductionPreservesNarrowSpikesAndGapBoundaries()
    {
        var samples = Enumerable.Range(1, 1000).Select(i => new RecordedSample(i / 1000.0, i / 1000.0, i == 457 ? 99 : i == 458 ? -44 : 1, "OK")).ToList();
        samples[600] = new(.601, null, null, "Unavailable");
        var channel = new RecordedChannel("test", "Test", "unit", samples);
        var plot = channel.Plot(0, 1, 10);
        Assert.Equal(2, plot.Count); Assert.Contains(plot.SelectMany(p => p), p => p.Value == 99); Assert.Contains(plot.SelectMany(p => p), p => p.Value == -44);
        Assert.All(plot, segment => Assert.True(segment.Zip(segment.Skip(1)).All(p => p.Second.SampleSeconds >= p.First.SampleSeconds)));
        Assert.True(plot.Sum(s => s.Count) < 100);
    }
    [Fact]
    public void LongMissingTimeSplitsThePlotAndLoadingCanBeCancelled()
    {
        var log = RecordedLog.Read(new StringReader(Csv((1, 1000, "OK"), (9, 2000, "OK"))));
        Assert.Equal(2, log.Channels[0].Plot(0, 10, 100).Count);
        Assert.Null(log.Channels[0].At(7)!.Value); Assert.Equal("Stale at cursor", log.Channels[0].At(7)!.Status);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => RecordedLog.Read(new StringReader(Csv((1, 1000, "OK"))), cancel.Token));
    }
    [Fact]
    public void RefusesOversizedFilesWithoutReadingThem()
    {
        using var temporary = new TempFolder(); string file = Path.Combine(temporary.Path, "large.csv");
        using (var stream = File.Create(file)) stream.SetLength(RecordedLog.MaximumFileBytes + 1);
        Assert.Throws<InvalidDataException>(() => RecordedLog.Load(file));
    }
    [Fact]
    public void ExtremeElapsedValuesCannotOverflowTheTimestampOrigin()
    {
        string csv = Csv((1, 2000, "OK")).Replace("\"1\",\"1\",\"2000\"", "\"1e300\",\"1\",\"2000\"");
        Assert.Throws<InvalidDataException>(() => RecordedLog.Read(new StringReader(csv)));
    }
    [Theory, MemberData(nameof(MultiRomLoggerTests.Profiles), MemberType = typeof(MultiRomLoggerTests))]
    public void ProductionRecorderRoundTripsEveryProfileWithoutChangingSource(string id, int channels, int queries, int vectors)
    {
        var profile = LoggerDefinition.ForCalibration(id)!; Assert.Equal(channels, profile.ChannelIds.Count); Assert.Equal(queries, profile.QueryIds.Count); Assert.True(vectors > 0);
        using var temporary = new TempFolder(); string folder;
        using (var recording = new LoggerRecording(temporary.Path, profile, LoggerCalibration.ExplicitMap(54, 1), new("test", "fixture", null, id),
            profile.ChannelIds, profile.Channels.ToDictionary(c => c.Id, c => c.Unit), null, "test", 5))
        {
            folder = recording.Folder;
            for (int scan = 1; scan <= 3; scan++)
            {
                var time = DateTimeOffset.UnixEpoch.AddSeconds(scan);
                recording.Append(new(scan, time, TimeSpan.FromSeconds(scan), profile.ChannelIds.ToDictionary(c => c,
                    c => new LoggerReading(c, scan, time.AddMilliseconds(-10), "OK")), []));
            }
            recording.Complete("Stopped");
        }
        string path = Path.Combine(folder, "log.csv"); string hash = RomInspector.Hash(File.ReadAllBytes(path));
        var log = RecordedLog.Load(path); Assert.Equal(channels, log.Channels.Count); Assert.Equal(3, log.Rows);
        Assert.All(log.Channels, c => Assert.Equal(3, c.AvailableCount)); Assert.Equal(hash, RomInspector.Hash(File.ReadAllBytes(path)));
    }
}
