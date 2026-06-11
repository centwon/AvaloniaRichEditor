using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace AvaloniaRichEditor.Demo;

// Demonstrates the RichEditorIcons hook: maps the editor's chrome icon slots to
// FluentIcons.Avalonia symbols. The library itself ships no icon assets — remove this file
// (or never set Provider) and the toolbar/menus fall back to their built-in text glyphs.
internal static class FluentIconProvider
{
    public static void Install()
    {
        RichEditorIcons.Provider = key => key switch
        {
            // WinUI-style layered color icons: the base glyph keeps a fixed foreground, the accent
            // layer (the colour bar) has none, so it inherits — and displays — the colour the
            // toolbar pushes through the face wrapper's Foreground (caret-synced).
            RichEditorIcon.TextColor => Layered(Symbol.TextColor, Symbol.TextColorAccent),
            RichEditorIcon.Highlight => Layered(Symbol.Highlight, Symbol.HighlightAccent),
            _ => Map(key) is { } s ? new SymbolIcon { Symbol = s, FontSize = 16 } : null,
        };
    }

    // 18px (vs 16 elsewhere): these glyphs include their colour bar, so the letter part would
    // otherwise read smaller than the neighbouring icons.
    private static Control Layered(Symbol baseGlyph, Symbol accentGlyph) => new Panel
    {
        Children =
        {
            new SymbolIcon { Symbol = baseGlyph, FontSize = 18, Foreground = new SolidColorBrush(Color.Parse("#1B1B1B")) },
            new SymbolIcon { Symbol = accentGlyph, FontSize = 18 }, // no Foreground -> tinted by the toolbar
        },
    };

    private static Symbol? Map(RichEditorIcon key) => key switch
    {
        RichEditorIcon.Bold => Symbol.TextBold,
        RichEditorIcon.Italic => Symbol.TextItalic,
        RichEditorIcon.Underline => Symbol.TextUnderline,
        RichEditorIcon.Strikethrough => Symbol.TextStrikethrough,
        RichEditorIcon.FormatPainter => Symbol.PaintBrush,
        RichEditorIcon.BulletList => Symbol.TextBulletList,
        RichEditorIcon.NumberedList => Symbol.TextNumberList,
        RichEditorIcon.IndentIncrease => Symbol.TextIndentIncrease,
        RichEditorIcon.IndentDecrease => Symbol.TextIndentDecrease,
        RichEditorIcon.InsertTable => Symbol.Table,
        RichEditorIcon.InsertImage => Symbol.Image,
        RichEditorIcon.InsertDivider => Symbol.LineHorizontal1,
        RichEditorIcon.Undo => Symbol.ArrowUndo,
        RichEditorIcon.Redo => Symbol.ArrowRedo,
        RichEditorIcon.Cut => Symbol.Cut,
        RichEditorIcon.Copy => Symbol.Copy,
        RichEditorIcon.Paste => Symbol.ClipboardPaste,
        RichEditorIcon.Delete => Symbol.Delete,
        RichEditorIcon.SelectAll => Symbol.SelectAllOn,
        RichEditorIcon.ClearFormatting => Symbol.TextClearFormatting,
        RichEditorIcon.AlignLeft => Symbol.TextAlignLeft,
        RichEditorIcon.AlignCenter => Symbol.TextAlignCenter,
        RichEditorIcon.AlignRight => Symbol.TextAlignRight,
        RichEditorIcon.OpenLink => Symbol.Open,
        RichEditorIcon.EditLink => Symbol.LinkEdit,
        RichEditorIcon.RemoveLink => Symbol.LinkDismiss,
        RichEditorIcon.CopyLink => Symbol.Link,
        RichEditorIcon.InsertLink => Symbol.Link,
        RichEditorIcon.ReplaceImage => Symbol.ImageEdit,
        RichEditorIcon.SaveImageAs => Symbol.Save,
        RichEditorIcon.InsertRowAbove => Symbol.TableStackAbove,
        RichEditorIcon.InsertRowBelow => Symbol.TableStackBelow,
        RichEditorIcon.DeleteRow => Symbol.TableDeleteRow,
        RichEditorIcon.InsertColumnLeft => Symbol.TableStackLeft,
        RichEditorIcon.InsertColumnRight => Symbol.TableStackRight,
        RichEditorIcon.DeleteColumn => Symbol.TableDeleteColumn,
        RichEditorIcon.MergeCells => Symbol.TableCellsMerge,
        RichEditorIcon.UnmergeCells => Symbol.TableCellsSplit,
        RichEditorIcon.DeleteTable => Symbol.TableDismiss,
        _ => null,
    };
}
