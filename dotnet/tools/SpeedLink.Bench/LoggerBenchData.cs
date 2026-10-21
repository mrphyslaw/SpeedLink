using System.Text.Json;

// Developer fixture only; never linked into the desktop executable.
sealed class LoggerBenchData
{
    private readonly Dictionary<string, byte[][]> replies;
    private readonly Dictionary<string, int> counts = [];
    public LoggerBenchData(string vectors)
    {
        using var data = JsonDocument.Parse(File.ReadAllBytes(vectors));
        var all = data.RootElement.GetProperty("vectors").EnumerateArray().Where(v => v.GetProperty("type").GetString() == "decode")
            .Select(v => v.GetProperty("responseHex").GetString()!).Distinct().ToArray();
        replies = all.GroupBy(hex => "22" + hex.Substring(2, 4)).ToDictionary(g => g.Key, g => g.Select(Convert.FromHexString).ToArray());
    }
    public byte[]? Handle(byte[] request, string fault)
    {
        string key = Convert.ToHexString(request); counts.TryGetValue(key, out int count); counts[key] = count + 1;
        if (fault == "logger-unsupported" && key == "22A211") return [0x7F, 0x22, 0x31];
        if (fault == "logger-transient" && key == "221746" && count == 0) return [0x7F, 0x22, 0x22];
        if (fault == "logger-drop" && counts.Values.Sum() == 4) return null;
        if (!replies.TryGetValue(key, out var options)) return [0x7F, 0x22, 0x31];
        if (fault == "logger-delay") Thread.Sleep(count % 3 == 0 ? 75 : 10);
        byte[] result = options[count % options.Length];
        if (fault == "logger-short" && counts.Values.Sum() == 4) return result[..^1];
        return result;
    }
}
