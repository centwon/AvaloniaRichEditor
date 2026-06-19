using System;
using System.Linq;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Table interaction: cell lookup, Tab navigation, cell-range selection (merge geometry) and the
// row/column structure commands. Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    private (TableBlock tb, int r, int c)? FindCell(Paragraph p)
        => Document == null ? null : FindCellIn(Document.Blocks, p);

    // Finds the innermost table + cell directly holding paragraph p, recursing into nested tables
    // (P4-2b). Returns the deepest enclosing cell so navigation/menus act on the table the caret is in.
    private static (TableBlock tb, int r, int c)? FindCellIn(System.Collections.Generic.IEnumerable<Block> blocks, Paragraph p)
    {
        foreach (var b in blocks)
            if (b is TableBlock tb)
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                    {
                        var cell = tb.Cells[r][c];
                        var nested = FindCellIn(cell.Blocks, p);
                        if (nested != null) return nested;
                        if (cell.Blocks.Contains(p)) return (tb, r, c);
                    }
        return null;
    }

    // Rectangular cell block (inclusive, span-aware) defined by the two selection *endpoints* — the
    // cell the drag started in and the cell it ended in. Using the endpoints (not every cell the linear
    // text selection passes through) makes a vertical drag select a vertical block, so up/down cells can
    // be merged. Returns null unless both endpoints are cells of `tb` and they differ.
    private (int r0, int c0, int r1, int c1)? SelectedCellRange(TableBlock tb)
    {
        if (_selectionStart.Paragraph == null || _selectionEnd.Paragraph == null) return null;
        if (FindCell(_selectionStart.Paragraph) is not { } s || s.tb != tb) return null;
        if (FindCell(_selectionEnd.Paragraph) is not { } e || e.tb != tb) return null;
        // Both endpoints in the same cell = a caret/text selection inside one cell, not a cell block.
        // (Must compare the cells directly: a merged cell spans rows/cols, so a span-expanded bounding
        // box would otherwise look multi-cell even for a single merged cell.)
        if (s.r == e.r && s.c == e.c) return null;
        var (scs, srs) = tb.SpanOf(s.r, s.c);
        var (ecs, ers) = tb.SpanOf(e.r, e.c);
        int r0 = Math.Min(s.r, e.r), c0 = Math.Min(s.c, e.c);
        int r1 = Math.Max(s.r + srs - 1, e.r + ers - 1), c1 = Math.Max(s.c + scs - 1, e.c + ecs - 1);
        return (r0, c0, r1, c1);
    }

    // True when the box is a mergeable rectangle: spans more than one cell and no anchor inside it
    // reaches outside the box (no partial overlap with an existing merge).
    private static bool IsCleanRect(TableBlock tb, int r0, int c0, int r1, int c1)
    {
        if (r0 < 0 || c0 < 0 || r1 >= tb.Rows || c1 >= tb.Columns) return false;
        if (r0 == r1 && c0 == c1) return false;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                var (ar, ac) = tb.AnchorOf(r, c);
                if (ar < r0 || ac < c0) return false;
                var (cs, rs) = tb.SpanOf(ar, ac);
                if (ar + rs - 1 > r1 || ac + cs - 1 > c1) return false;
            }
        return true;
    }

    // Tab moves to the next table cell (Shift+Tab to the previous); Tab in the last cell appends a
    // new row. Outside a table it inserts spaces so focus doesn't leave the editor.
    private void HandleTab(bool shift)
    {
        var loc = _caretPosition.Paragraph != null ? FindCell(_caretPosition.Paragraph) : null;
        if (loc == null)
        {
            if (Document != null) PushUndo();
            InsertText("    ");
            return;
        }

        var (tb, r, c) = loc.Value;
        // Navigate over logical (anchor) cells so merged areas count once and covered cells are skipped.
        var anchors = tb.LogicalCells().Select(x => x.cell.Para).ToList();
        int idx = anchors.IndexOf(_caretPosition.Paragraph!);
        if (idx < 0) { var (ar, ac) = tb.AnchorOf(r, c); idx = anchors.IndexOf(tb.Cells[ar][ac].Para); }
        if (shift)
        {
            if (idx > 0) FocusCell(anchors[idx - 1]);
        }
        else if (idx >= 0 && idx + 1 < anchors.Count)
        {
            FocusCell(anchors[idx + 1]);
        }
        else
        {
            if (Document != null) PushUndo();
            tb.InsertRow(tb.Rows);
            if (Document != null) UpdateParents(Document);
            FocusCell(tb.Cells[tb.Rows - 1][0].Para);
        }
    }

    private void FocusCell(Paragraph cell)
    {
        // If handed a covered cell, redirect the caret to its merge anchor.
        if (FindCell(cell) is { } loc && loc.tb.IsCovered(loc.r, loc.c))
        {
            var (ar, ac) = loc.tb.AnchorOf(loc.r, loc.c);
            cell = loc.tb.Cells[ar][ac].Para;
        }
        int len = GetParagraphLength(cell);
        _caretPosition = new TextPointer(cell, len);
        _selectionStart = new TextPointer(cell, 0);
        _selectionEnd = new TextPointer(cell, len);
        ResetCaretBlink();
        InvalidateVisual();
    }

    private void TableInsertRow(TableBlock tb, int at)
    {
        if (Document == null || at < 0) return;
        PushUndo();
        tb.InsertRow(at);
        UpdateParents(Document);
        int ar = Math.Clamp(at, 0, tb.Rows - 1);
        _caretPosition = new TextPointer(tb.Cells[ar][0].Para, 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void TableDeleteRow(TableBlock tb, int at)
    {
        if (Document == null || tb.Rows <= 1 || at < 0) return;
        PushUndo();
        tb.DeleteRow(at);
        UpdateParents(Document);
        int nr = Math.Clamp(at, 0, tb.Rows - 1);
        _caretPosition = new TextPointer(tb.Cells[nr][0].Para, 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void TableInsertColumn(TableBlock tb, int at)
    {
        if (Document == null || at < 0) return;
        PushUndo();
        tb.InsertColumn(at);
        UpdateParents(Document);
        int ac = Math.Clamp(at, 0, tb.Columns - 1);
        _caretPosition = new TextPointer(tb.Cells[0][ac].Para, 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }

    private void TableDeleteColumn(TableBlock tb, int at)
    {
        if (Document == null || tb.Columns <= 1 || at < 0) return;
        PushUndo();
        tb.DeleteColumn(at);
        UpdateParents(Document);
        int nc = Math.Clamp(at, 0, tb.Columns - 1);
        _caretPosition = new TextPointer(tb.Cells[0][nc].Para, 0);
        CollapseSelectionToCaret();
        InvalidateVisual();
    }
}
