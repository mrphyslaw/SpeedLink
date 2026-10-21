using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Xml.Linq;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class MainWindow
{
    internal async Task<object> ExerciseDefinitionsOffline(string romPath, string folder, string calibration)
    {
        Show(); Navigate(1); EditorPane.OpenRom(romPath); UpdateLayout();
        var original = EditorPane.Document!; var table = original.Definition.Tables.First(t => t.Kind == "1D" && t.Storage == "float");
        original.Apply([new(table, 0, 1234)], "Retain edits during definition import"); byte[] edited = original.Snapshot();
        var xml = XDocument.Parse(Encoding.UTF8.GetString(DefinitionLibrary.BundledBytes(DefinitionKind.Editor, calibration)));
        xml.Descendants("version").Single().Value = "import-workflow";
        string xmlPath = Path.Combine(folder, calibration + ".xml"); xml.Save(xmlPath);
        var json = JsonNode.Parse(DefinitionLibrary.BundledBytes(DefinitionKind.Logger, calibration))!;
        json["definitionVersion"] = "import-workflow";
        string jsonPath = Path.Combine(folder, calibration + ".logger.json"); File.WriteAllText(jsonPath, json.ToJsonString());
        var manager = new DefinitionManagerWindow(definitionLibrary) { Owner = this }; int confirmations = 0;
        try
        {
            manager.Show();
            bool Accept(DefinitionCandidate candidate, DefinitionEntry current)
            {
                if (candidate.Entry.Calibration != calibration || candidate.Entry.Controls == 0 || current.Imported)
                    throw new InvalidOperationException("Import preview lost the identity or bundled source.");
                confirmations++; return true;
            }
            if (!manager.ImportPath(xmlPath, Accept) || !manager.ImportPath(jsonPath, Accept)) throw new InvalidOperationException("Definition import was cancelled.");
            manager.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)manager.ActualWidth, (int)manager.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(manager); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(folder, "Definitions.png"))) encoder.Save(output);
            RefreshDefinitions();
            if (!ReferenceEquals(original, EditorPane.Document) || !edited.AsSpan().SequenceEqual(original.Snapshot()) || !original.IsDirty)
                throw new InvalidOperationException("Definition import replaced or changed the open ROM.");
            if (definitionSnapshot.LoggerFor(calibration)!.DefinitionVersion != "import-workflow") throw new InvalidOperationException("Imported logger was not selected.");
            string saved = Path.Combine(folder, "edited-copy.bin"); original.SaveNew(saved); EditorPane.OpenRom(saved);
            if (EditorPane.Document!.Definition.Version != "import-workflow" || inspectorForEditor().Inspect(File.ReadAllBytes(saved)).ChecksumValid != true)
                throw new InvalidOperationException("Imported editor or checksum export failed.");
            var restarted = new MainWindow(vault, inspectorForEditor(), guardForEditor());
            try
            {
                restarted.OpenEditorFile(saved);
                if (restarted.EditorPane.Document!.Definition.Version != "import-workflow" || restarted.definitionSnapshot.LoggerFor(calibration)!.DefinitionVersion != "import-workflow")
                    throw new InvalidOperationException("Definition updates did not survive a fresh window.");
            }
            finally { restarted.CloseWorkbenchTest(); }
            manager.SelectAndUseBundled(DefinitionKind.Editor, calibration); manager.SelectAndUseBundled(DefinitionKind.Logger, calibration); RefreshDefinitions();
            if (definitionSnapshot.Entries.Any(e => e.Imported || e.Problem is not null)) throw new InvalidOperationException("Bundled fallback did not restore cleanly.");
            if (!DefinitionsButton.IsEnabled) throw new InvalidOperationException("Definitions should be available offline.");
            busy = true; Controls(); if (DefinitionsButton.IsEnabled) throw new InvalidOperationException("Definitions can change during an active operation.");
            busy = false; Controls();
            return new { passed = true, hardwareAccessed = false, actualManagerActions = true, confirmations, editorAndLoggerImported = true, openEditsPreserved = true,
                persistedInFreshWindow = true, checksumVerified = true, bundledRestored = true, busyControlLocked = true };
        }
        finally { busy = false; manager.Close(); }
        RomInspector inspectorForEditor() => EditorPane.TestInspector;
        TuningCodeGuard guardForEditor() => EditorPane.TestGuard;
    }
}
