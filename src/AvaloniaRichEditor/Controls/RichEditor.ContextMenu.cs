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

    /// <summary>When <see langword="true"/>, the right-click menu includes the full character/paragraph/
    /// list/heading formatting groups. Default <see langword="false"/> (slim): the menu shows only clipboard,
    /// quick bold/italic/underline toggles, hyperlink and inserts — the rich formatting lives on the toolbar.
    /// A standalone host without a toolbar can opt in.</summary>
    public static readonly StyledProperty<bool> ShowFormattingMenuProperty =
        AvaloniaProperty.Register<RichEditor, bool>(nameof(ShowFormattingMenu));

    /// <summary>Gets or sets whether the right-click menu carries the full formatting groups. Default false.</summary>
    public bool ShowFormattingMenu
    {
        get => GetValue(ShowFormattingMenuProperty);
        set => SetValue(ShowFormattingMenuProperty, value);
    }

    // Menu labels come from the shared localization table (ko/en built in, host-extensible).
    // Menus are rebuilt on every right-click, so a runtime language switch needs no extra wiring.
    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    // The currently open context menu, so an interactive submenu item (the table-size grid picker)
    // can dismiss it after acting. Set just before Open.
    private ContextMenu? _openContextMenu;

    private static MenuItem Mi(string header, Action action, bool enabled = true, RichEditorIcon? icon = null, KeyGesture? gesture = null)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        // Menus are rebuilt per right-click, so a host-provided icon (RichEditorIcons.Provider)
        // shows up immediately; null keeps the text-only item.
        if (icon is { } k && RichEditorIcons.TryCreate(k) is { } ic) mi.Icon = ic;
        // Display-only shortcut hint (the editor handles keys itself in OnKeyDown, so InputGesture must not
        // be bound as a live accelerator — Avalonia's MenuItem shows it without invoking it).
        if (gesture != null) mi.InputGesture = gesture;
        mi.Click += (_, _) => action();
        return mi;
    }

    // A checkable toggle item (checkbox) reflecting a current on/off state — e.g. a bold toggle or a list state.
    private static MenuItem CheckItem(string header, bool isChecked, Action action, bool enabled = true, KeyGesture? gesture = null)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled, ToggleType = MenuItemToggleType.CheckBox, IsChecked = isChecked };
        if (gesture != null) mi.InputGesture = gesture;
        mi.Click += (_, _) => action();
        return mi;
    }

    // A radio item within a named group, checked when it matches the current value.
    private static MenuItem RadioItem(string header, string group, bool isChecked, Action action, KeyGesture? gesture = null)
    {
        var mi = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, GroupName = group, IsChecked = isChecked };
        if (gesture != null) mi.InputGesture = gesture;
        mi.Click += (_, _) => action();
        return mi;
    }

    private static MenuItem Sub(string header, params Control[] children)
    {
        var mi = new MenuItem { Header = header };
        mi.ItemsSource = children;
        return mi;
    }

    // "Insert table" submenu carrying an 8×10 grid picker (hover to choose rows×columns, click to
    // insert) — the same affordance as the toolbar button, replacing the old fixed 2×2 item.
    private MenuItem BuildInsertTableMenu()
    {
        const int rows = 8, cols = 10;
        var cells = new Border[rows, cols];
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = cols, Rows = rows };
        var label = new TextBlock
        {
            Text = Loc("DragToSelectSize"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var active = new SolidColorBrush(Color.Parse("#90CAF9"));
        void Highlight(int hr, int hc)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    cells[r, c].Background = (r <= hr && c <= hc) ? active : Brushes.White;
            label.Text = $"{hr + 1} × {hc + 1}";
        }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int rr = r, cc = c;
                var cell = new Border
                {
                    Width = 16, Height = 16, Margin = new Thickness(1),
                    Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                };
                cell.PointerEntered += (_, _) => Highlight(rr, cc);
                cell.PointerPressed += (_, _) => { BeginTableDraw(rr + 1, cc + 1); _openContextMenu?.Close(); };
                cells[r, c] = cell;
                grid.Children.Add(cell);
            }
        var panel = new StackPanel { Margin = new Thickness(8, 6) };
        panel.Children.Add(grid);
        panel.Children.Add(label);
        var mi = new MenuItem { Header = Loc("InsertTable") };
        if (RichEditorIcons.TryCreate(RichEditorIcon.InsertTable) is { } ic) mi.Icon = ic;
        mi.ItemsSource = new Control[] { panel };
        return mi;
    }

    // "Margin" submenu with per-side presets for a block (paragraph, image, or table). The current
    // value is radio-checked; picking one pushes an undo checkpoint and re-measures. Left maps to
    // Block.Indent (the existing left offset); right exists for paragraphs only — nothing flows
    // around images/tables, so a right margin would be invisible there.
    private MenuItem MarginMenu(Block target)
    {
        Control[] Presets(Func<double> get, Action<double> set)
        {
            var items = new List<Control>();
            foreach (double v in new[] { 0d, 5, 10, 20, 30 })
            {
                var mi = new MenuItem
                {
                    Header = $"{v:0} px",
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = Math.Abs(get() - v) < 0.5,
                };
                mi.Click += (_, _) =>
                {
                    if (Document != null) PushUndo();
                    set(v);
                    NotifyStatus(); // content size changed -> re-measure scroll extent
                    InvalidateVisual();
                };
                items.Add(mi);
            }
            return items.ToArray();
        }
        var sides = new List<Control>
        {
            Sub(Loc("MarginTop"), Presets(() => target.MarginTop, v => target.MarginTop = v)),
            Sub(Loc("MarginBottom"), Presets(() => target.MarginBottom, v => target.MarginBottom = v)),
            Sub(Loc("MarginLeft"), Presets(() => target.Indent, v => target.Indent = v)),
        };
        if (target is Paragraph mp)
            sides.Add(Sub(Loc("MarginRight"), Presets(() => mp.MarginRight, v => mp.MarginRight = v)));
        return Sub(Loc("Margin"), sides.ToArray());
    }

    // A context menu with a compact look: smaller font and tight row height/padding so long menus don't
    // dominate the screen. The MenuItem style applies to nested submenu items too.
    private ContextMenu NewContextMenu()
    {
        // Pin the menu's font to the editor's UI font. A ContextMenu opened on this control otherwise
        // inherits FontFamily down from it, so it would drift with whatever the host/theme sets — the
        // menu is chrome and must stay on a stable UI face, never the document's per-run font.
        var menu = new ContextMenu { Placement = PlacementMode.Pointer, FontFamily = DefaultFontFamily };
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
            var roItems = new List<Control> { Mi(Loc("Copy"), CopySelectionToClipboard, hasSelection, RichEditorIcon.Copy, RichEditorShortcuts.Gesture(ShortcutId.Copy)), Mi(Loc("SelectAll"), SelectAll, icon: RichEditorIcon.SelectAll, gesture: RichEditorShortcuts.Gesture(ShortcutId.SelectAll)) };
            var roMenu = NewContextMenu();
            roMenu.ItemsSource = roItems;
            _openContextMenu = roMenu;
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
        else if (CellImageAtPoint(point) is { } cellImg)
        {
            // A block image inside a table cell (P4-2b): GetBlockAtPoint returns the enclosing table, so
            // detect the image via its rendered rect and give it the same image menu as a top-level one.
            _selectedBlock = cellImg;
            CollapseSelectionToCaret();
            BuildImageMenu(items, cellImg);
        }
        else if (block is TableBlock tbk)
        {
            _selectedBlock = null;
            var tp = GetPositionFromPoint(point);
            _caretPosition = tp;
            if (!hasSelection) CollapseSelectionToCaret();
            // The table is "selected as a structure" when in cell-selection mode, when the whole table
            // carries the block caret, or when the drag selection spans cells. In those cases show the
            // table-structure menu. Otherwise the user is editing inside a cell (bare caret or text within
            // one cell) -> text-formatting menu with the table ops tucked into a "Table" submenu.
            bool tableStructureMode = (_cellSelMode && _cellSelTable == tbk)
                || ReferenceEquals(_caretBlock, tbk)
                || (hasSelection && SelectedCellRange(tbk) != null);
            if (tableStructureMode)
                BuildTableMenu(items, tbk, tp.Paragraph, hasSelection);
            else
                // Editing inside a cell: same caret menu as a top-level paragraph, with the table ops in a
                // submenu. Target the INNERMOST table the caret is in (a nested table — P4-2b), not the
                // top-level one GetBlockAtPoint returned, so row/column/merge act on the right table.
                BuildCaretMenu(items, point, hasSelection,
                    (tp.Paragraph != null ? FindCell(tp.Paragraph)?.tb : null) ?? tbk);
        }
        else
        {
            _selectedBlock = null;
            BuildCaretMenu(items, point, hasSelection, null);
        }

        ResetCaretBlink();
        InvalidateVisual();

        if (items.Count == 0) return;
        var menu = NewContextMenu();
        menu.ItemsSource = items;
        _openContextMenu = menu;
        menu.Open(this);
    }

    // The block image inside a table cell whose rendered rect contains p (P4-2b), or null. Mirrors the
    // click-selection lookup; populated each render in _cellImageRects.
    private ImageBlock? CellImageAtPoint(Point p)
    {
        foreach (var ci in _cellImageRects)
            if (ci.rect.Contains(p)) return ci.img;
        return null;
    }

    // The caret-position menu (inline-image / hyperlink / text), shared by top-level paragraphs and
    // table cells so the two stay identical. `cellTable` non-null means the caret is inside that table's
    // cell: BuildTextMenu then drops the table-insert picker and appends a "Table" submenu.
    private void BuildCaretMenu(List<Control> items, Point point, bool hasSelection, TableBlock? cellTable)
    {
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
                BuildTextMenu(items, hasSelection, link, cellTable);
        }
    }

    private void AddClipboardItems(List<Control> items, bool hasSelection)
    {
        items.Add(Mi(Loc("Cut"), () =>
        {
            if (Document != null) PushUndo();
            CopySelectionToClipboard();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection, RichEditorIcon.Cut, RichEditorShortcuts.Gesture(ShortcutId.Cut)));
        items.Add(Mi(Loc("Copy"), CopySelectionToClipboard, hasSelection, RichEditorIcon.Copy, RichEditorShortcuts.Gesture(ShortcutId.Copy)));
        items.Add(Mi(Loc("Paste"), () => { _ = PasteFromClipboardAsync(); }, icon: RichEditorIcon.Paste, gesture: RichEditorShortcuts.Gesture(ShortcutId.Paste)));
        items.Add(Mi(Loc("Delete"), () =>
        {
            if (Document != null) PushUndo();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection, RichEditorIcon.Delete));
    }

    // ── 글자 모양 (character shape) ── quick toggles (checked) + font larger/smaller + clear. The precise
    // pickers (specific size, text color, highlight, font family) are toolbar-only, HWP-style.
    private MenuItem CharacterFormatSub(CaretFormat fmt, bool hasSelection) => Sub(Loc("CharacterFormat"),
        CheckItem(Loc("Bold"), fmt.Bold, ToggleBold, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Bold)),
        CheckItem(Loc("Italic"), fmt.Italic, ToggleItalic, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Italic)),
        CheckItem(Loc("Underline"), fmt.Underline, ToggleUnderline, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Underline)),
        CheckItem(Loc("Strikethrough"), fmt.Strike, ToggleStrikethrough, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Strikethrough)),
        new Separator(),
        Mi(Loc("FontSizeIncrease"), IncreaseFontSize, true, RichEditorIcon.FontSizeIncrease, RichEditorShortcuts.Gesture(ShortcutId.FontLarger)),
        Mi(Loc("FontSizeDecrease"), DecreaseFontSize, true, RichEditorIcon.FontSizeDecrease, RichEditorShortcuts.Gesture(ShortcutId.FontSmaller)),
        new Separator(),
        Mi(Loc("ClearFormatting"), ClearFormatting, hasSelection, RichEditorIcon.ClearFormatting));

    // 문단 모양 (paragraph shape): alignment as a radio group (current value checked) + indent + margin,
    // flattened into one level (HWP-style — no nested 정렬/여백 submenus).
    private MenuItem BuildParagraphFormatSub(CaretFormat fmt)
    {
        var children = new List<Control>
        {
            RadioItem(Loc("AlignLeft"), "ctxAlign", fmt.Align == TextAlignment.Left, () => SetTextAlignment(TextAlignment.Left), RichEditorShortcuts.Gesture(ShortcutId.AlignLeft)),
            RadioItem(Loc("AlignCenter"), "ctxAlign", fmt.Align == TextAlignment.Center, () => SetTextAlignment(TextAlignment.Center), RichEditorShortcuts.Gesture(ShortcutId.AlignCenter)),
            RadioItem(Loc("AlignRight"), "ctxAlign", fmt.Align == TextAlignment.Right, () => SetTextAlignment(TextAlignment.Right), RichEditorShortcuts.Gesture(ShortcutId.AlignRight)),
            RadioItem(Loc("AlignJustify"), "ctxAlign", fmt.Align == TextAlignment.Justify, () => SetTextAlignment(TextAlignment.Justify), RichEditorShortcuts.Gesture(ShortcutId.AlignJustify)),
            new Separator(),
            Mi(Loc("IndentIncrease"), () => Indent(20), true, RichEditorIcon.IndentIncrease, RichEditorShortcuts.Gesture(ShortcutId.IndentIncrease)),
            Mi(Loc("IndentDecrease"), () => Indent(-20), true, RichEditorIcon.IndentDecrease, RichEditorShortcuts.Gesture(ShortcutId.IndentDecrease)),
        };
        // Margin (top-level paragraphs only — cell paragraphs lay out inside the cell).
        if (_caretPosition.Paragraph is { } mp && Document != null && Document.Blocks.IndexOf(mp) >= 0)
        {
            children.Add(new Separator());
            children.Add(MarginMenu(mp));
        }
        return Sub(Loc("ParagraphFormat"), children.ToArray());
    }

    // ── 목록 (list) — promoted to top level; bullet/number/quote states are checked to reflect the caret.
    private MenuItem ListSub(CaretFormat fmt) => Sub(Loc("List"),
        CheckItem(Loc("BulletList"), fmt.List == ListKind.Bullet, ToggleBullet, gesture: RichEditorShortcuts.Gesture(ShortcutId.BulletList)),
        Sub(Loc("BulletStyle"),
            Mi("•", () => SetListStyle(ListMarkerStyle.Disc)),
            Mi("◦", () => SetListStyle(ListMarkerStyle.Circle)),
            Mi("▪", () => SetListStyle(ListMarkerStyle.Square)),
            Mi("–", () => SetListStyle(ListMarkerStyle.Dash))),
        CheckItem(Loc("NumberedList"), fmt.List == ListKind.Ordered, ToggleNumbering),
        Sub(Loc("NumberStyle"),
            Mi("1.", () => SetListStyle(ListMarkerStyle.Decimal)),
            Mi("1)", () => SetListStyle(ListMarkerStyle.DecimalParen)),
            Mi("a)", () => SetListStyle(ListMarkerStyle.LowerAlpha)),
            Mi("A)", () => SetListStyle(ListMarkerStyle.UpperAlpha)),
            Mi("i)", () => SetListStyle(ListMarkerStyle.LowerRoman))),
        new Separator(),
        CheckItem(Loc("Quote"), fmt.Quote, ToggleQuote));

    // ── 제목 (heading / style) — promoted to top level, radio-checked at the current level.
    private MenuItem HeadingSub(CaretFormat fmt) => Sub(Loc("Heading"),
        HeadingRadio(Loc("Heading1"), 1, fmt.Heading), HeadingRadio(Loc("Heading2"), 2, fmt.Heading),
        HeadingRadio(Loc("Heading3"), 3, fmt.Heading), HeadingRadio(Loc("Heading4"), 4, fmt.Heading),
        HeadingRadio(Loc("Heading5"), 5, fmt.Heading), HeadingRadio(Loc("Heading6"), 6, fmt.Heading),
        new Separator(),
        HeadingRadio(Loc("BodyText"), 0, fmt.Heading));

    // A heading level as a radio item within the 제목 group (current level checked); carries its Ctrl+Alt+n hint.
    private MenuItem HeadingRadio(string header, int level, int current)
    {
        // Heading1..6 are consecutive enum values; level 0 = body text.
        var sc = level == 0 ? ShortcutId.BodyText : (ShortcutId)((int)ShortcutId.Heading1 + level - 1);
        return RadioItem(header, "ctxHeading", current == level, () => SetHeading(level), RichEditorShortcuts.Gesture(sc));
    }

    /// <summary>Inserts a horizontal rule (<see cref="DividerBlock"/>) at the caret position.</summary>
    public void InsertDivider()
    {
        if (Document == null) return;
        PushUndo();
        InsertBlockAtCaret(new DividerBlock());
        InvalidateVisual();
    }

    // The caret-position text menu. `cellTable` non-null means the caret is inside that table's cell:
    // the menu is identical to a top-level paragraph's, except the table-insert picker is dropped (no
    // nested tables yet) and the table-structure operations are appended as a "Table" submenu.
    private void BuildTextMenu(List<Control> items, bool hasSelection, Run? link, TableBlock? cellTable = null)
    {
        var fmt = GetCaretFormat(); // current caret state → radio/check reflection
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());

        if (ShowFormattingMenu)
        {
            // HWP-style: 글자 모양 / 문단 모양 / 목록 / 제목 grouping submenus (flattened — alignment and
            // margin under 문단 모양, list/heading promoted to top level). Full formatting opt-in.
            items.Add(CharacterFormatSub(fmt, hasSelection));
            items.Add(BuildParagraphFormatSub(fmt));
            items.Add(ListSub(fmt));
            items.Add(HeadingSub(fmt));
        }
        else
        {
            // Slim (default): just the quick character toggles, checked to reflect the caret. The rich
            // formatting groups live on the toolbar; opt in with ShowFormattingMenu for a toolbar-less host.
            items.Add(CheckItem(Loc("Bold"), fmt.Bold, ToggleBold, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Bold)));
            items.Add(CheckItem(Loc("Italic"), fmt.Italic, ToggleItalic, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Italic)));
            items.Add(CheckItem(Loc("Underline"), fmt.Underline, ToggleUnderline, hasSelection, RichEditorShortcuts.Gesture(ShortcutId.Underline)));
        }

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
        items.Add(Mi(Loc("SelectAll"), SelectAll, icon: RichEditorIcon.SelectAll, gesture: RichEditorShortcuts.Gesture(ShortcutId.SelectAll)));
        items.Add(Mi(Loc("Undo"), DoUndo, CanUndo, RichEditorIcon.Undo, RichEditorShortcuts.Gesture(ShortcutId.Undo)));
        items.Add(Mi(Loc("Redo"), DoRedo, CanRedo, RichEditorIcon.Redo, RichEditorShortcuts.Gesture(ShortcutId.Redo)));
        // Block-insert items appear only when the corresponding feature flag is enabled (N3.5). Inside a
        // cell, image/divider/table all insert into the cell (P4-2a/P4-2b nested tables).
        if (AllowTables || AllowImages) items.Add(new Separator());
        if (AllowTables) items.Add(BuildInsertTableMenu());
        if (AllowImages) items.Add(Mi(Loc("InsertImage"), () => { _ = InsertImageFromFileAsync(); }, icon: RichEditorIcon.InsertImage));
        if (AllowTables || AllowImages) items.Add(Mi(Loc("InsertDivider"), InsertDivider, icon: RichEditorIcon.InsertDivider));
        // Inside a cell: row/column/merge/delete-table operations live in a "Table" submenu.
        if (cellTable != null)
        {
            var tableItems = new List<Control>();
            AddTableStructureItems(tableItems, cellTable, _caretPosition.Paragraph, hasSelection);
            items.Add(new Separator());
            items.Add(Sub(Loc("TableOps"), tableItems.ToArray()));
        }
    }

    private void BuildImageMenu(List<Control> items, ImageBlock img)
    {
        // Edit
        items.Add(Mi(Loc("Copy"), () => { _ = CopyImageToClipboardAsync(img.RawBytes, img.Image, inline: false, img.Width, img.Height); }, img.RawBytes != null || img.Image != null, RichEditorIcon.Copy, RichEditorShortcuts.Gesture(ShortcutId.Copy)));
        // 개체 모양 (object shape): size preset, 글자처럼 취급, margin.
        items.Add(new Separator());
        // Size presets in a submenu. "Original" resets to natural size; the fractions scale the
        // current display size (so they compound). Width/Height only — encoded bytes untouched.
        items.Add(Sub(Loc("ImageSize"),
            Mi(Loc("OriginalSize"), () => ResetImageSize(img), img.Image != null),
            Mi(Loc("HalfSize"), () => ScaleImageSize(img, 1.0 / 2), img.Image != null),
            Mi(Loc("ThirdSize"), () => ScaleImageSize(img, 1.0 / 3), img.Image != null),
            Mi(Loc("QuarterSize"), () => ScaleImageSize(img, 1.0 / 4), img.Image != null)));
        // HWP-style toggle: unchecked here (block image); checking it demotes to an inline character.
        // Disabled for a cell image: block<->inline conversion anchors to top-level paragraphs, which a
        // cell image doesn't have (mirrors the inline-image menu's guard inside cells).
        items.Add(CheckItem(Loc("InlineWithText"), false, () => ConvertImageBlockToInline(img), img.Parent is FlowDocument));
        items.Add(MarginMenu(img));
        // File ops: replace / save.
        items.Add(new Separator());
        items.Add(Mi(Loc("ReplaceImage"), () => { _ = ReplaceImageAsync(img); }, icon: RichEditorIcon.ReplaceImage));
        items.Add(Mi(Loc("SaveImageAs"), () => { _ = SaveImageAsync(img); }, img.Image != null, RichEditorIcon.SaveImageAs));
        // Delete
        items.Add(new Separator());
        items.Add(Mi(Loc("Delete"), () => DeleteBlock(img), icon: RichEditorIcon.Delete));
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
        // Edit
        items.Add(Mi(Loc("Copy"), () => { _ = CopyImageToClipboardAsync(img.RawBytes, img.Image, inline: true, img.Width, img.Height); }, img.RawBytes != null || img.Image != null, RichEditorIcon.Copy, RichEditorShortcuts.Gesture(ShortcutId.Copy)));
        // 개체 모양 (object shape): size preset + 글자처럼 취급.
        items.Add(new Separator());
        items.Add(Sub(Loc("ImageSize"),
            Mi(Loc("OriginalSize"), () => ResetInlineImageSize(img), img.Image != null),
            Mi(Loc("HalfSize"), () => ScaleInlineImageSize(img, 1.0 / 2), img.Image != null),
            Mi(Loc("ThirdSize"), () => ScaleInlineImageSize(img, 1.0 / 3), img.Image != null),
            Mi(Loc("QuarterSize"), () => ScaleInlineImageSize(img, 1.0 / 4), img.Image != null)));
        // Checked here (inline = treated as a character). Unchecking promotes back to a block image;
        // disabled inside table cells, which cannot host block siblings.
        bool canBlock = Document != null && Document.Blocks.IndexOf(p) >= 0;
        items.Add(CheckItem(Loc("InlineWithText"), true, () => ConvertInlineImageToBlock(p, img), canBlock));
        // File ops: replace / save.
        items.Add(new Separator());
        items.Add(Mi(Loc("ReplaceImage"), () => { _ = ReplaceInlineImageAsync(img); }, icon: RichEditorIcon.ReplaceImage));
        items.Add(Mi(Loc("SaveImageAs"), () => { _ = SaveBitmapAsync(img.Image); }, img.Image != null, RichEditorIcon.SaveImageAs));
        // Delete
        items.Add(new Separator());
        items.Add(Mi(Loc("Delete"), () => DeleteInlineImage(p, img), icon: RichEditorIcon.Delete));
    }

    private void BuildTableMenu(List<Control> items, TableBlock tb, Paragraph? cell, bool hasSelection)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        AddTableStructureItems(items, tb, cell, hasSelection);
    }

    // The table-structure operations (row/column insert-delete, cell merge, margin, delete table).
    // Used both as the body of the table-selection menu and as a "Table" submenu inside the cell-text
    // menu (right-clicking while editing inside a cell).
    private void AddTableStructureItems(List<Control> items, TableBlock tb, Paragraph? cell, bool hasSelection)
    {
        var loc = cell != null ? FindCell(cell) : null;
        int r = loc?.r ?? -1;
        int c = loc?.c ?? -1;
        // Explicit way into cell-selection mode. Dragging across cells is the other one, but that can
        // never produce a ONE-cell block, so without this a single cell couldn't be selected as a unit.
        items.Add(Mi(Loc("SelectCell"), () =>
        {
            if (loc is not { } lc) return;
            var (ar, ac) = lc.tb.AnchorOf(lc.r, lc.c);
            SelectCellAsBlock(lc.tb, lc.tb.Cells[ar][ac]);
        }, loc != null));
        items.Add(new Separator());
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
            FocusCell(tb.Cells[g.r0][g.c0].Para);
            InvalidateVisual();
        }, canMerge, RichEditorIcon.MergeCells));
        bool canUnmerge = r >= 0 && c >= 0 && (tb.SpanOf(r, c).cs > 1 || tb.SpanOf(r, c).rs > 1);
        items.Add(Mi(Loc("UnmergeCells"), () =>
        {
            if (Document != null) PushUndo();
            tb.UnmergeCell(r, c);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[r][c].Para);
            InvalidateVisual();
        }, canUnmerge, RichEditorIcon.UnmergeCells));
        items.Add(new Separator());
        items.Add(MarginMenu(tb));
        // HWP-style "treat as character": a top-level block table can become inline; an inline table can
        // promote back to a block (only when its host paragraph is top-level).
        if (tb.Parent is FlowDocument)
        {
            var asChar = new MenuItem { Header = Loc("InlineWithText"), ToggleType = MenuItemToggleType.CheckBox, IsChecked = false };
            asChar.Click += (_, _) => ConvertTableBlockToInline(tb);
            items.Add(asChar);
        }
        else if (tb.Parent is InlineTable itw && itw.Parent is Paragraph itHost)
        {
            var asChar = new MenuItem
            {
                Header = Loc("InlineWithText"),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = true,
                IsEnabled = Document != null && Document.Blocks.IndexOf(itHost) >= 0,
            };
            asChar.Click += (_, _) => ConvertInlineTableToBlock(itHost, itw);
            items.Add(asChar);
        }
        items.Add(Mi(Loc("DeleteTable"), () =>
        {
            if (tb.Parent is InlineTable it2 && it2.Parent is Paragraph host2) DeleteInlineTable(host2, it2);
            else DeleteBlock(tb);
        }, icon: RichEditorIcon.DeleteTable));
    }
}
