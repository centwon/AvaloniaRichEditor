using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Right-click context menus (per-object: text / link / inline-image / image / table) and the
// shared menu-item builders. Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    // ---------------- Context menu (right-click) ----------------

    // Menu labels come from the shared localization table (ko/en built in, host-extensible).
    // Menus are rebuilt on every right-click, so a runtime language switch needs no extra wiring.
    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    private static MenuItem Mi(string header, Action action, bool enabled = true, RichEditorIcon? icon = null)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        // Menus are rebuilt per right-click, so a host-provided icon (RichEditorIcons.Provider)
        // shows up immediately; null keeps the text-only item.
        if (icon is { } k && RichEditorIcons.TryCreate(k) is { } ic) mi.Icon = ic;
        mi.Click += (_, _) => action();
        return mi;
    }

    private static MenuItem Sub(string header, params Control[] children)
    {
        var mi = new MenuItem { Header = header };
        mi.ItemsSource = children;
        return mi;
    }

    // A context menu with a compact look: smaller font and tight row height/padding so long menus don't
    // dominate the screen. The MenuItem style applies to nested submenu items too.
    private static ContextMenu NewContextMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        menu.Styles.Add(new Style(x => x.OfType<MenuItem>())
        {
            Setters =
            {
                new Setter(MenuItem.FontSizeProperty, 12.0),
                new Setter(MenuItem.MinHeightProperty, 0.0),
                new Setter(MenuItem.PaddingProperty, new Thickness(10, 2)),
            }
        });
        return menu;
    }

    private void CollapseSelectionToCaret()
    {
        _selectionStart = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
        _selectionEnd = new TextPointer(_caretPosition.Paragraph, _caretPosition.Offset);
    }

    private void ShowContextMenu(Point point)
    {
        if (Document == null) return;

        bool hasSelection = _selectionStart.Paragraph != null && _selectionEnd.Paragraph != null
            && _selectionStart.CompareTo(_selectionEnd) != 0;

        if (IsReadOnly)
        {
            var roItems = new List<Control> { Mi(Loc("Copy"), CopySelectionToClipboard, hasSelection, RichEditorIcon.Copy), Mi(Loc("SelectAll"), SelectAll, icon: RichEditorIcon.SelectAll) };
            var roMenu = NewContextMenu();
            roMenu.ItemsSource = roItems;
            roMenu.Open(this);
            return;
        }

        var items = new List<Control>();
        var block = GetBlockAtPoint(point);

        if (block is ImageBlock ib)
        {
            _selectedBlock = ib;
            CollapseSelectionToCaret();
            BuildImageMenu(items, ib);
        }
        else if (block is TableBlock tbk)
        {
            _selectedBlock = null;
            var tp = GetPositionFromPoint(point);
            _caretPosition = tp;
            if (!hasSelection) CollapseSelectionToCaret();
            // Text selected within a single cell -> text-formatting menu; a cell-block selection (or a bare
            // caret) -> table-structure menu.
            if (hasSelection && SelectedCellRange(tbk) == null)
                BuildCellTextMenu(items, hasSelection);
            else
                BuildTableMenu(items, tbk, tp.Paragraph, hasSelection);
        }
        else
        {
            _selectedBlock = null;
            var tp = GetPositionFromPoint(point);
            // Right-clicking exactly on an inline image (and not over a selection) gets its own menu.
            var inlineImg = !hasSelection && tp.Paragraph != null ? InlineImageAt(tp.Paragraph, tp.Offset) : null;
            if (!hasSelection)
            {
                _caretPosition = tp;
                CollapseSelectionToCaret();
            }
            if (inlineImg != null && tp.Paragraph != null)
            {
                _selectedInline = (tp.Paragraph, inlineImg); // show selection chrome for the menu target
                BuildInlineImageMenu(items, tp.Paragraph, inlineImg);
            }
            else
            {
                _selectedInline = null;
                var link = GetLinkRunAtPoint(point);
                if (link != null && !string.IsNullOrEmpty(link.NavigateUri))
                    BuildLinkMenu(items, hasSelection, link);
                else
                    BuildTextMenu(items, hasSelection, link);
            }
        }

        ResetCaretBlink();
        InvalidateVisual();

        if (items.Count == 0) return;
        var menu = NewContextMenu();
        menu.ItemsSource = items;
        menu.Open(this);
    }

    private void AddClipboardItems(List<Control> items, bool hasSelection)
    {
        items.Add(Mi(Loc("Cut"), () =>
        {
            if (Document != null) PushUndo();
            CopySelectionToClipboard();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection, RichEditorIcon.Cut));
        items.Add(Mi(Loc("Copy"), CopySelectionToClipboard, hasSelection, RichEditorIcon.Copy));
        items.Add(Mi(Loc("Paste"), () => { _ = PasteFromClipboardAsync(); }, icon: RichEditorIcon.Paste));
        items.Add(Mi(Loc("Delete"), () =>
        {
            if (Document != null) PushUndo();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection, RichEditorIcon.Delete));
    }

    private void AddFormatItems(List<Control> items, bool hasSelection)
    {
        // Font submenu is built from the (host-overridable) FontFamilyChoices, so no locale is assumed.
        var fontItems = FontFamilyChoices
            .Select(f => (Control)Mi(f, () => SetFontFamily(f), hasSelection))
            .ToArray();
        // Character-level formatting, grouped under one submenu so the top level stays short.
        items.Add(Sub(Loc("CharacterFormat"),
            Mi(Loc("Bold"), ToggleBold, hasSelection, RichEditorIcon.Bold),
            Mi(Loc("Italic"), ToggleItalic, hasSelection, RichEditorIcon.Italic),
            Mi(Loc("Underline"), ToggleUnderline, hasSelection, RichEditorIcon.Underline),
            Mi(Loc("Strikethrough"), ToggleStrikethrough, hasSelection, RichEditorIcon.Strikethrough),
            new Separator(),
            Sub(Loc("FontSize"),
                Mi("10", () => SetFontSize(10)), Mi("12", () => SetFontSize(12)), Mi("14", () => SetFontSize(14)),
                Mi("18", () => SetFontSize(18)), Mi("24", () => SetFontSize(24)), Mi("36", () => SetFontSize(36))),
            Sub(Loc("TextColor"),
                Mi(Loc("ColorBlack"), () => SetForeground(Brushes.Black)), Mi(Loc("ColorRed"), () => SetForeground(Brushes.Red)),
                Mi(Loc("ColorBlue"), () => SetForeground(Brushes.Blue)), Mi(Loc("ColorGreen"), () => SetForeground(Brushes.Green)),
                Mi(Loc("ColorGray"), () => SetForeground(Brushes.Gray))),
            Sub(Loc("Highlight"),
                Mi(Loc("HighlightYellow"), () => SetHighlight(Brushes.Yellow), hasSelection),
                Mi(Loc("HighlightGreen"), () => SetHighlight(Brushes.LightGreen), hasSelection),
                Mi(Loc("HighlightPink"), () => SetHighlight(Brushes.Pink), hasSelection),
                Mi(Loc("HighlightSkyBlue"), () => SetHighlight(Brushes.LightBlue), hasSelection),
                Mi(Loc("HighlightNone"), () => SetHighlight(null), hasSelection)),
            Sub(Loc("FontFamily"), fontItems),
            new Separator(),
            Mi(Loc("ClearFormatting"), ClearFormatting, hasSelection, RichEditorIcon.ClearFormatting)));
        // Paragraph-level formatting, also grouped.
        items.Add(Sub(Loc("Paragraph"),
            Sub(Loc("Alignment"),
                Mi(Loc("AlignLeft"), () => SetTextAlignment(TextAlignment.Left), icon: RichEditorIcon.AlignLeft),
                Mi(Loc("AlignCenter"), () => SetTextAlignment(TextAlignment.Center), icon: RichEditorIcon.AlignCenter),
                Mi(Loc("AlignRight"), () => SetTextAlignment(TextAlignment.Right), icon: RichEditorIcon.AlignRight)),
            Sub(Loc("List"),
                Mi(Loc("BulletList"), ToggleBullet, icon: RichEditorIcon.BulletList),
                Mi(Loc("NumberedList"), ToggleNumbering, icon: RichEditorIcon.NumberedList)),
            Sub(Loc("Heading"),
                Mi(Loc("Heading1"), () => SetHeading(1)),
                Mi(Loc("Heading2"), () => SetHeading(2)),
                Mi(Loc("Heading3"), () => SetHeading(3)),
                Mi(Loc("BodyText"), () => SetHeading(0))),
            Sub(Loc("Indent"),
                Mi(Loc("IndentIncrease"), () => Indent(20), icon: RichEditorIcon.IndentIncrease),
                Mi(Loc("IndentDecrease"), () => Indent(-20), icon: RichEditorIcon.IndentDecrease))));
    }

    // Menu shown when text is selected inside a table cell: clipboard + grouped formatting only — no
    // table-structure or block-insert items.
    private void BuildCellTextMenu(List<Control> items, bool hasSelection)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        AddFormatItems(items, hasSelection);
    }

    /// <summary>Inserts a horizontal rule (<see cref="DividerBlock"/>) at the caret position.</summary>
    public void InsertDivider()
    {
        if (Document == null) return;
        PushUndo();
        InsertBlockAtCaret(new DividerBlock());
        InvalidateVisual();
    }

    private void BuildTextMenu(List<Control> items, bool hasSelection, Run? link)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        AddFormatItems(items, hasSelection);
        items.Add(new Separator());
        if (link != null && !string.IsNullOrEmpty(link.NavigateUri))
        {
            items.Add(Mi(Loc("OpenLink"), () => OpenUrl(link.NavigateUri!), icon: RichEditorIcon.OpenLink));
            items.Add(Mi(Loc("EditLink"), () => { _ = EditHyperlinkAsync(link.NavigateUri, link); }, icon: RichEditorIcon.EditLink));
            items.Add(Mi(Loc("RemoveLink"), () => SetHyperlink(null, link), icon: RichEditorIcon.RemoveLink));
        }
        else
        {
            items.Add(Mi(Loc("InsertLink"), () => { _ = EditHyperlinkAsync(null, null); }, hasSelection, RichEditorIcon.InsertLink));
        }
        items.Add(new Separator());
        items.Add(Mi(Loc("SelectAll"), SelectAll, icon: RichEditorIcon.SelectAll));
        items.Add(Mi(Loc("Undo"), DoUndo, icon: RichEditorIcon.Undo));
        items.Add(Mi(Loc("Redo"), DoRedo, icon: RichEditorIcon.Redo));
        // Block-insert items appear only when the corresponding feature flag is enabled (N3.5).
        if (AllowTables || AllowImages) items.Add(new Separator());
        if (AllowTables) items.Add(Mi(Loc("InsertTable2x2"), () => InsertTable(2, 2), icon: RichEditorIcon.InsertTable));
        if (AllowImages) items.Add(Mi(Loc("InsertImage"), () => { _ = InsertImageFromFileAsync(); }, icon: RichEditorIcon.InsertImage));
        if (AllowTables || AllowImages) items.Add(Mi(Loc("InsertDivider"), InsertDivider, icon: RichEditorIcon.InsertDivider));
    }

    private void BuildImageMenu(List<Control> items, ImageBlock img)
    {
        items.Add(Mi(Loc("Copy"), () => { _ = CopyImageToClipboardAsync(img.RawBytes, img.Image, inline: false, img.Width, img.Height); }, img.RawBytes != null || img.Image != null, RichEditorIcon.Copy));
        items.Add(Mi(Loc("Delete"), () => DeleteBlock(img), icon: RichEditorIcon.Delete));
        items.Add(new Separator());
        items.Add(Mi(Loc("OriginalSize"), () => ResetImageSize(img), img.Image != null));
        // Scale presets relative to the natural size. Width/Height only — the encoded bytes are
        // untouched (no generation loss), matching the drag-handle behavior.
        items.Add(Mi(Loc("HalfSize"), () => ScaleImageSize(img, 1.0 / 2), img.Image != null));
        items.Add(Mi(Loc("ThirdSize"), () => ScaleImageSize(img, 1.0 / 3), img.Image != null));
        items.Add(Mi(Loc("QuarterSize"), () => ScaleImageSize(img, 1.0 / 4), img.Image != null));
        items.Add(Mi(Loc("ReplaceImage"), () => { _ = ReplaceImageAsync(img); }, icon: RichEditorIcon.ReplaceImage));
        items.Add(Mi(Loc("SaveImageAs"), () => { _ = SaveImageAsync(img); }, img.Image != null, RichEditorIcon.SaveImageAs));
        items.Add(new Separator());
        // HWP-style toggle: unchecked here (block image); checking it demotes to an inline character.
        var asChar = new MenuItem { Header = Loc("InlineWithText"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = false };
        asChar.Click += (_, _) => ConvertImageBlockToInline(img);
        items.Add(asChar);
    }

    // Concise menu shown when right-clicking a hyperlink: link actions + copy, no formatting clutter.
    private void BuildLinkMenu(List<Control> items, bool hasSelection, Run link)
    {
        items.Add(Mi(Loc("OpenLink"), () => OpenUrl(link.NavigateUri!), icon: RichEditorIcon.OpenLink));
        items.Add(Mi(Loc("EditLink"), () => { _ = EditHyperlinkAsync(link.NavigateUri, link); }, icon: RichEditorIcon.EditLink));
        items.Add(Mi(Loc("RemoveLink"), () => SetHyperlink(null, link), icon: RichEditorIcon.RemoveLink));
        items.Add(Mi(Loc("CopyLink"), () =>
        {
            if (link.NavigateUri != null) TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(link.NavigateUri);
        }, !string.IsNullOrEmpty(link.NavigateUri), RichEditorIcon.CopyLink));
        items.Add(new Separator());
        items.Add(Mi(Loc("Copy"), CopySelectionToClipboard, hasSelection, RichEditorIcon.Copy));
    }

    // Menu for an inline image (small in-paragraph icon): mirrors the block-image menu but operates on
    // the InlineImage in place.
    private void BuildInlineImageMenu(List<Control> items, Paragraph p, InlineImage img)
    {
        items.Add(Mi(Loc("Copy"), () => { _ = CopyImageToClipboardAsync(img.RawBytes, img.Image, inline: true, img.Width, img.Height); }, img.RawBytes != null || img.Image != null, RichEditorIcon.Copy));
        items.Add(Mi(Loc("Delete"), () => DeleteInlineImage(p, img), icon: RichEditorIcon.Delete));
        items.Add(new Separator());
        items.Add(Mi(Loc("OriginalSize"), () => ResetInlineImageSize(img), img.Image != null));
        items.Add(Mi(Loc("HalfSize"), () => ScaleInlineImageSize(img, 1.0 / 2), img.Image != null));
        items.Add(Mi(Loc("ThirdSize"), () => ScaleInlineImageSize(img, 1.0 / 3), img.Image != null));
        items.Add(Mi(Loc("QuarterSize"), () => ScaleInlineImageSize(img, 1.0 / 4), img.Image != null));
        items.Add(Mi(Loc("ReplaceImage"), () => { _ = ReplaceInlineImageAsync(img); }, icon: RichEditorIcon.ReplaceImage));
        items.Add(Mi(Loc("SaveImageAs"), () => { _ = SaveBitmapAsync(img.Image); }, img.Image != null, RichEditorIcon.SaveImageAs));
        items.Add(new Separator());
        // Checked here (inline = treated as a character). Unchecking promotes back to a block image;
        // disabled inside table cells, which cannot host block siblings.
        bool canBlock = Document != null && Document.Blocks.IndexOf(p) >= 0;
        var asChar = new MenuItem { Header = Loc("InlineWithText"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = true, IsEnabled = canBlock };
        asChar.Click += (_, _) => ConvertInlineImageToBlock(p, img);
        items.Add(asChar);
    }

    private void BuildTableMenu(List<Control> items, TableBlock tb, Paragraph? cell, bool hasSelection)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        var loc = cell != null ? FindCell(cell) : null;
        int r = loc?.r ?? -1;
        int c = loc?.c ?? -1;
        items.Add(Mi(Loc("InsertRowAbove"), () => TableInsertRow(tb, r), r >= 0, RichEditorIcon.InsertRowAbove));
        items.Add(Mi(Loc("InsertRowBelow"), () => TableInsertRow(tb, r + 1), r >= 0, RichEditorIcon.InsertRowBelow));
        items.Add(Mi(Loc("DeleteRow"), () => TableDeleteRow(tb, r), r >= 0 && tb.Rows > 1, RichEditorIcon.DeleteRow));
        items.Add(new Separator());
        items.Add(Mi(Loc("InsertColumnLeft"), () => TableInsertColumn(tb, c), c >= 0, RichEditorIcon.InsertColumnLeft));
        items.Add(Mi(Loc("InsertColumnRight"), () => TableInsertColumn(tb, c + 1), c >= 0, RichEditorIcon.InsertColumnRight));
        items.Add(Mi(Loc("DeleteColumn"), () => TableDeleteColumn(tb, c), c >= 0 && tb.Columns > 1, RichEditorIcon.DeleteColumn));
        items.Add(new Separator());
        var range = SelectedCellRange(tb);
        bool canMerge = range is { } rg && IsCleanRect(tb, rg.r0, rg.c0, rg.r1, rg.c1);
        items.Add(Mi(Loc("MergeCells"), () =>
        {
            if (range is not { } g) return;
            if (Document != null) PushUndo();
            tb.MergeCells(g.r0, g.c0, g.r1, g.c1);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[g.r0][g.c0]);
            InvalidateVisual();
        }, canMerge, RichEditorIcon.MergeCells));
        bool canUnmerge = r >= 0 && c >= 0 && (tb.SpanOf(r, c).cs > 1 || tb.SpanOf(r, c).rs > 1);
        items.Add(Mi(Loc("UnmergeCells"), () =>
        {
            if (Document != null) PushUndo();
            tb.UnmergeCell(r, c);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[r][c]);
            InvalidateVisual();
        }, canUnmerge, RichEditorIcon.UnmergeCells));
        items.Add(new Separator());
        items.Add(Mi(Loc("DeleteTable"), () => DeleteBlock(tb), icon: RichEditorIcon.DeleteTable));
    }
}
