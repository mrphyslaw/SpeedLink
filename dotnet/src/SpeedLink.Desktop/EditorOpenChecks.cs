using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    internal async Task<string[]> ExerciseRejectedRomFiles(string path, string folder)
    {
        var cases = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            cases.Add(name);
            File.WriteAllText(Path.Combine(folder, "RomFileRecovery.json"), System.Text.Json.JsonSerializer.Serialize(new { cases }));
        }
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout(); }
        var source = File.ReadAllBytes(path);
        var profile = inspector.ProfileFor(source)!;
        var definition = TuningDefinition.Known.Single(d => d.Calibration == profile.Calibration);
        var protectedRanges = definition.Tables.Select(t => (Start: t.Address, End: t.End)).ToList();
        protectedRanges.Add((0, profile.BootLength));
        protectedRanges.Add((profile.IdentityOffset, profile.IdentityOffset + profile.Identity.Length));
        protectedRanges.Add((0x8000, 0x8004)); protectedRanges.Add((source.Length - 4, source.Length));
        if (profile.CorrectionAddress >= 0) protectedRanges.Add((profile.CorrectionAddress, profile.CorrectionAddress + 4));
        protectedRanges.AddRange(profile.LayoutWords.Select(w => (w.Address, w.Address + 4)));
        protectedRanges.AddRange((profile.Markers ?? []).Select(w => (w.Address, w.Address + 2)));
        protectedRanges.AddRange((profile.RoutineRanges ?? []).Select(r => (r.Address, r.Address + r.Length)));
        if (profile.LoopAddress is int loop) protectedRanges.Add((loop, loop + profile.LoopSize!.Value));
        var addresses = new List<int>();
        foreach (var range in (profile.SumRanges ?? [new(profile.ChecksumStart, source.Length - profile.ChecksumStart)]).Reverse())
            for (int at = range.Address + range.Length - 4; at >= range.Address && addresses.Count < 2; at -= 4)
                if (!protectedRanges.Any(r => at < r.End && at + 4 > r.Start)) addresses.Add(at);
        Check(addresses.Count == 2, "Unsupported-ROM fixture has two unprotected program words");
        var programChanged = (byte[])source.Clone();
        uint first = BinaryPrimitives.ReadUInt32BigEndian(programChanged.AsSpan(addresses[0], 4));
        uint second = BinaryPrimitives.ReadUInt32BigEndian(programChanged.AsSpan(addresses[1], 4));
        BinaryPrimitives.WriteUInt32BigEndian(programChanged.AsSpan(addresses[0], 4), unchecked(first + 1));
        BinaryPrimitives.WriteUInt32BigEndian(programChanged.AsSpan(addresses[1], 4), unchecked(second - 1));
        var changedInspection = inspector.Inspect(programChanged);
        Check(changedInspection.ProfileValid && !codeGuard.Matches(programChanged, definition, profile),
            "Unsupported program passes native integrity but fails stock program identification");
        if (profile.Family == "Gen2")
            Check(!changedInspection.CanExport, "Gen 2 program guard blocks checksum correction and export");
        var invalidLayout = (byte[])source.Clone(); invalidLayout[0] ^= 1;
        var inputs = new[]
        {
            (Name: "unsupported-program", Bytes: programChanged, Message: "program area differs"),
            (Name: "unknown-calibration", Bytes: new byte[RomInspector.RomSize], Message: "No matching tuning definition"),
            (Name: "truncated-file", Bytes: source[..1024], Message: "Expected"),
            (Name: "invalid-boot", Bytes: invalidLayout, Message: "boot, integrity or layout")
        };
        foreach (var input in inputs) File.WriteAllBytes(Path.Combine(folder, input.Name + ".bin"), input.Bytes);
        void Reject((string Name, byte[] Bytes, string Message) input, string state)
        {
            var messages = new List<string>();
            ExecuteEditorAction(() => OpenRom(Path.Combine(folder, input.Name + ".bin")), messages.Add);
            Check(messages.Count == 1 && messages[0].Contains(input.Message, StringComparison.Ordinal), state + ": " + input.Name + " reports one recoverable error");
        }
        foreach (var input in inputs)
        {
            Reject(input, "Empty editor"); await Idle();
            Check(document is null && table is null && NoMapPanel.IsVisible, "Empty editor remains usable after " + input.Name);
        }
        OpenRom(path); await Idle();
        var doc = document!;
        var maps = doc.Definition.Tables.Where(t => t.Rows > 1 && t.Columns > 1 && !t.IsChoice).Take(2).ToArray();
        var firstMap = OpenMap(maps[0]); var floating = OpenMap(maps[1]);
        floating.UndockMap_Click(floating, new()); await Idle();
        var native = mapSlots.Single(s => ReferenceEquals(s.Editor, floating)).Window!;
        firstMap.SelectCell(1); floating.SelectCell(2);
        firstMap.SetCellZoom(firstMap.cellZoom + .05);
        const string comparisonName = "comparison_stock.bin";
        doc.LoadComparison(source, Path.Combine(folder, comparisonName));
        floating.BaselineBox.SelectedIndex = 2;
        firstMap.BaselineBox.SelectedIndex = 1;
        floating.ActivateMap();
        Check(firstMap.CellText.Text.Contains("Compared with Last save", StringComparison.Ordinal) &&
            floating.CellText.Text.Contains("Compared with " + comparisonName, StringComparison.Ordinal) &&
            ChangedOnlyLabel.Text == "Changed vs. Last save" &&
            Equals(ResetCellsMenu.Header, "Reset selection to comparison__stock.bin"),
            "Docked and detached maps identify their own comparison, filter and reset targets");
        doc.Apply([new(maps[0], 0, doc.Read(maps[0], 0) + 1)], "Existing unsaved edit"); await Idle();
        var snapshot = doc.Snapshot(); var slots = mapSlots.ToArray();
        var selection = firstMap.SelectedIndices(); var floatingSelection = floating.SelectedIndices();
        double zoom = firstMap.cellZoom; string fileName = FileText.Text;
        foreach (var input in inputs)
        {
            Reject(input, "Edited multi-map workspace"); await Idle();
            Check(ReferenceEquals(document, doc) && doc.IsDirty && doc.CanUndo && snapshot.AsSpan().SequenceEqual(doc.Snapshot()), "Rejected " + input.Name + " preserves unsaved bytes and undo");
            Check(mapSlots.SequenceEqual(slots) && native.IsVisible && firstMap.SelectedIndices().SequenceEqual(selection) && floating.SelectedIndices().SequenceEqual(floatingSelection) && firstMap.cellZoom == zoom && FileText.Text == fileName,
                "Rejected " + input.Name + " preserves docked and detached maps, selections and zoom");
        }
        bool prompted = false;
        var errors = new List<string>();
        ExecuteEditorAction(() => ReplaceRom(Path.Combine(folder, "truncated-file.bin"),
            choose: () => { prompted = true; return UnsavedRomChoice.Discard; }), errors.Add);
        Check(!prompted && errors.Count == 1, "A replacement ROM is validated before asking to save or discard");
        errors.Clear();
        Check(!ReplaceRom(path, choose: () => UnsavedRomChoice.Cancel) &&
            !ReplaceRom(path, choose: () => UnsavedRomChoice.SaveCopy, saveCopy: () => SaveCopy(_ => null, errors.Add)) &&
            !ReplaceRom(path, choose: () => UnsavedRomChoice.SaveCopy, saveCopy: () => SaveCopy(_ => path, errors.Add)) &&
            errors.Count == 1 && ReferenceEquals(document, doc) && doc.IsDirty && doc.CanUndo &&
            snapshot.AsSpan().SequenceEqual(doc.Snapshot()) && mapSlots.SequenceEqual(slots),
            "Cancel, canceled save and failed save each leave the edited document and maps open");
        doc.Undo(); Check(source.AsSpan().SequenceEqual(doc.Snapshot()), "Undo still works after unsupported file rejection");
        doc.Redo(); Check(snapshot.AsSpan().SequenceEqual(doc.Snapshot()), "Redo still works after unsupported file rejection");
        string saved = Path.Combine(folder, "supported-calibration-edit.bin");
        Check(ReplaceRom(path, choose: () => UnsavedRomChoice.SaveCopy, saveCopy: () => SaveCopy(_ => saved, errors.Add)) &&
            inspector.Inspect(File.ReadAllBytes(saved)).ChecksumValid == true && !doc.IsDirty &&
            document != doc && source.AsSpan().SequenceEqual(document!.Snapshot()),
            "Save copy verifies the edited ROM before opening the replacement");
        OpenRom(saved); await Idle();
        var savedDoc = document!;
        var savedMap = savedDoc.Definition.Tables.Single(t => t.Name == maps[0].Name);
        Check(savedDoc.SourcePath == saved && savedDoc.Read(savedMap, 0) == doc.Read(maps[0], 0),
            "Supported calibration-only modified ROM opens after unsupported rejection");
        savedDoc.Apply([new(savedMap, 0, savedDoc.Read(savedMap, 0) + 1)], "Edit to discard");
        Check(ReplaceRom(path, choose: () => UnsavedRomChoice.Discard) &&
            source.AsSpan().SequenceEqual(document!.Snapshot()) && File.ReadAllBytes(path).AsSpan().SequenceEqual(source),
            "Discard opens the replacement and preserves the original file");
        bool unexpectedEscaped = false;
        try { ExecuteEditorAction(() => throw new NullReferenceException("Controlled unexpected failure"), _ => { }); }
        catch (NullReferenceException) { unexpectedEscaped = true; }
        Check(unexpectedEscaped, "Unexpected programming errors still reach fatal crash reporting");
        ClearMapWindows(); MapTabs.Items.Clear(); SaveMapWorkspace(); await Idle();
        return cases.ToArray();
    }
}
