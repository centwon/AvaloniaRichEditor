using System.Collections.Generic;
using Avalonia.Input;

namespace AvaloniaRichEditor.Controls;

/// <summary>Identifies a command that has a keyboard shortcut. The key that ties the shortcut table, the
/// editor's key handler, the context-menu hints and the toolbar tooltips together.
/// <para>Public since 1.3.0, so a host building its own toolbar or menu can label a command with the
/// gesture the editor actually acts on. New commands are appended, so the numeric values keep their
/// meaning (as <see cref="RichEditorIcon"/> does).</para></summary>
public enum RichEditorShortcutId
{
    /// <summary>Cut the selection to the clipboard.</summary>
    Cut,
    /// <summary>Copy the selection to the clipboard.</summary>
    Copy,
    /// <summary>Paste, keeping whatever formatting the clipboard carries.</summary>
    Paste,
    /// <summary>Paste the clipboard's plain text only.</summary>
    PastePlain,
    /// <summary>Select the cell's contents, then the table, then the document (staged).</summary>
    SelectAll,
    /// <summary>Undo the last edit.</summary>
    Undo,
    /// <summary>Redo the last undone edit.</summary>
    Redo,
    /// <summary>Bold toggle.</summary>
    Bold,
    /// <summary>Italic toggle.</summary>
    Italic,
    /// <summary>Underline toggle.</summary>
    Underline,
    /// <summary>Strikethrough toggle.</summary>
    Strikethrough,
    /// <summary>Next size up the standard ladder.</summary>
    FontLarger,
    /// <summary>Next size down the standard ladder.</summary>
    FontSmaller,
    /// <summary>Indent the paragraph one step.</summary>
    IndentIncrease,
    /// <summary>Outdent the paragraph one step.</summary>
    IndentDecrease,
    /// <summary>Align the paragraph left.</summary>
    AlignLeft,
    /// <summary>Centre the paragraph.</summary>
    AlignCenter,
    /// <summary>Align the paragraph right.</summary>
    AlignRight,
    /// <summary>Justify the paragraph.</summary>
    AlignJustify,
    /// <summary>Make the paragraph a level 1 heading.</summary>
    Heading1,
    /// <summary>Make the paragraph a level 2 heading.</summary>
    Heading2,
    /// <summary>Make the paragraph a level 3 heading.</summary>
    Heading3,
    /// <summary>Make the paragraph a level 4 heading.</summary>
    Heading4,
    /// <summary>Make the paragraph a level 5 heading.</summary>
    Heading5,
    /// <summary>Make the paragraph a level 6 heading.</summary>
    Heading6,
    /// <summary>Make the paragraph body text (heading level 0).</summary>
    BodyText,
    /// <summary>Bulleted list toggle.</summary>
    BulletList,
    /// <summary>Numbered list toggle.</summary>
    NumberedList,
    /// <summary>Line spacing 100% of the font size.</summary>
    LineSpacingSingle,
    /// <summary>Line spacing 150% of the font size.</summary>
    LineSpacingOneHalf,
    /// <summary>Line spacing 200% of the font size.</summary>
    LineSpacingDouble,
    /// <summary>Select the caret's cell as a block (HWP's cell block key).</summary>
    SelectCell,
    /// <summary>Open the link dialog for the selection or the caret's word.</summary>
    InsertLink,
    /// <summary>Find.</summary>
    Find,
    /// <summary>Find and replace.</summary>
    FindReplace,
}

/// <summary>One shortcut in the table: the command, the modifiers it needs (each must match exactly), the
/// key, and the hint text to show for it (e.g. "Ctrl+B").</summary>
public readonly record struct RichEditorShortcut(RichEditorShortcutId Id, bool Ctrl, bool Shift, bool Alt, Key Key, string Display);

