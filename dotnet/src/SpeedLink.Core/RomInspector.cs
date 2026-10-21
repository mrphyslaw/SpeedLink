using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpeedLink.Core;

public sealed record WordGuard(int Address, uint Value);
public sealed record RomProfile(string Calibration, string Model, int IdentityOffset, string Identity,
    string BootSha256, int ChecksumStart, int CorrectionAddress, WordGuard[] LayoutWords,
    int? LoopAddress, int? LoopSize, string? LoopSha256);

public sealed record RomInspection(int Size, string? Calibration, string? Model, string Sha256,
    string BootSha256, bool ProfileRecognized, bool IdentityValid, bool BootRecognized,
    bool MarkersValid, bool ChecksumLayoutRecognized, string? Checksum, string? ExpectedChecksum,
    bool? ChecksumValid, int? ChecksumStart, int? CorrectionAddress, bool CanCorrectChecksum,
    bool ProfileValid)
{
    public object ToManifest() => new Dictionary<string, object?>
    {
        ["size"] = Size,
        ["calibration"] = Calibration,
        ["model"] = Model,
        ["sha256"] = Sha256,
        ["bootSHA256"] = BootSha256,
        ["profileRecognized"] = ProfileRecognized,
        ["identityValid"] = IdentityValid,
        ["bootRecognized"] = BootRecognized,
        ["markersValid"] = MarkersValid,
        ["checksumLayoutRecognized"] = ChecksumLayoutRecognized,
        ["checksum"] = Checksum,
        ["expectedChecksum"] = ExpectedChecksum,
        ["checksumValid"] = ChecksumValid,
        ["checksumStart"] = ChecksumStart,
        ["correctionAddress"] = CorrectionAddress,
        ["canCorrectChecksum"] = CanCorrectChecksum,
        ["profileValid"] = ProfileValid,
        ["vehicleMatchVerified"] = false
    };
}

public sealed class RomInspector
{
    public const int RomSize = 0x100000;
    public const int BootSize = 0x8000;
    public const uint ChecksumTarget = 0x5AA55AA5;
    public static IReadOnlyList<RomProfile> KnownProfiles { get; } = LoadProfiles();
    private readonly IReadOnlyList<RomProfile> profiles;
    public RomInspector(IReadOnlyList<RomProfile>? profiles = null) => this.profiles = profiles ?? KnownProfiles;
    private static IReadOnlyList<RomProfile> LoadProfiles()
    {
        using var stream = typeof(RomInspector).Assembly.GetManifestResourceStream("SpeedLink.Core.Profiles.json")
            ?? throw new InvalidOperationException("Known-ROM metadata is missing.");
        return Array.AsReadOnly(JsonSerializer.Deserialize<RomProfile[]>(stream) ?? throw new InvalidDataException("Invalid ROM metadata."));
    }
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public RomProfile? ProfileFor(ReadOnlySpan<byte> data)
    {
        RomProfile? found = null;
        foreach (var profile in profiles)
            if (data.Length >= profile.IdentityOffset + 9 && data.Slice(profile.IdentityOffset, 9).SequenceEqual(Encoding.ASCII.GetBytes(profile.Calibration)))
            {
                if (found is not null)
                    return null;
                found = profile;
            }
        return found;
    }
    public static uint Sum(ReadOnlySpan<byte> data, RomProfile profile)
    {
        if (data.Length != RomSize)
            throw new InvalidDataException("A complete 1 MiB ROM is required.");
        uint sum = 0;
        for (var at = profile.ChecksumStart; at < data.Length; at += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4)));
        return sum;
    }
    public RomInspection Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length != RomSize)
            throw new InvalidDataException($"Expected {RomSize:N0} bytes; this file has {data.Length:N0}.");
        var profile = ProfileFor(data);
        var boot = Hash(data[..BootSize]);
        var markers = data[0x8002] == 0x5A && data[0x8003] == 0xA5 && data[^2] == 0xA5 && data[^1] == 0x5A;
        if (profile is null)
            return new(data.Length, null, null, Hash(data), boot, false, false, false, markers, false, null, null, null, null, null, false, false);
        var identity = data.Slice(profile.IdentityOffset, profile.Identity.Length).SequenceEqual(Encoding.ASCII.GetBytes(profile.Identity));
        var bootKnown = boot == profile.BootSha256;
        var layout = identity;
        foreach (var guard in profile.LayoutWords)
            layout &= BinaryPrimitives.ReadUInt32BigEndian(data.Slice(guard.Address, 4)) == guard.Value;
        if (profile.LoopAddress is int at && profile.LoopSize is int size)
            layout &= Hash(data.Slice(at, size)) == profile.LoopSha256;
        if (profile.Calibration == "L38KEH000")
            layout &= bootKnown;
        uint? sum = layout ? Sum(data, profile) : null;
        bool? valid = sum.HasValue ? sum.Value == ChecksumTarget : null;
        var correct = identity && bootKnown && markers && layout;
        return new(data.Length, profile.Calibration, profile.Model, Hash(data), boot, true, identity, bootKnown,
            markers, layout, sum?.ToString("X8"), layout ? ChecksumTarget.ToString("X8") : null, valid,
            profile.ChecksumStart, profile.CorrectionAddress, correct, correct && valid == true);
    }
    public byte[] CorrectedCopy(ReadOnlySpan<byte> data)
    {
        var info = Inspect(data);
        if (!info.CanCorrectChecksum)
            throw new InvalidDataException("Checksum correction requires a recognized identity, boot and checksum layout.");
        var profile = ProfileFor(data)!;
        var output = data.ToArray();
        var old = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(profile.CorrectionAddress, 4));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(profile.CorrectionAddress, 4), unchecked(old + ChecksumTarget - Sum(data, profile)));
        if (!Inspect(output).ProfileValid)
            throw new InvalidDataException("Checksum-corrected copy failed verification.");
        return output;
    }
}
