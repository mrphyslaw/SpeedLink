using SpeedLink.Core;
namespace SpeedLink.Tests;

internal enum RecoveryStage
{
    Session, Seed, Key, Erase, Download, Transfer, Exit, Reset, Verify, Interrupted, Failed
}
internal sealed class RecoveryModel
{
    public const int Chunk = 3968, Start = 0x8000, Size = 0xF8000;
    private readonly byte[] image;
    private byte[]? key, inflight;
    public RecoveryStage Stage { get; private set; } = RecoveryStage.Session;
    public int Block
    {
        get; private set;
    }
    public int Acknowledged
    {
        get; private set;
    }
    public List<(string Event, Dictionary<string, object?> Fields)> Events { get; } = [];
    public static string Name(RecoveryStage value) => value switch { RecoveryStage.Exit => "transfer_exit", RecoveryStage.Verify => "needs_post_reset_verification", _ => value.ToString().ToLowerInvariant() };
    public RecoveryModel(byte[] image, byte[] backup)
    {
        Validate(image, backup);
        this.image = image.ToArray();
    }
    public static void Validate(byte[] image, byte[] backup)
    {
        var inspector = Fixture.Inspector();
        if (image.Length != RomInspector.RomSize || backup.Length != RomInspector.RomSize)
            throw new InvalidDataException("Complete images required.");
        if (!image.AsSpan(0, Start).SequenceEqual(backup.AsSpan(0, Start)))
            throw new InvalidDataException("Boot bytes differ.");
        foreach (var data in new[] { image, backup })
        {
            var info = inspector.Inspect(data);
            if (info.Calibration != "L38KEH000" || !info.ProfileValid)
                throw new InvalidDataException("Recovery profile mismatch.");
        }
    }
    public static byte[] Download(int address = Start, int size = Size)
    {
        if (address != Start || size != Size)
            throw new InvalidDataException("A complete application span is required.");
        return Fixture.Hex("3400008000000F8000");
    }
    public static IEnumerable<(int Address, byte[] Data)> Transfers(byte[] image)
    {
        if (image.Length != RomInspector.RomSize)
            throw new InvalidDataException("A full ROM is required.");
        for (int at = Start; at < image.Length; at += Chunk)
            yield return (at, image.AsSpan(at, Chunk).ToArray());
    }
    public byte[] Next()
    {
        if (inflight is not null || Stage is RecoveryStage.Verify or RecoveryStage.Interrupted or RecoveryStage.Failed)
            throw new InvalidDataException("No resend or cursor resume is allowed.");
        inflight = Stage switch
        {
            RecoveryStage.Session => [0x10, 0x85],
            RecoveryStage.Seed => [0x27, 0x01],
            RecoveryStage.Key => [0x27, 0x02, .. key!],
            RecoveryStage.Erase => [0xB1, 0, 0xB2, 0],
            RecoveryStage.Download => Download(),
            RecoveryStage.Transfer => [0x36, .. image.AsSpan(Start + Block * Chunk, Chunk).ToArray()],
            RecoveryStage.Exit => [0x37],
            RecoveryStage.Reset => [0x11, 0x01],
            _ => throw new InvalidDataException("Invalid stage.")
        };
        Events.Add(("issued", new()
        {
            ["stage"] = Name(Stage),
            ["block"] = Block,
            ["requestSHA256"] = RomInspector.Hash(inflight),
            ["requestBytes"] = inflight.Length,
            ["acknowledgedBytes"] = Acknowledged
        }));
        return inflight;
    }
    public void Accept(byte[] response)
    {
        if (inflight is null)
            throw new InvalidDataException("No request is outstanding.");
        if (response.Length == 3 && response[0] == 0x7F && response[1] == inflight[0])
        {
            if (response[2] == 0x78)
            {
                Events.Add(("pending", new()
                {
                    ["stage"] = Name(Stage)
                }));
                return;
            }
            Stage = RecoveryStage.Failed;
            inflight = null;
            Events.Add(("negative_response", new()
            {
                ["nrc"] = response[2]
            }));
            throw new InvalidDataException($"NRC 0x{response[2]:X2}.");
        }
        byte[] expected = Stage switch
        {
            RecoveryStage.Session => [0x50, 0x85],
            RecoveryStage.Key => [0x67, 0x02],
            RecoveryStage.Erase => [0xF1, 0, 0xB2],
            RecoveryStage.Download => [0x74, 0x0F, 0xFF],
            RecoveryStage.Transfer => [0x76],
            RecoveryStage.Exit => [0x77],
            RecoveryStage.Reset => [0x51],
            _ => []
        };
        bool valid = Stage == RecoveryStage.Seed ? response.Length == 5 && response[0] == 0x67 && response[1] == 0x01 : response.AsSpan().SequenceEqual(expected);
        if (!valid)
        {
            Stage = RecoveryStage.Failed;
            inflight = null;
            throw new InvalidDataException("Unexpected response; halted without retry.");
        }
        var old = Stage;
        if (Stage == RecoveryStage.Seed)
        {
            key = SecurityAccess.SeedKey(response.AsSpan(2));
            Stage = response.AsSpan(2).ToArray().All(x => x == 0) ? RecoveryStage.Erase : RecoveryStage.Key;
        }
        else if (Stage == RecoveryStage.Transfer)
        {
            Block++;
            Acknowledged += Chunk;
            if (Acknowledged == Size)
                Stage = RecoveryStage.Exit;
        }
        else
            Stage = Stage switch
            {
                RecoveryStage.Session => RecoveryStage.Seed,
                RecoveryStage.Key => RecoveryStage.Erase,
                RecoveryStage.Erase => RecoveryStage.Download,
                RecoveryStage.Download => RecoveryStage.Transfer,
                RecoveryStage.Exit => RecoveryStage.Reset,
                RecoveryStage.Reset => RecoveryStage.Verify,
                _ => throw new InvalidDataException("Invalid stage.")
            };
        Events.Add(("acknowledged", new()
        {
            ["stage"] = Name(old),
            ["nextStage"] = Name(Stage),
            ["block"] = Block,
            ["acknowledgedBytes"] = Acknowledged
        }));
        inflight = null;
    }
    public void Timeout()
    {
        if (inflight is null)
            throw new InvalidDataException("No outstanding request.");
        var previous = Stage;
        Stage = RecoveryStage.Interrupted;
        inflight = null;
        Events.Add(("ambiguous_timeout", new()
        {
            ["stage"] = Name(previous),
            ["acknowledgedBytes"] = Acknowledged,
            ["action"] = "reconnect_and_restart_from_erase"
        }));
    }
    public static string Run(object candidate, byte[] image, byte[] backup, string vault, string fault = "none", TestFileSystem? files = null)
    {
        if (candidate.GetType() != typeof(TestEcu))
            throw new TransportException("Programming runner accepts the in-memory simulator only.");
        var model = (TestEcu)candidate;
        Validate(image, backup);
        var store = Fixture.Store(files);
        var (folder, journal) = store.CreateOperation(vault, "simulator", "recovery_simulation");
        using (journal)
        {
            var session = new RecoveryModel(image, backup);
            int eventIndex = 0;
            void Persist()
            {
                while (eventIndex < session.Events.Count)
                {
                    var row = session.Events[eventIndex++];
                    journal.Append(row.Event, row.Fields);
                }
            }
            try
            {
                DurableStorage.NewJson(store.FileSystem, System.IO.Path.Combine(folder, "plan.json"), new
                {
                    flashStart = Start,
                    flashBytes = Size,
                    payloadBytes = Chunk,
                    transferCount = 256
                });
                journal.Append("image_association", new()
                {
                    ["imageSHA256"] = RomInspector.Hash(image),
                    ["backupSHA256"] = RomInspector.Hash(backup),
                    ["simulated"] = true,
                    ["fault"] = fault
                });
                while (session.Stage != RecoveryStage.Verify)
                {
                    var request = session.Next();
                    Persist();
                    var response = fault == "erase_rejected" && request[0] == 0xB1 ? new byte[] { 0x7F, 0xB1, 0x72 } : model.Program(request);
                    if (fault == "pending" && request[0] == 0xB1)
                    {
                        session.Accept([0x7F, 0xB1, 0x78]);
                        Persist();
                    }
                    if (fault == "lost_ack" && request[0] == 0x36)
                    {
                        session.Timeout();
                        Persist();
                        throw new TransportException("Simulated lost acknowledgement; no payload was resent.");
                    }
                    session.Accept(response);
                    Persist();
                }
                journal.Append("verification_started", new()
                {
                    ["simulated"] = true
                });
                if (!model.Memory.AsSpan().SequenceEqual(image) || !Fixture.Inspector().Inspect(model.Memory).ProfileValid)
                    throw new InvalidDataException("Readback mismatch.");
                DurableStorage.NewJson(store.FileSystem, System.IO.Path.Combine(folder, "verification.json"), new
                {
                    simulated = true,
                    fullImageMatches = true,
                    hardwareValidated = false
                });
                journal.Append("completed", new()
                {
                    ["simulated"] = true,
                    ["fullImageMatches"] = true,
                    ["sha256"] = RomInspector.Hash(image)
                });
                return folder;
            }
            catch (Exception error) { if (!journal.Failed) Persist(); journal.RecordStopped(new() { ["reason"] = error.Message, ["automaticResumeAllowed"] = false }); throw; }
        }
    }
}
