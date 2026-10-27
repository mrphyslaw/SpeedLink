using System.Security.Cryptography;
using System.Text.Json;

namespace SpeedLink.Core;

public sealed record TuningCodeSignature(string Calibration, int CodeStart, string Sha256);

/// <summary>Recognizes the reference program area while allowing all defined tuning values to change.</summary>
public sealed class TuningCodeGuard
{
    private readonly IReadOnlyList<TuningCodeSignature> signatures;
    public TuningCodeGuard(IReadOnlyList<TuningCodeSignature>? signatures = null) => this.signatures = signatures ?? Known;
    public static IReadOnlyList<TuningCodeSignature> Known { get; } = Load();
    private static IReadOnlyList<TuningCodeSignature> Load()
    {
        using var stream = typeof(TuningCodeGuard).Assembly.GetManifestResourceStream("SpeedLink.Core.TuningCode.json") ?? throw new InvalidDataException("Tuning program signatures are missing.");
        return Array.AsReadOnly(JsonSerializer.Deserialize<TuningCodeSignature[]>(stream) ?? throw new InvalidDataException("Invalid tuning program signatures."));
    }
    public bool Matches(ReadOnlySpan<byte> image, TuningDefinition definition, RomProfile profile)
    {
        var signature = signatures.SingleOrDefault(s => s.Calibration == definition.Calibration);
        return signature is not null && signature.CodeStart == profile.ChecksumStart && signature.Sha256 == Hash(image, definition, profile);
    }
    public static string Hash(ReadOnlySpan<byte> image, TuningDefinition definition, RomProfile profile)
    {
        if (image.Length != RomInspector.RomSize) throw new InvalidDataException("Complete ROM required for program identification.");
        int start = profile.ChecksumStart;
        var program = image[start..].ToArray();
        foreach (var table in definition.Tables)
        {
            if (table.Bits.Length > 0)
            {
                ulong mask = table.Bits.Aggregate(0UL, (value, bit) => value | (1UL << bit));
                for (int b = 0; b < table.Width; b++)
                {
                    int offset = table.Address + b - start;
                    if (offset >= 0 && offset < program.Length)
                        program[offset] &= (byte)~(mask >> (8 * (table.BigEndian ? table.Width - b - 1 : b)));
                }
            }
            else
            {
                int from = Math.Max(start, table.Address), to = Math.Min(image.Length, table.End);
                if (to > from) program.AsSpan(from - start, to - from).Clear();
            }
        }
        if (profile.CorrectionAddress >= start) program.AsSpan(profile.CorrectionAddress - start, 4).Clear();
        return Convert.ToHexStringLower(SHA256.HashData(program));
    }
}
