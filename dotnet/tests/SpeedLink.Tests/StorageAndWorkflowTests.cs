using System.Text;
using System.Text.Json.Nodes;
using SpeedLink.Core;
namespace SpeedLink.Tests;

public class StorageTests
{
    [Fact]
    public void test_durable_complete_chain()
    {
        using var temp = new TempFolder();
        var store = new BackupStore();
        var path = Path.Combine(temp.Path, "journal.jsonl");
        using (var journal = new OperationJournal(path))
        {
            journal.Append("started", new()
            {
                ["mode"] = "simulator",
                ["operation"] = "test"
            });
            journal.Append("issued", new()
            {
                ["request"] = "test"
            });
            journal.Append("completed");
        }
        var result = store.InspectJournal(path);
        Assert.True(result.Valid);
        Assert.True(result.Complete);
        Assert.Equal(3, result.Records);
    }
    [Fact]
    public void test_interrupted_truncated_modified_reordered()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "journal.jsonl");
        using (var journal = new OperationJournal(path))
        {
            journal.Append("started", new()
            {
                ["mode"] = "simulator"
            });
            journal.Append("issued", new()
            {
                ["block"] = 1
            });
        }
        var store = new BackupStore();
        Assert.False(store.InspectJournal(path).Complete);
        var data = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(data);
        foreach (var changed in new[] { data[..^1], Encoding.UTF8.GetBytes(text.Replace("\"block\":1", "\"block\":2", StringComparison.Ordinal)), Encoding.UTF8.GetBytes(string.Join('\n', text.TrimEnd('\n').Split('\n').Reverse()) + "\n") })
        {
            File.WriteAllBytes(path, changed);
            Assert.False(store.InspectJournal(path).Valid);
        }
    }
    [Fact]
    public void test_exclusive_output()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "rom.bin");
        var fs = new BackupFileSystem();
        DurableStorage.NewFile(fs, path, "original"u8.ToArray());
        Assert.Throws<IOException>(() => DurableStorage.NewFile(fs, path, "changed"u8.ToArray()));
        Assert.Equal("original", File.ReadAllText(path));
    }
}
public class RomAndWorkflowTests
{
    [Fact]
    public void test_image_and_corrected_copy()
    {
        var rom = Fixture.Rom;
        var inspector = Fixture.Inspector();
        Assert.True(inspector.Inspect(rom).ProfileValid);
        rom[0x50000] ^= 1;
        Assert.False(inspector.Inspect(rom).ChecksumValid);
        var corrected = inspector.CorrectedCopy(rom);
        Assert.True(inspector.Inspect(corrected).ProfileValid);
        Assert.Equal(rom[..0x48000], corrected[..0x48000]);
        Assert.Equal(rom[0x48004..], corrected[0x48004..]);
        Assert.Throws<InvalidDataException>(() => inspector.Inspect(rom[..^1]));
    }
    [Fact]
    public void test_wrong_profile_cannot_correct()
    {
        var rom = Fixture.Rom;
        rom[0x100] ^= 1;
        Assert.Throws<InvalidDataException>(() => Fixture.Inspector().CorrectedCopy(rom));
    }
    [Fact]
    public void test_two_pass_backup_manifest_and_mode_separation()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var files = new TestFileSystem { Durable = true };
        var store = Fixture.Store(files);
        var result = Fixture.Reader(files).ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path);
        var loaded = store.Load(result.Path, "simulator");
        Assert.Equal(Fixture.Rom, loaded.Data);
        Assert.True(loaded.Manifest["simulated"]!.GetValue<bool>());
        Assert.False(loaded.Manifest["factoryProvenanceVerified"]!.GetValue<bool>());
        Assert.True(store.InspectJournal(Path.Combine(Path.GetDirectoryName(result.Path)!, "journal.jsonl")).Complete);
        foreach (var name in new[] { "pass1.read", "pass2.read" })
            Assert.Equal(loaded.Data, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(result.Path)!, name)));
        Assert.Throws<InvalidDataException>(() => store.Load(result.Path, "hardware"));
        File.SetAttributes(result.Path, FileAttributes.Normal);
        File.WriteAllBytes(result.Path, loaded.Data[..^1]);
        Assert.Throws<InvalidDataException>(() => store.Load(result.Path));
    }
    [Fact]
    public void test_short_read_stops_and_keeps_journal()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom) { ModifyResponse = (request, response) => request[0] == 0x23 ? response[..^1] : response };
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path));
        Assert.Empty(Directory.GetFiles(temp.Path, "rom.bin", SearchOption.AllDirectories));
        var entry = Fixture.Store().Scan(temp.Path).Single();
        Assert.True(entry.Valid);
        Assert.False(entry.Complete);
    }
    [Fact]
    public void test_second_read_mismatch_never_seals_backup()
    {
        using var temp = new TempFolder();
        int reads = 0;
        using var ecu = new TestEcu(Fixture.Rom)
        {
            ModifyResponse = (request, response) =>
            {
                if (request[0] == 0x23 && ++reads == 266)
                    response[^1] ^= 1;
                return response;
            }
        };
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path));
        Assert.Empty(Directory.GetFiles(temp.Path, "rom.bin", SearchOption.AllDirectories));
        Assert.Equal(2, Directory.GetFiles(temp.Path, "*.read", SearchOption.AllDirectories).Length);
    }
    [Fact]
    public void test_incomplete_journal_cannot_be_loaded_as_backup()
    {
        using var temp = new TempFolder();
        var files = new TestFileSystem();
        var path = Path.Combine(temp.Path, "rom.bin");
        DurableStorage.NewFile(files, path, Fixture.Rom);
        DurableStorage.NewJson(files, Path.Combine(temp.Path, "backup.json"), new
        {
            formatVersion = 1,
            mode = "simulator",
            simulated = true
        });
        using (var journal = new OperationJournal(Path.Combine(temp.Path, "journal.jsonl"), files))
            journal.Append("started", new()
            {
                ["mode"] = "simulator",
                ["operation"] = "read_backup"
            });
        Assert.Throws<InvalidDataException>(() => Fixture.Store(files).Load(path));
    }
    [Fact]
    public void test_identity_changed_blocks_read_before_unlock()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var identity = ReadWorkflow.Identify(ecu) with
        {
            Vin = "different"
        };
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(ecu, identity, temp.Path));
        Assert.False(ecu.ReadSession);
    }
    [Fact]
    public void test_engine_running_blocks_read()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom) { ModifyResponse = (request, response) => Convert.ToHexString(request) == "010C" ? Fixture.Hex("410C0FA0") : response };
        Assert.Throws<TransportException>(() => Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path));
        Assert.False(ecu.ReadSession);
    }
    [Fact]
    public void test_unknown_calibration_can_be_dumped()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom) { ModifyResponse = (request, response) => Convert.ToHexString(request) == "0904" ? new byte[] { 0x49, 0x04, 1 }.Concat("UNKNOWN01"u8.ToArray()).ToArray() : response };
        var result = Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path);
        var loaded = Fixture.Store().Load(result.Path);
        Assert.Equal(Fixture.Rom, loaded.Data);
        Assert.Equal("UNKNOWN01", loaded.Manifest["identity"]!["calibration"]!.GetValue<string>());
    }
    [Fact]
    public void test_cancel_before_read()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Fixture.Reader().ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path, cancellation: cancellation.Token));
        Assert.False(ecu.ReadSession);
    }
    [Fact]
    public void test_recovery_and_pending_complete_with_full_readback()
    {
        using var temp = new TempFolder();
        foreach (var fault in new[] { "none", "pending" })
        {
            using var ecu = new TestEcu(Fixture.Rom);
            var folder = RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path, fault);
            Assert.Equal(Fixture.Rom, ecu.Memory);
            Assert.True(Fixture.Store().InspectJournal(Path.Combine(folder, "journal.jsonl")).Complete);
            var transfers = File.ReadAllLines(Path.Combine(folder, "journal.jsonl")).Select(x => JsonNode.Parse(x)!).Where(x => x["event"]!.GetValue<string>() == "issued" && x["details"]?["stage"]?.GetValue<string>() == "transfer").ToArray();
            Assert.Equal(256, transfers.Length);
            Assert.Equal(3969, transfers[^1]["details"]!["requestBytes"]!.GetValue<int>());
            Assert.True(JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "verification.json")))!["fullImageMatches"]!.GetValue<bool>());
        }
    }
    [Fact]
    public void test_lost_ack_stops_then_new_full_recovery_repairs()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var boot = ecu.Memory[..0x8000];
        Assert.Throws<TransportException>(() => RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path, "lost_ack"));
        Assert.Equal(0x8000 + 3968, ecu.Cursor);
        Assert.Equal(boot, ecu.Memory[..0x8000]);
        Assert.False(Fixture.Store().Scan(temp.Path).Single().Complete);
        Assert.Single(ecu.Programs, x => x[0] == 0x36);
        RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path);
        Assert.Equal(Fixture.Rom, ecu.Memory);
    }
    [Fact]
    public void test_negative_erase_does_not_erase_or_continue()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        Assert.Throws<InvalidDataException>(() => RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path, "erase_rejected"));
        Assert.Equal(Fixture.Rom, ecu.Memory);
        Assert.False(Fixture.Store().Scan(temp.Path).Single().Complete);
    }
    [Fact]
    public void test_real_transport_cannot_enter_simulated_writer()
    {
        using var temp = new TempFolder();
        var serial = new FakeSerial();
        using var link = new ElmTransport(serial);
        Assert.Throws<TransportException>(() => RecoveryModel.Run(link, Fixture.Rom, Fixture.Rom, temp.Path));
        Assert.Empty(serial.Writes);
    }
}