/// <summary>The single source of truth for command keyboard shortcuts (Word-standard scheme). The editor's
/// <c>OnKeyDown</c> matches events against this table; the context menu and toolbar read
/// <see cref="Display"/> for their hint text — so behavior and the shown shortcut never drift.
/// <para>A host that builds its own toolbar or menu reads it the same way, instead of writing "Ctrl+B"
/// again somewhere the editor cannot see.</para></summary>
public static class RichEditorShortcuts
{
    // Public callers get the list through All, which hands out no reference to this array: a host that
    // could reorder or blank an entry would be editing what the editor's own key handler matches against.
    private static readonly RichEditorShortcut[] Table =
    {
        new(RichEditorShortcutId.Cut,           true, false, false, Key.X, "Ctrl+X"),
        new(RichEditorShortcutId.Copy,          true, false, false, Key.C, "Ctrl+C"),
        new(RichEditorShortcutId.Paste,         true, false, false, Key.V, "Ctrl+V"),
        new(RichEditorShortcutId.PastePlain,    true, true,  false, Key.V, "Ctrl+Shift+V"),
        new(RichEditorShortcutId.SelectAll,     true, false, false, Key.A, "Ctrl+A"),
        new(RichEditorShortcutId.Undo,          true, false, false, Key.Z, "Ctrl+Z"),
        new(RichEditorShortcutId.Redo,          true, false, false, Key.Y, "Ctrl+Y"),
        new(RichEditorShortcutId.Redo,          true, true,  false, Key.Z, "Ctrl+Shift+Z"), // alias (display keeps Ctrl+Y)
        new(RichEditorShortcutId.Bold,          true, false, false, Key.B, "Ctrl+B"),
        new(RichEditorShortcutId.Italic,        true, false, false, Key.I, "Ctrl+I"),
        new(RichEditorShortcutId.Underline,     true, false, false, Key.U, "Ctrl+U"),
        new(RichEditorShortcutId.Strikethrough, true, true,  false, Key.X, "Ctrl+Shift+X"),
        new(RichEditorShortcutId.FontLarger,    true, true,  false, Key.OemPeriod, "Ctrl+Shift+."),
        new(RichEditorShortcutId.FontSmaller,   true, true,  false, Key.OemComma,  "Ctrl+Shift+,"),
        new(RichEditorShortcutId.IndentIncrease,true, false, false, Key.M, "Ctrl+M"),
        new(RichEditorShortcutId.IndentDecrease,true, true,  false, Key.M, "Ctrl+Shift+M"),
        new(RichEditorShortcutId.AlignLeft,     true, false, false, Key.L, "Ctrl+L"),
        new(RichEditorShortcutId.AlignCenter,   true, false, false, Key.E, "Ctrl+E"),
        new(RichEditorShortcutId.AlignRight,    true, false, false, Key.R, "Ctrl+R"),
        new(RichEditorShortcutId.AlignJustify,  true, false, false, Key.J, "Ctrl+J"),
        new(RichEditorShortcutId.Heading1,      true, false, true,  Key.D1, "Ctrl+Alt+1"),
        new(RichEditorShortcutId.Heading2,      true, false, true,  Key.D2, "Ctrl+Alt+2"),
        new(RichEditorShortcutId.Heading3,      true, false, true,  Key.D3, "Ctrl+Alt+3"),
        new(RichEditorShortcutId.Heading4,      true, false, true,  Key.D4, "Ctrl+Alt+4"),
        new(RichEditorShortcutId.Heading5,      true, false, true,  Key.D5, "Ctrl+Alt+5"),
        new(RichEditorShortcutId.Heading6,      true, false, true,  Key.D6, "Ctrl+Alt+6"),
        new(RichEditorShortcutId.BodyText,      true, true,  false, Key.N, "Ctrl+Shift+N"),
        new(RichEditorShortcutId.BulletList,    true, true,  false, Key.L, "Ctrl+Shift+L"),
        new(RichEditorShortcutId.NumberedList,  true, true,  false, Key.D7, "Ctrl+Shift+7"), // Docs convention; Word has no standard binding (as the WinUI port)
        new(RichEditorShortcutId.LineSpacingSingle,  true, false, false, Key.D1, "Ctrl+1"),
        new(RichEditorShortcutId.LineSpacingOneHalf, true, false, false, Key.D5, "Ctrl+5"),
        new(RichEditorShortcutId.LineSpacingDouble,  true, false, false, Key.D2, "Ctrl+2"),
        // Word's Insert Hyperlink (from the WinUI port, where it has been bound since the hyperlink audit).
        new(RichEditorShortcutId.InsertLink,         true, false, false, Key.K, "Ctrl+K"),
        new(RichEditorShortcutId.Find,               true, false, false, Key.F, "Ctrl+F"),
        new(RichEditorShortcutId.FindReplace,        true, false, false, Key.H, "Ctrl+H"),
        // Not Ctrl-modified, so TryMatch (reached only with Ctrl) never runs it: OnKeyDown routes F5 through
        // TryCellBlockKey. Listed for the menu hint (HWP's cell block key; the WinUI port lists it the same).
        new(RichEditorShortcutId.SelectCell,         false, false, false, Key.F5, "F5"),
    };

    /// <summary>Every shortcut, in the order the key handler matches them. A command can appear more than
    /// once (an alias, such as Ctrl+Shift+Z for redo); the first entry is its primary one.</summary>
    public static IReadOnlyList<RichEditorShortcut> All { get; } = System.Array.AsReadOnly(Table);

    private static readonly Dictionary<RichEditorShortcutId, string> DisplayMap = BuildDisplayMap();

    private static Dictionary<RichEditorShortcutId, string> BuildDisplayMap()
    {
        var d = new Dictionary<RichEditorShortcutId, string>();
        foreach (var s in Table) d.TryAdd(s.Id, s.Display); // keep the first (primary) display per id
        return d;
    }

    /// <summary>The shortcut hint text for a command (e.g. "Ctrl+B"), or "" if none.</summary>
    public static string Display(RichEditorShortcutId id) => DisplayMap.TryGetValue(id, out var s) ? s : "";

    /// <summary>The primary shortcut as an Avalonia <see cref="KeyGesture"/> (for a menu item's display-only
    /// <c>InputGesture</c>), or null if the command has no shortcut.</summary>
    public static KeyGesture? Gesture(RichEditorShortcutId id)
    {
        foreach (var s in Table)
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
    // Internal on purpose: a host that wants this can match against All itself, and the editor keeps the
    // freedom to change how it routes keys (F5, for one, is in the table only as a menu hint).
    internal static bool TryMatch(bool ctrl, bool shift, bool alt, Key key, out RichEditorShortcutId id)
    {
        foreach (var s in Table)
            if (s.Ctrl == ctrl && s.Shift == shift && s.Alt == alt && s.Key == key) { id = s.Id; return true; }
        id = default;
        return false;
    }
}
