using System;
using Avalonia.Controls;

namespace AvaloniaRichEditor.Controls;

/// <summary>
/// Identifies a built-in chrome icon slot (toolbar button or context-menu item) that a host can
/// replace via <see cref="RichEditorIcons.Provider"/>.
/// </summary>
public enum RichEditorIcon
{
    // Toolbar (character formatting)
    /// <summary>Bold toggle.</summary>
    Bold,
    /// <summary>Italic toggle.</summary>
    Italic,
    /// <summary>Underline toggle.</summary>
    Underline,
    /// <summary>Strikethrough toggle.</summary>
    Strikethrough,
    /// <summary>Format painter.</summary>
    FormatPainter,
    /// <summary>Text (foreground) colour picker.</summary>
    TextColor,
    /// <summary>Highlight (background) colour picker.</summary>
    Highlight,
    // Toolbar (paragraph / inserts / history)
    /// <summary>Bulleted list toggle.</summary>
    BulletList,
    /// <summary>Numbered list toggle.</summary>
    NumberedList,
    /// <summary>Increase indent.</summary>
    IndentIncrease,
    /// <summary>Decrease indent.</summary>
    IndentDecrease,
    /// <summary>Insert table.</summary>
    InsertTable,
    /// <summary>Insert image.</summary>
    InsertImage,
    /// <summary>Insert horizontal divider.</summary>
    InsertDivider,
    /// <summary>Undo.</summary>
    Undo,
    /// <summary>Redo.</summary>
    Redo,
    // Context menu (clipboard / selection)
    /// <summary>Cut.</summary>
    Cut,
    /// <summary>Copy.</summary>
    Copy,
    /// <summary>Paste.</summary>
    Paste,
    /// <summary>Delete selection / object.</summary>
    Delete,
    /// <summary>Select all.</summary>
    SelectAll,
    /// <summary>Clear formatting.</summary>
    ClearFormatting,
    // Context menu (alignment)
    /// <summary>Align left.</summary>
    AlignLeft,
    /// <summary>Align center.</summary>
    AlignCenter,
    /// <summary>Align right.</summary>
    AlignRight,
    // Context menu (links)
    /// <summary>Open hyperlink.</summary>
    OpenLink,
    /// <summary>Edit hyperlink.</summary>
    EditLink,
    /// <summary>Remove hyperlink.</summary>
    RemoveLink,
    /// <summary>Copy hyperlink address.</summary>
    CopyLink,
    /// <summary>Insert hyperlink.</summary>
    InsertLink,
    // Context menu (image)
    /// <summary>Replace image.</summary>
    ReplaceImage,
    /// <summary>Save image to a file.</summary>
    SaveImageAs,
    // Context menu (table structure)
    /// <summary>Insert row above.</summary>
    InsertRowAbove,
    /// <summary>Insert row below.</summary>
    InsertRowBelow,
    /// <summary>Delete row.</summary>
    DeleteRow,
    /// <summary>Insert column to the left.</summary>
    InsertColumnLeft,
    /// <summary>Insert column to the right.</summary>
    InsertColumnRight,
    /// <summary>Delete column.</summary>
    DeleteColumn,
    /// <summary>Merge cells.</summary>
    MergeCells,
    /// <summary>Unmerge cell.</summary>
    UnmergeCells,
    /// <summary>Delete table.</summary>
    DeleteTable,
}

/// <summary>
/// Host-pluggable icon factory for the built-in chrome (toolbar buttons and context menus).
/// By default the chrome uses lightweight text glyphs and the library carries no icon assets;
/// assign <see cref="Provider"/> to swap in an icon library of your choice (e.g. FluentIcons.Avalonia):
/// <code>RichEditorIcons.Provider = key => new SymbolIcon { Symbol = Map(key), FontSize = 16 };</code>
/// The factory is called once per icon slot whenever the chrome is (re)built, and must return a new
/// <see cref="Control"/> instance each call (a control can only have one parent). Return null to keep
/// the built-in glyph for that slot. Global, like <see cref="RichEditorLocalization"/>; set it before
/// the first toolbar/menu is built, or rebuild afterwards.
/// <para>Color-picker slots (<see cref="RichEditorIcon.TextColor"/>/<see cref="RichEditorIcon.Highlight"/>):
/// a provided icon replaces the whole button face including the swatch bar, and the toolbar pushes the
/// caret's current colour through the face's inherited Foreground. Layer your icon WinUI-style — a base
/// glyph with an explicit Foreground under an accent (bar) layer without one — so only the bar shows
/// the colour.</para>
/// </summary>
public static class RichEditorIcons
{
    /// <summary>Factory invoked for each icon slot when the chrome is built; null (or a null
    /// return value) keeps the built-in text glyph for that slot.</summary>
    public static Func<RichEditorIcon, Control?>? Provider { get; set; }

    // Single internal lookup so call sites don't repeat the null-provider dance.
    internal static Control? TryCreate(RichEditorIcon icon) => Provider?.Invoke(icon);
}
