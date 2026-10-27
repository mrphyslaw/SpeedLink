using System.IO.Compression;
using System.Text.Json;
using SpeedLink.Core;

namespace SpeedLink.Tests;

public sealed class CrashReportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SpeedLink-crash-" + Guid.NewGuid().ToString("N"));
    private CrashReports Store => new(Path.Combine(root, "crashes"));
    private string Capture(CrashReports? store = null, string message = "Unexpected failure") => (store ?? Store).Capture(new InvalidOperationException(message), "test", "User interface", true, "Editing")!;
    [Fact] public void report_is_durable_and_available_after_restart()
    {
        string path = Capture(); var fresh = Store;
        Assert.Equal(path, Assert.Single(fresh.List(true))); Assert.Equal("Editing", fresh.Read(path).Activity);
        Assert.True(fresh.Read(path).Fatal); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.pending"));
    }
    [Fact] public void personal_data_and_exception_object_graphs_are_excluded()
    {
        var error = new InvalidOperationException("Failure C:\\Users\\PrivateOwner\\rom.bin\nSN:SECRET123 AAAAAAAAAAAAAAAAA");
        error.Data["ROM"] = "PRIVATE_IMAGE_PAYLOAD";
        string path = Store.Capture(error, "test", "Runtime", true, "Idle")!; string text = File.ReadAllText(path);
        Assert.DoesNotContain("PrivateOwner", text); Assert.DoesNotContain("SECRET123", text); Assert.DoesNotContain("AAAAAAAAAAAAAAAAA", text); Assert.DoesNotContain("PRIVATE_IMAGE_PAYLOAD", text);
    }
    [Fact] public void registered_identity_secrets_are_redacted()
    {
        var redactor = new DiagnosticRedactor(); redactor.Register("OwnerSecret");
        string path = Store.Capture(new Exception("OwnerSecret"), "test", "Runtime", true, "Idle", redactor)!;
        Assert.DoesNotContain("OwnerSecret", File.ReadAllText(path));
    }
    [Fact] public void messages_and_files_are_bounded()
    {
        string path = Capture(message: new string('x', 100000)); Assert.True(new FileInfo(path).Length <= CrashReports.MaximumBytes); Assert.Equal(4096, Store.Read(path).Message.Length);
    }
    [Fact] public void unwritable_primary_uses_fallback()
    {
        Directory.CreateDirectory(root); string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "keep");
        var store = new CrashReports(blocked, Path.Combine(root, "fallback")); string path = Capture(store);
        Assert.Equal(Path.Combine(root, "fallback"), Path.GetDirectoryName(path)); Assert.Equal("keep", File.ReadAllText(blocked));
    }
    [Fact] public void no_writable_location_returns_failure_without_overwriting()
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "blocked"), "keep");
        Assert.Null(new CrashReports(Path.Combine(root, "blocked")).Capture(new Exception(), "test", "Runtime", true, "Idle"));
    }
    [Fact] public void acknowledgement_preserves_report_and_does_not_repeat_prompt()
    {
        string path = Capture(); byte[] before = File.ReadAllBytes(path); Store.Acknowledge(path);
        Assert.Empty(Store.List(true)); Assert.Single(Store.List()); Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Throws<IOException>(() => Store.Acknowledge(path));
    }
    [Fact] public void retention_is_limited_and_preserves_other_files()
    {
        string first = Capture(); string unrelated = Path.Combine(Path.GetDirectoryName(first)!, "original.bin"); File.WriteAllText(unrelated, "keep");
        for (int i = 0; i < 14; i++) Capture();
        Assert.Equal(CrashReports.RetainedReports, Store.List().Count); Assert.Equal("keep", File.ReadAllText(unrelated));
    }
    [Fact] public void exporter_allowlists_files_and_never_overwrites()
    {
        string path = Capture(); File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "image.bin"), "PRIVATE_IMAGE");
        string zipPath = Path.Combine(root, "support.zip"); Store.Export(path, zipPath); byte[] before = File.ReadAllBytes(zipPath);
        using var zip = ZipFile.OpenRead(zipPath); Assert.Equal(2, zip.Entries.Count); Assert.NotNull(zip.GetEntry("crash.json")); Assert.NotNull(zip.GetEntry("summary.txt"));
        Assert.Throws<IOException>(() => Store.Export(path, zipPath)); Assert.Equal(before, File.ReadAllBytes(zipPath));
    }
    [Fact] public void malformed_oversized_and_outside_reports_are_rejected()
    {
        string path = Capture(); File.WriteAllText(path, "{"); Assert.Empty(Store.List()); Assert.Throws<JsonException>(() => Store.Read(path));
        File.WriteAllText(path, new string('x', CrashReports.MaximumBytes + 1)); Assert.Empty(Store.List());
        Assert.Throws<InvalidDataException>(() => Store.Read(Path.Combine(root, "outside.json")));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
