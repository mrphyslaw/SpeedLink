using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SpeedLink.Core;

namespace SpeedLink.Desktop;

public partial class EditorWorkspace
{
    private enum UnsavedRomChoice { Cancel, SaveCopy, Discard }

    public void OpenRom(string path) => ReplaceRom(path);

    internal bool TryOpenRom(string path, byte[]? verifiedBytes = null)
    {
        bool opened = false;
        Attempt(() => { opened = ReplaceRom(path, verifiedBytes); });
        return opened;
    }

    private bool ReplaceRom(string path, byte[]? verifiedBytes = null,
        Func<UnsavedRomChoice>? choose = null, Func<bool>? saveCopy = null)
    {
        // Validate the proposed replacement before asking about the current ROM.
        var opened = new RomEditor(verifiedBytes ?? File.ReadAllBytes(path), path, inspector, codeGuard, Definitions);
        if (!MayLeaveDocument(choose ?? PromptUnsavedRom, saveCopy ?? SaveCopy)) return false;
        SaveMapWorkspace();
        restoringMaps = true;
        try
        {
            ClearMapWindows();
            if (document is not null) document.Changed -= Document_Changed;
            document = opened;
            document.Changed += Document_Changed;
            table = null;
            MapTabs.Items.Clear();
            BaselineBox.SelectedIndex = 0;
            SearchBox.Text = "";
            ChangedOnly.IsChecked = false;
            FileText.Text = Path.GetFileName(path);
            FileText.ToolTip = path;
            RefreshBrowser();
            RefreshTable();
            RefreshControls();
            ShowMapBrowser();
        }
        finally { restoringMaps = false; }
        RestoreMapWorkspace();
        EditorStatus.Text = "ROM loaded. Choose a map to edit. Static axes are read-only.";
        return true;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (mapOwner is not null) { mapOwner.Open_Click(sender, e); return; }
        var picker = new OpenFileDialog { Title = "Open ROM for editing", Filter = "ROM files|*.bin;*.rom|All files|*.*" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) TryOpenRom(picker.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => MapRoot.SaveCopy();

    private bool SaveCopy() => SaveCopy(ChooseCopyPath, ReportEditorError);

    private bool SaveCopy(Func<RomEditor, string?> choosePath, Action<string> report)
    {
        if (document is not { } current) return false;
        bool saved = false;
        ExecuteEditorAction(() =>
        {
            string? path = choosePath(current);
            if (path is null || !FinishAllMapEdits()) return;
            current.SaveNew(path);
            EditorStatus.Text = "Saved · bytes and checksum verified: " + path;
            saved = true;
        }, report);
        return saved;
    }

    private string? ChooseCopyPath(RomEditor current)
    {
        string stem = Path.GetFileNameWithoutExtension(current.SourcePath) + "_edited";
        string folder = Path.GetDirectoryName(current.LastSavePath ?? current.SourcePath)!;
        string suggestion = stem + ".bin";
        int revision = 2;
        while (File.Exists(Path.Combine(folder, suggestion))) suggestion = stem + "_" + revision++ + ".bin";
        var picker = new SaveFileDialog
        {
            Title = "Save a new edited ROM copy", Filter = "Binary ROM|*.bin",
            FileName = suggestion, InitialDirectory = folder, OverwritePrompt = false
        };
        return picker.ShowDialog(Window.GetWindow(this)) == true ? picker.FileName : null;
    }

    internal bool MayClose() => closingForTest || MayLeaveDocument(PromptUnsavedRom, SaveCopy);

    private bool MayLeaveDocument(Func<UnsavedRomChoice> choose, Func<bool> saveCopy)
    {
        if (document is null || !document.IsDirty && !mapSlots.Any(s => s.Editor.scalarDraft || s.Editor.gridDraft))
            return true;
        switch (choose())
        {
            case UnsavedRomChoice.SaveCopy:
                return saveCopy();
            case UnsavedRomChoice.Discard:
                foreach (var slot in mapSlots)
                {
                    slot.Editor.MapGrid.CancelEdit(DataGridEditingUnit.Cell);
                    slot.Editor.MapGrid.CancelEdit(DataGridEditingUnit.Row);
                    slot.Editor.RefreshScalar();
                }
                return true;
            default:
                return false;
        }
    }

    private UnsavedRomChoice PromptUnsavedRom()
    {
        var dialog = Floating("Unsaved ROM edits", 475, 180);
        dialog.MinHeight = 0;
        dialog.ResizeMode = ResizeMode.NoResize;
        dialog.SizeToContent = SizeToContent.Height;
        var content = new StackPanel { Margin = new(18) };
        string name = Path.GetFileName(document!.LastSavePath ?? document.SourcePath);
        content.Children.Add(new TextBlock
        {
            Text = $"Save changes to {name} as a new ROM copy?",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Discard drops changes made since the last save.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 7, 0, 0),
            Foreground = UiTheme.Muted
        });
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 16, 0, 0)
        };
        var save = new Button { Content = "Save copy…", Margin = new(0, 0, 8, 0) };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        var discard = new Button { Content = "Discard", Margin = new(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        UnsavedRomChoice choice = UnsavedRomChoice.Cancel;
        save.Click += (_, _) => { choice = UnsavedRomChoice.SaveCopy; dialog.DialogResult = true; };
        discard.Click += (_, _) => { choice = UnsavedRomChoice.Discard; dialog.DialogResult = true; };
        actions.Children.Add(save);
        actions.Children.Add(discard);
        actions.Children.Add(cancel);
        content.Children.Add(actions);
        dialog.Content = content;
        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true ? choice : UnsavedRomChoice.Cancel;
    }
}
