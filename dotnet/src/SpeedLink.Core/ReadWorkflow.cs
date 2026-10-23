using System.Buffers.Binary;
using System.Text;

namespace SpeedLink.Core;

public static class SecurityAccess
{
    public static byte[] SeedKey(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 3)
            throw new InvalidDataException("The boot seed must contain exactly three bytes.");
        uint state = 0xC541A9;
        foreach (var value in seed.ToArray().Concat("MazdA"u8.ToArray()))
        {
            int current = value;
            for (int bit = 0; bit < 8; bit++)
            {
                state = ((state >> 1) ^ (((state ^ (uint)current) & 1) != 0 ? 0x909028u : 0u)) & 0xFFFFFF;
                current >>= 1;
            }
        }
        return [(byte)((state >> 4) & 255), (byte)(((state >> 8) & 240) | ((state >> 20) & 15)), (byte)(((state << 4) & 240) | ((state >> 16) & 15))];
    }
}
public sealed record ReadProgress(double Fraction, string Message);
public sealed record BackupResult(string Path, RomInspection Inspection);

public sealed class ReadWorkflow
{
    private readonly BackupStore store;
    private readonly RomInspector inspector;
    public ReadWorkflow(BackupStore? store = null, RomInspector? inspector = null)
    {
        this.store = store ?? new();
        this.inspector = inspector ?? new();
    }
    public static EcuIdentity Identify(IReadTransport transport)
    {
        string? vin = null, calibration = null, vinStatus = null, calibrationStatus = null;
        foreach (var code in new byte[] { 0x02, 0x04 })
        {
            try
            {
                var response = transport.Exchange([0x09, code]);
                if (response.Length < 3 || response[0] != 0x49 || response[1] != code || response[2] != 1)
                    throw new TransportException("Unrecognized identification response format.");
                var text = response.AsSpan(3).TrimEnd((byte)0);
                if (text.IsEmpty || text.ToArray().Any(c => c < 32 || c > 126))
                    throw new TransportException("Invalid ECU identity text.");
                var value = Encoding.ASCII.GetString(text);
                if (code == 0x02)
                {
                    if (value.Length != 17 || value.Any(c => !"ABCDEFGHJKLMNPRSTUVWXYZ0123456789".Contains(c)))
                        throw new TransportException("Invalid VIN response.");
                    vin = value;
                }
                else
                    calibration = value;
            }
            catch (NegativeResponseException error) { if (code == 0x02) vinStatus = error.Message; else calibrationStatus = error.Message; }
        }
        return new(transport.Mode, transport.Adapter, vin, calibration, vinStatus, calibrationStatus);
    }
    public static void CheckEngineStopped(IReadTransport transport)
    {
        var response = transport.Exchange([0x01, 0x0C]);
        if (response.Length != 4 || response[0] != 0x41 || response[1] != 0x0C)
            throw new TransportException("Could not establish engine RPM. Read was not started.");
        var rpm = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2)) / 4.0;
        if (rpm != 0)
            throw new TransportException($"Engine is running ({rpm:g} RPM). Switch it off before reading.");
    }
    public BackupResult ReadBackup(IReadTransport transport, EcuIdentity expected, string vault, IProgress<ReadProgress>? progress = null, CancellationToken cancellation = default)
    {
        if (transport.Mode == "hardware")
        {
            if (transport.ReadAttempted)
                throw new TransportException("Reconnect and identify before another read.");
            transport.ReadAttempted = true;
        }
        var (folder, journal) = store.CreateOperation(vault, transport.Mode, "read_backup");
        transport.Diagnostics?.AttachJournal(journal.Path);
        transport.Diagnostics?.Record("read.started", new { chunkBytes = transport.ReadChunkSize });
        using (journal)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var identity = Identify(transport);
                if (identity.Vin != expected.Vin || identity.Calibration != expected.Calibration)
                    throw new TransportException("ECU identity changed. Reconnect and identify again.");
                CheckEngineStopped(transport);
                journal.Append("identity", new()
                {
                    ["identity"] = identity.ToManifest()
                });
                cancellation.ThrowIfCancellationRequested();
                var session = transport.Exchange([0x10, 0x87]);
                if (session.Length < 2 || session[0] != 0x50 || session[1] != 0x87)
                    throw new TransportException("ECU did not enter read session 87.");
                cancellation.ThrowIfCancellationRequested();
                var seed = transport.Exchange([0x27, 0x01]);
                if (seed.Length != 5 || seed[0] != 0x67 || seed[1] != 0x01)
                    throw new TransportException("Unexpected security seed format.");
                if (!seed.AsSpan(2).SequenceEqual(new byte[3]))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var response = transport.Exchange([0x27, 0x02, .. SecurityAccess.SeedKey(seed.AsSpan(2))]);
                    if (!response.AsSpan().SequenceEqual(new byte[] { 0x67, 0x02 }))
                        throw new TransportException("Read-session key was not acknowledged. No retry was attempted.");
                }
                journal.Append("read_session_ready");
                var hashes = new List<string>();
                byte[] data = [];
                int chunk = transport.ReadChunkSize;
                if (chunk is < 1 or > 3968)
                    throw new TransportException("Unsupported read chunk size.");
                for (int pass = 1; pass <= 2; pass++)
                {
                    transport.Diagnostics?.Record("read.pass_started", new { pass });
                    data = new byte[RomInspector.RomSize];
                    using var file = store.FileSystem.CreateNew(System.IO.Path.Combine(folder, $"pass{pass}.read"));
                    for (int address = 0; address < data.Length; address += chunk)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int count = Math.Min(chunk, data.Length - address);
                        byte[] request = new byte[7];
                        request[0] = 0x23;
                        BinaryPrimitives.WriteInt32BigEndian(request.AsSpan(1, 4), address);
                        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(5, 2), (ushort)count);
                        journal.Append("read_issued", new()
                        {
                            ["passNumber"] = pass,
                            ["address"] = address,
                            ["bytes"] = count
                        });
                        var response = transport.Exchange(request);
                        if (response.Length != count + 1 || response[0] != 0x63)
                            throw new TransportException($"Short or malformed read at 0x{address:X6}; expected {count} bytes.");
                        var block = response.AsSpan(1).ToArray();
                        DurableStorage.Write(file, block);
                        block.CopyTo(data, address);
                        journal.Append("read_saved", new()
                        {
                            ["passNumber"] = pass,
                            ["address"] = address,
                            ["bytes"] = count,
                            ["sha256"] = RomInspector.Hash(block)
                        });
                        progress?.Report(new(((pass - 1) * RomInspector.RomSize + address + count) / (2.0 * RomInspector.RomSize), $"Reading pass {pass}/2 · 0x{address + count:X6}"));
                    }
                    hashes.Add(RomInspector.Hash(data));
                    journal.Append("read_pass_complete", new()
                    {
                        ["passNumber"] = pass,
                        ["sha256"] = hashes[^1]
                    });
                }
                if (hashes[0] != hashes[1])
                    throw new TransportException("The two ROM reads differ. Both captures were retained; no verified backup was created.");
                var inspection = inspector.Inspect(data);
                var path = store.Seal(folder, data, identity, inspection, hashes[0], hashes[1], transport.Mode);
                journal.Append("completed", new()
                {
                    ["rom"] = path,
                    ["sha256"] = hashes[0],
                    ["profileValid"] = inspection.ProfileValid
                });
                progress?.Report(new(1, "Backup verified · two matching reads"));
                return new(path, inspection);
            }
            catch (Exception error)
            {
                journal.RecordStopped(new()
                {
                    ["reason"] = error.Message,
                    ["noAutomaticRetry"] = true
                });
                throw;
            }
        }
    }
}
