using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace SpeedLink.Desktop;

internal enum ShortcutAction
{
    OpenRom, SaveRom, OpenRecording, KeyboardShortcuts,
    Connection, Editor, Logger, Recordings, RomChecks, Backups, FlashWorkspace,
    Undo, Redo, FindMap, ToggleBrowser, NewTab, NextTab, PreviousTab,
    MapInformation, ToggleGraph, FitMap, ExpandMap, DockMap, PinMap, CloseMap, EditTools,
    Copy, Paste, ResetCells, Increase, Decrease, IncreaseLarge, DecreaseLarge, InterpolateAcross, InterpolateDown
}

internal readonly record struct ShortcutKey(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    internal static ShortcutKey From(KeyEventArgs e) => new(e.Key == Key.System ? e.SystemKey : e.Key, e.KeyboardDevice.Modifiers);

    public override string ToString()
    {
        string label = Key is >= Key.D0 and <= Key.D9 ? (Key - Key.D0).ToString(CultureInfo.InvariantCulture) : Key.ToString();
        return (Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") +
            (Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") +
            (Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + label;
    }
}

internal sealed record ShortcutDefinition(ShortcutAction Action, string Group, string Label, ShortcutKey Default)
{
    internal bool GridOnly => Group == "Map cells";
}

internal sealed class ShortcutPreferences
{
    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys Shift = ModifierKeys.Shift;
    private const ModifierKeys Alt = ModifierKeys.Alt;
    internal static IReadOnlyList<ShortcutDefinition> Definitions { get; } =
    [
        new(ShortcutAction.OpenRom, "Application", "Open ROM", new(Key.O, Ctrl)),
        new(ShortcutAction.SaveRom, "Application", "Save ROM copy", new(Key.S, Ctrl)),
        new(ShortcutAction.OpenRecording, "Application", "Open recording", new(Key.O, Ctrl | Shift)),
        new(ShortcutAction.KeyboardShortcuts, "Application", "Open keyboard shortcuts", new(Key.K, Ctrl | Shift)),
        new(ShortcutAction.Connection, "Navigation", "Go to Connection", new(Key.D1, Ctrl)),
        new(ShortcutAction.Editor, "Navigation", "Go to Editor", new(Key.D2, Ctrl)),
        new(ShortcutAction.Logger, "Navigation", "Go to Logger", new(Key.D3, Ctrl)),
        new(ShortcutAction.Recordings, "Navigation", "Go to Recordings", new(Key.D4, Ctrl)),
        new(ShortcutAction.RomChecks, "Navigation", "Go to ROM checks", new(Key.D5, Ctrl)),
        new(ShortcutAction.Backups, "Navigation", "Go to Backups", new(Key.D6, Ctrl)),
        new(ShortcutAction.FlashWorkspace, "Navigation", "Go to Flash & Recover", new(Key.D7, Ctrl)),
        new(ShortcutAction.Undo, "Editor", "Undo ROM edit", new(Key.Z, Ctrl)),
        new(ShortcutAction.Redo, "Editor", "Redo ROM edit", new(Key.Y, Ctrl)),
        new(ShortcutAction.FindMap, "Editor", "Find a map", new(Key.F, Ctrl)),
        new(ShortcutAction.ToggleBrowser, "Editor", "Hide or restore map browser", new(Key.B, Ctrl)),
        new(ShortcutAction.NewTab, "Editor", "New workspace tab", new(Key.T, Ctrl)),
        new(ShortcutAction.NextTab, "Editor", "Next workspace tab", new(Key.Tab, Ctrl)),
        new(ShortcutAction.PreviousTab, "Editor", "Previous workspace tab", new(Key.Tab, Ctrl | Shift)),
        new(ShortcutAction.MapInformation, "Active map", "Map information", new(Key.F1)),
        new(ShortcutAction.ToggleGraph, "Active map", "Toggle table and graph", new(Key.F4)),
        new(ShortcutAction.FitMap, "Active map", "Fit entire map", new(Key.D0, Ctrl)),
        new(ShortcutAction.ExpandMap, "Active map", "Expand or restore map", new(Key.F6)),
        new(ShortcutAction.DockMap, "Active map", "Dock or undock map", new(Key.F7)),
        new(ShortcutAction.PinMap, "Active map", "Pin or unpin map", new(Key.P, Ctrl | Shift)),
        new(ShortcutAction.CloseMap, "Active map", "Close active map", new(Key.W, Ctrl)),
        new(ShortcutAction.EditTools, "Active map", "Show or hide edit tools", new(Key.F8)),
        new(ShortcutAction.Copy, "Map cells", "Copy selected cells", new(Key.C, Ctrl)),
        new(ShortcutAction.Paste, "Map cells", "Paste into selected cells", new(Key.V, Ctrl)),
        new(ShortcutAction.ResetCells, "Map cells", "Reset selected cells to baseline", new(Key.Back, Ctrl | Shift)),
        new(ShortcutAction.Increase, "Map cells", "Increase by one step", new(Key.Q)),
        new(ShortcutAction.Decrease, "Map cells", "Decrease by one step", new(Key.A)),
        new(ShortcutAction.IncreaseLarge, "Map cells", "Increase by ten steps", new(Key.Q, Shift)),
        new(ShortcutAction.DecreaseLarge, "Map cells", "Decrease by ten steps", new(Key.A, Shift)),
        new(ShortcutAction.InterpolateAcross, "Map cells", "Interpolate horizontally", new(Key.H, Shift)),
        new(ShortcutAction.InterpolateDown, "Map cells", "Interpolate vertically", new(Key.V, Shift))
    ];

    private Dictionary<ShortcutAction, ShortcutKey?> bindings = Definitions.ToDictionary(item => item.Action, item => (ShortcutKey?)item.Default);
    internal ShortcutKey? this[ShortcutAction action] => bindings[action];
    internal string Label(ShortcutAction action) => this[action]?.ToString() ?? "";
    internal ShortcutPreferences Copy() => new() { bindings = new(bindings) };
    internal void RestoreDefaults() => bindings = Definitions.ToDictionary(item => item.Action, item => (ShortcutKey?)item.Default);

    internal ShortcutAction? Find(ShortcutKey key)
    {
        foreach (var binding in bindings)
            if (binding.Value == key) return binding.Key;
        // Keep the keypad equivalent while the default Fit map binding is active.
        if (key == new ShortcutKey(Key.NumPad0, Ctrl) && this[ShortcutAction.FitMap] == new ShortcutKey(Key.D0, Ctrl))
            return ShortcutAction.FitMap;
        return null;
    }

    internal bool TryAssign(ShortcutAction action, ShortcutKey? key, out string error)
    {
        error = "";
        if (key is { } chord)
        {
            if (!CanAssign(Definitions.First(item => item.Action == action), chord))
            {
                error = "Choose a function key or use Ctrl with a letter, number or punctuation key. Grid actions also accept letters.";
                return false;
            }
            var conflict = action == ShortcutAction.FitMap && chord == new ShortcutKey(Key.D0, Ctrl)
                ? Find(chord) ?? Find(new(Key.NumPad0, Ctrl)) : Find(chord);
            if (conflict is { } used && used != action)
            {
                error = "Already assigned to " + Definitions.First(item => item.Action == used).Label + ". Clear that shortcut first.";
                return false;
            }
        }
        bindings[action] = key;
        return true;
    }

    private static bool CanAssign(ShortcutDefinition definition, ShortcutKey chord)
    {
        var key = chord.Key;
        var modifiers = chord.Modifiers;
        if ((modifiers & ~(Ctrl | Shift | Alt)) != 0 || modifiers.HasFlag(Alt) && !modifiers.HasFlag(Ctrl) ||
            key is Key.F10 or Key.Escape or Key.Enter or Key.Space) return false;
        if (key is >= Key.F1 and <= Key.F12) return true;
        if (modifiers.HasFlag(Ctrl))
            return key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.Divide or
                >= Key.OemSemicolon and <= Key.OemBackslash or Key.Back or Key.Delete or Key.Tab or Key.PageUp or Key.PageDown;
        return definition.GridOnly && key is >= Key.A and <= Key.Z;
    }

    internal static ShortcutPreferences Load(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 16_384) return new();
            var saved = JsonSerializer.Deserialize<Dictionary<string, ShortcutKey?>>(file, new JsonSerializerOptions { MaxDepth = 5 });
            if (saved is null) return new();
            var result = new ShortcutPreferences();
            foreach (var item in Definitions)
                if (saved.TryGetValue(item.Action.ToString(), out var key))
                {
                    if (key is { } chord && !CanAssign(item, chord)) return new();
                    result.bindings[item.Action] = key;
                }
            if (result.bindings.Any(binding => binding.Value is { } key && result.Find(key) != binding.Key) ||
                result.bindings[ShortcutAction.FitMap] == new ShortcutKey(Key.D0, Ctrl) &&
                result.bindings.Any(binding => binding.Value == new ShortcutKey(Key.NumPad0, Ctrl)))
                return new();
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            File.WriteAllBytes(pending, JsonSerializer.SerializeToUtf8Bytes(bindings.ToDictionary(item => item.Key.ToString(), item => item.Value)));
            File.Move(pending, path, overwrite: true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
