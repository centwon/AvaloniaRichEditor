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

    private static MenuItem Mi(string header, Action action, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
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
            var roItems = new List<Control> { Mi("복사", CopySelectionToClipboard, hasSelection), Mi("모두 선택", SelectAll) };
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
                BuildInlineImageMenu(items, tp.Paragraph, inlineImg);
            }
            else
            {
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
        items.Add(Mi("잘라내기", () =>
        {
            if (Document != null) PushUndo();
            CopySelectionToClipboard();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection));
        items.Add(Mi("복사", CopySelectionToClipboard, hasSelection));
        items.Add(Mi("붙여넣기", () => { _ = PasteFromClipboardAsync(); }));
        items.Add(Mi("삭제", () =>
        {
            if (Document != null) PushUndo();
            DeleteSelection();
            InvalidateVisual();
        }, hasSelection));
    }

    private void AddFormatItems(List<Control> items, bool hasSelection)
    {
        // Font submenu is built from the (host-overridable) FontFamilyChoices, so no locale is assumed.
        var fontItems = FontFamilyChoices
            .Select(f => (Control)Mi(f, () => SetFontFamily(f), hasSelection))
            .ToArray();
        // Character-level formatting, grouped under one submenu so the top level stays short.
        items.Add(Sub("글자 모양",
            Mi("굵게", ToggleBold, hasSelection),
            Mi("기울임", ToggleItalic, hasSelection),
            Mi("밑줄", ToggleUnderline, hasSelection),
            Mi("취소선", ToggleStrikethrough, hasSelection),
            new Separator(),
            Sub("글자 크기",
                Mi("10", () => SetFontSize(10)), Mi("12", () => SetFontSize(12)), Mi("14", () => SetFontSize(14)),
                Mi("18", () => SetFontSize(18)), Mi("24", () => SetFontSize(24)), Mi("36", () => SetFontSize(36))),
            Sub("글자 색",
                Mi("검정", () => SetForeground(Brushes.Black)), Mi("빨강", () => SetForeground(Brushes.Red)),
                Mi("파랑", () => SetForeground(Brushes.Blue)), Mi("초록", () => SetForeground(Brushes.Green)),
                Mi("회색", () => SetForeground(Brushes.Gray))),
            Sub("형광펜",
                Mi("노랑", () => SetHighlight(Brushes.Yellow), hasSelection),
                Mi("연두", () => SetHighlight(Brushes.LightGreen), hasSelection),
                Mi("분홍", () => SetHighlight(Brushes.Pink), hasSelection),
                Mi("하늘", () => SetHighlight(Brushes.LightBlue), hasSelection),
                Mi("없음", () => SetHighlight(null), hasSelection)),
            Sub("글꼴", fontItems),
            new Separator(),
            Mi("서식 지우기", ClearFormatting, hasSelection)));
        // Paragraph-level formatting, also grouped.
        items.Add(Sub("문단",
            Sub("정렬",
                Mi("왼쪽", () => SetTextAlignment(TextAlignment.Left)),
                Mi("가운데", () => SetTextAlignment(TextAlignment.Center)),
                Mi("오른쪽", () => SetTextAlignment(TextAlignment.Right))),
            Sub("목록",
                Mi("글머리표", ToggleBullet),
                Mi("번호 매기기", ToggleNumbering)),
            Sub("제목",
                Mi("제목 1", () => SetHeading(1)),
                Mi("제목 2", () => SetHeading(2)),
                Mi("제목 3", () => SetHeading(3)),
                Mi("본문", () => SetHeading(0))),
            Sub("들여쓰기",
                Mi("들여쓰기 +", () => Indent(20)),
                Mi("내어쓰기 -", () => Indent(-20)))));
    }

    // Menu shown when text is selected inside a table cell: clipboard + grouped formatting only — no
    // table-structure or block-insert items.
    private void BuildCellTextMenu(List<Control> items, bool hasSelection)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        AddFormatItems(items, hasSelection);
    }

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
            items.Add(Mi("링크 열기", () => OpenUrl(link.NavigateUri!)));
            items.Add(Mi("링크 편집...", () => { _ = EditHyperlinkAsync(link.NavigateUri, link); }));
            items.Add(Mi("링크 제거", () => SetHyperlink(null, link)));
        }
        else
        {
            items.Add(Mi("링크 삽입...", () => { _ = EditHyperlinkAsync(null, null); }, hasSelection));
        }
        items.Add(new Separator());
        items.Add(Mi("모두 선택", SelectAll));
        items.Add(Mi("실행 취소", DoUndo));
        items.Add(Mi("다시 실행", DoRedo));
        // Block-insert items appear only when the corresponding feature flag is enabled (N3.5).
        if (AllowTables || AllowImages) items.Add(new Separator());
        if (AllowTables) items.Add(Mi("표 삽입 (2x2)", () => InsertTable(2, 2)));
        if (AllowImages) items.Add(Mi("이미지 삽입...", () => { _ = InsertImageFromFileAsync(); }));
        if (AllowTables || AllowImages) items.Add(Mi("구분선 삽입", InsertDivider));
    }

    private void BuildImageMenu(List<Control> items, ImageBlock img)
    {
        items.Add(Mi("삭제", () => DeleteBlock(img)));
        items.Add(new Separator());
        items.Add(Mi("원본 크기로", () => ResetImageSize(img), img.Image != null));
        items.Add(Mi("이미지 교체...", () => { _ = ReplaceImageAsync(img); }));
        items.Add(Mi("다른 이름으로 저장...", () => { _ = SaveImageAsync(img); }, img.Image != null));
    }

    // Concise menu shown when right-clicking a hyperlink: link actions + copy, no formatting clutter.
    private void BuildLinkMenu(List<Control> items, bool hasSelection, Run link)
    {
        items.Add(Mi("링크 열기", () => OpenUrl(link.NavigateUri!)));
        items.Add(Mi("링크 편집...", () => { _ = EditHyperlinkAsync(link.NavigateUri, link); }));
        items.Add(Mi("링크 제거", () => SetHyperlink(null, link)));
        items.Add(Mi("링크 복사", () =>
        {
            if (link.NavigateUri != null) TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(link.NavigateUri);
        }, !string.IsNullOrEmpty(link.NavigateUri)));
        items.Add(new Separator());
        items.Add(Mi("복사", CopySelectionToClipboard, hasSelection));
    }

    // Menu for an inline image (small in-paragraph icon): mirrors the block-image menu but operates on
    // the InlineImage in place.
    private void BuildInlineImageMenu(List<Control> items, Paragraph p, InlineImage img)
    {
        items.Add(Mi("삭제", () => DeleteInlineImage(p, img)));
        items.Add(new Separator());
        items.Add(Mi("원본 크기로", () => ResetInlineImageSize(img), img.Image != null));
        items.Add(Mi("이미지 교체...", () => { _ = ReplaceInlineImageAsync(img); }));
        items.Add(Mi("다른 이름으로 저장...", () => { _ = SaveBitmapAsync(img.Image); }, img.Image != null));
    }

    private void BuildTableMenu(List<Control> items, TableBlock tb, Paragraph? cell, bool hasSelection)
    {
        AddClipboardItems(items, hasSelection);
        items.Add(new Separator());
        var loc = cell != null ? FindCell(cell) : null;
        int r = loc?.r ?? -1;
        int c = loc?.c ?? -1;
        items.Add(Mi("위에 행 삽입", () => TableInsertRow(tb, r), r >= 0));
        items.Add(Mi("아래에 행 삽입", () => TableInsertRow(tb, r + 1), r >= 0));
        items.Add(Mi("행 삭제", () => TableDeleteRow(tb, r), r >= 0 && tb.Rows > 1));
        items.Add(new Separator());
        items.Add(Mi("왼쪽에 열 삽입", () => TableInsertColumn(tb, c), c >= 0));
        items.Add(Mi("오른쪽에 열 삽입", () => TableInsertColumn(tb, c + 1), c >= 0));
        items.Add(Mi("열 삭제", () => TableDeleteColumn(tb, c), c >= 0 && tb.Columns > 1));
        items.Add(new Separator());
        var range = SelectedCellRange(tb);
        bool canMerge = range is { } rg && IsCleanRect(tb, rg.r0, rg.c0, rg.r1, rg.c1);
        items.Add(Mi("셀 병합", () =>
        {
            if (range is not { } g) return;
            if (Document != null) PushUndo();
            tb.MergeCells(g.r0, g.c0, g.r1, g.c1);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[g.r0][g.c0]);
            InvalidateVisual();
        }, canMerge));
        bool canUnmerge = r >= 0 && c >= 0 && (tb.SpanOf(r, c).cs > 1 || tb.SpanOf(r, c).rs > 1);
        items.Add(Mi("셀 병합 해제", () =>
        {
            if (Document != null) PushUndo();
            tb.UnmergeCell(r, c);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[r][c]);
            InvalidateVisual();
        }, canUnmerge));
        items.Add(new Separator());
        items.Add(Mi("표 삭제", () => DeleteBlock(tb)));
    }
}
