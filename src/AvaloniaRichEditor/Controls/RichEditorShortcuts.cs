using System.Collections.Generic;
using Avalonia.Input;

namespace AvaloniaRichEditor.Controls;

/// <summary>Identifies a command that has a keyboard shortcut. Used as the key that ties the shortcut
/// table, the editor's key handler, the context-menu hints, and the toolbar tooltips together.</summary>
internal enum ShortcutId
{
    Cut, Copy, Paste, PastePlain, SelectAll, Undo, Redo,
    Bold, Italic, Underline, Strikethrough, FontLarger, FontSmaller,
    IndentIncrease, IndentDecrease,
    AlignLeft, AlignCenter, AlignRight, AlignJustify,
    Heading1, Heading2, Heading3, Heading4, Heading5, Heading6, BodyText,
    BulletList, LineSpacingSingle, LineSpacingOneHalf, LineSpacingDouble,
}

internal readonly record struct ShortcutSpec(ShortcutId Id, bool Ctrl, bool Shift, bool Alt, Key Key, string Display);

/// <summary>The single source of truth for command keyboard shortcuts (Word-standard scheme). The editor's
/// <c>OnKeyDown</c> matches events against <see cref="All"/>; the context menu and toolbar read
/// <see cref="Display"/> for their hint text — so behavior and the shown shortcut never drift.</summary>
internal static class RichEditorShortcuts
{
    public static readonly ShortcutSpec[] All =
    {
        new(ShortcutId.Cut,           true, false, false, Key.X, "Ctrl+X"),
        new(ShortcutId.Copy,          true, false, false, Key.C, "Ctrl+C"),
        new(ShortcutId.Paste,         true, false, false, Key.V, "Ctrl+V"),
        new(ShortcutId.PastePlain,    true, true,  false, Key.V, "Ctrl+Shift+V"),
        new(ShortcutId.SelectAll,     true, false, false, Key.A, "Ctrl+A"),
        new(ShortcutId.Undo,          true, false, false, Key.Z, "Ctrl+Z"),
        new(ShortcutId.Redo,          true, false, false, Key.Y, "Ctrl+Y"),
        new(ShortcutId.Redo,          true, true,  false, Key.Z, "Ctrl+Shift+Z"), // alias (display keeps Ctrl+Y)
        new(ShortcutId.Bold,          true, false, false, Key.B, "Ctrl+B"),
        new(ShortcutId.Italic,        true, false, false, Key.I, "Ctrl+I"),
        new(ShortcutId.Underline,     true, false, false, Key.U, "Ctrl+U"),
        new(ShortcutId.Strikethrough, true, true,  false, Key.X, "Ctrl+Shift+X"),
        new(ShortcutId.FontLarger,    true, true,  false, Key.OemPeriod, "Ctrl+Shift+."),
        new(ShortcutId.FontSmaller,   true, true,  false, Key.OemComma,  "Ctrl+Shift+,"),
        new(ShortcutId.IndentIncrease,true, false, false, Key.M, "Ctrl+M"),
        new(ShortcutId.IndentDecrease,true, true,  false, Key.M, "Ctrl+Shift+M"),
        new(ShortcutId.AlignLeft,     true, false, false, Key.L, "Ctrl+L"),
        new(ShortcutId.AlignCenter,   true, false, false, Key.E, "Ctrl+E"),
        new(ShortcutId.AlignRight,    true, false, false, Key.R, "Ctrl+R"),
        new(ShortcutId.AlignJustify,  true, false, false, Key.J, "Ctrl+J"),
        new(ShortcutId.Heading1,      true, false, true,  Key.D1, "Ctrl+Alt+1"),
        new(ShortcutId.Heading2,      true, false, true,  Key.D2, "Ctrl+Alt+2"),
        new(ShortcutId.Heading3,      true, false, true,  Key.D3, "Ctrl+Alt+3"),
        new(ShortcutId.Heading4,      true, false, true,  Key.D4, "Ctrl+Alt+4"),
        new(ShortcutId.Heading5,      true, false, true,  Key.D5, "Ctrl+Alt+5"),
        new(ShortcutId.Heading6,      true, false, true,  Key.D6, "Ctrl+Alt+6"),
        new(ShortcutId.BodyText,      true, true,  false, Key.N, "Ctrl+Shift+N"),
        new(ShortcutId.BulletList,    true, true,  false, Key.L, "Ctrl+Shift+L"),
        new(ShortcutId.LineSpacingSingle,  true, false, false, Key.D1, "Ctrl+1"),
        new(ShortcutId.LineSpacingOneHalf, true, false, false, Key.D5, "Ctrl+5"),
        new(ShortcutId.LineSpacingDouble,  true, false, false, Key.D2, "Ctrl+2"),
    };

    private static readonly Dictionary<ShortcutId, string> DisplayMap = BuildDisplayMap();

    private static Dictionary<ShortcutId, string> BuildDisplayMap()
    {
        var d = new Dictionary<ShortcutId, string>();
        foreach (var s in All) d.TryAdd(s.Id, s.Display); // keep the first (primary) display per id
        return d;
    }

    /// <summary>The shortcut hint text for a command (e.g. "Ctrl+B"), or "" if none.</summary>
    public static string Display(ShortcutId id) => DisplayMap.TryGetValue(id, out var s) ? s : "";

    /// <summary>The primary shortcut as an Avalonia <see cref="KeyGesture"/> (for a menu item's display-only
    /// <c>InputGesture</c>), or null if the command has no shortcut.</summary>
    public static KeyGesture? Gesture(ShortcutId id)
    {
        foreach (var s in All)
            if (s.Id == id)
            {
                var mods = KeyModifiers.None;
                if (s.Ctrl) mods |= KeyModifiers.Control;
                if (s.Shift) mods |= KeyModifiers.Shift;
                if (s.Alt) mods |= KeyModifiers.Alt;
                return new KeyGesture(s.Key, mods);
            }
        return null;
    }

    /// <summary>Matches a key event to a command. Modifiers must match exactly.</summary>
    public static bool TryMatch(bool ctrl, bool shift, bool alt, Key key, out ShortcutId id)
    {
        foreach (var s in All)
            if (s.Ctrl == ctrl && s.Shift == shift && s.Alt == alt && s.Key == key) { id = s.Id; return true; }
        id = default;
        return false;
    }
}
