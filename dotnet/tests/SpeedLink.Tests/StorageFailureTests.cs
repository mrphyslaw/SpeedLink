using SpeedLink.Core;
namespace SpeedLink.Tests;

public class StorageFailureTests
{
    [Fact]
    public void test_programming_fault_matrix()
    {
        var cases = new (string Name, Func<string, byte[], bool> Predicate, string Fault, int Calls, int Transfers)[] {
            ("pending_marker_fsync",TestFileSystem.FileIs("journal.jsonl.incomplete"),"fsync",0,0),
            ("started_record_disk_full",TestFileSystem.EventIs("started"),"disk_full",0,0),
            ("plan_file_fsync",TestFileSystem.FileIs("plan.json"),"fsync",0,0),
            ("image_association_flush",TestFileSystem.EventIs("image_association"),"flush",0,0),
            ("session_issued_fsync",TestFileSystem.EventIs("issued","session"),"fsync",0,0),
            ("erase_issued_fsync",TestFileSystem.EventIs("issued","erase"),"fsync",3,0),
            ("erase_ack_fsync",TestFileSystem.EventIs("acknowledged","erase"),"fsync",4,0),
            ("download_issued_fsync",TestFileSystem.EventIs("issued","download"),"fsync",4,0),
            ("first_transfer_issued_fsync",TestFileSystem.EventIs("issued","transfer",0),"fsync",5,0),
            ("third_transfer_disk_full",TestFileSystem.EventIs("issued","transfer",2),"disk_full",7,2),
            ("first_transfer_ack_fsync",TestFileSystem.EventIs("acknowledged","transfer",1),"fsync",6,1),
            ("transfer_journal_short_write",TestFileSystem.EventIs("issued","transfer",1),"short",6,1),
            ("verification_file_fsync",TestFileSystem.FileIs("verification.json"),"fsync",263,256),
            ("completed_record_fsync",TestFileSystem.EventIs("completed"),"fsync",263,256),
            ("completed_record_short_write",TestFileSystem.EventIs("completed"),"short",263,256) };
        foreach (var row in cases)
        {
            using var temp = new TempFolder();
            using var ecu = new TestEcu(Fixture.Rom);
            var files = new TestFileSystem { Predicate = row.Predicate, Fault = row.Fault };
            Assert.Throws<IOException>(() => RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path, files: files));
            Assert.True(files.Fired, row.Name);
            Assert.Equal(row.Calls, ecu.Programs.Count);
            Assert.Equal(row.Transfers, ecu.Programs.Count(x => x[0] == 0x36));
            Assert.Equal(Fixture.Rom[..0x8000], ecu.Memory[..0x8000]);
            Assert.Equal(0, files.JournalWritesAfterFailure);
            Assert.All(Fixture.Store(files).Scan(temp.Path), entry => Assert.False(entry.Complete));
        }
    }
    [Fact]
    public void test_read_and_backup_fault_matrix()
    {
        var cases = new (string Name, Func<string, byte[], bool> Predicate, string Fault, int Reads)[] {
            ("read_issued_fsync",TestFileSystem.EventIs("read_issued"),"fsync",0),
            ("capture_disk_full",TestFileSystem.FileIs("pass1.read"),"disk_full",1),
            ("capture_short_write",TestFileSystem.FileIs("pass1.read"),"short",1),
            ("capture_fsync",TestFileSystem.FileIs("pass1.read"),"fsync",1),
            ("read_saved_fsync",TestFileSystem.EventIs("read_saved"),"fsync",1),
            ("backup_rom_disk_full",TestFileSystem.FileIs("rom.bin"),"disk_full",530),
            ("backup_manifest_fsync",TestFileSystem.FileIs("backup.json"),"fsync",530),
            ("backup_completed_fsync",TestFileSystem.EventIs("completed"),"fsync",530) };
        foreach (var row in cases)
        {
            using var temp = new TempFolder();
            using var ecu = new TestEcu(Fixture.Rom);
            var files = new TestFileSystem { Predicate = row.Predicate, Fault = row.Fault };
            Assert.Throws<IOException>(() => Fixture.Reader(files).ReadBackup(ecu, ReadWorkflow.Identify(ecu), temp.Path));
            Assert.True(files.Fired, row.Name);
            Assert.Equal(row.Reads, ecu.Requests.Count(x => x[0] == 0x23));
            Assert.All(Fixture.Store(files).Scan(temp.Path), entry => Assert.False(entry.Complete));
            foreach (var file in Directory.GetFiles(temp.Path, "rom.bin", SearchOption.AllDirectories))
                Assert.True(Record.Exception(() => Fixture.Store(files).Load(file)) is IOException or InvalidDataException);
            Assert.Equal(0, files.JournalWritesAfterFailure); // Healthy journals can still record a capture-file failure.
        }
    }
    [Fact]
    public void test_journal_open_denied_sends_nothing()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var files = new TestFileSystem { Predicate = TestFileSystem.FileIs("journal.jsonl"), Fault = "open" };
        Assert.Throws<UnauthorizedAccessException>(() => RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path, files: files));
        Assert.Empty(ecu.Programs);
    }
    [Fact]
    public void test_completion_marker_removal_denied_remains_incomplete()
    {
        using var temp = new TempFolder();
        using var ecu = new TestEcu(Fixture.Rom);
        var files = new TestFileSystem { Predicate = TestFileSystem.FileIs("journal.jsonl.incomplete"), Fault = "delete" };
        Assert.Throws<UnauthorizedAccessException>(() => RecoveryModel.Run(ecu, Fixture.Rom, Fixture.Rom, temp.Path, files: files));
        var row = Fixture.Store(files).Scan(temp.Path).Single();
        Assert.True(row.Valid);
        Assert.False(row.Complete);
        Assert.Equal("completed", row.LastEvent);
    }
}
