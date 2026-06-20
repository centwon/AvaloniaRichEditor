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

    // The content width of the cell that encloses a nested table `t`, or null if `t` is top-level.
    // Used to clamp a nested table's width on resize so it stays within its cell.
    private static double? EnclosingCellInnerWidth(TableBlock t)
    {
        if (t.Parent is not TableCell cell || cell.Parent is not TableBlock parent) return null;
        for (int r = 0; r < parent.Rows; r++)
            for (int c = 0; c < parent.Columns; c++)
                if (ReferenceEquals(parent.Cells[r][c], cell))
                {
                    var (cs, _) = parent.SpanOf(r, c);
                    double w = 0;
                    for (int k = c; k < c + cs && k < parent.ColumnWidths.Count; k++) w += parent.ColumnWidths[k];
                    return System.Math.Max(10, w - 10);
                }
        return null;
    }

    // Finds the innermost table + cell directly holding paragraph p, recursing into nested tables
    // (P4-2b). Returns the deepest enclosing cell so navigation/menus act on the table the caret is in.
    private static (TableBlock tb, int r, int c)? FindCellIn(System.Collections.Generic.IEnumerable<Block> blocks, Paragraph p)
    {
        foreach (var b in blocks)
        {
            if (b is TableBlock tb)
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                    {
                        var cell = tb.Cells[r][c];
                        var nested = FindCellIn(cell.Blocks, p);
                        if (nested != null) return nested;
                        if (cell.Blocks.Contains(p)) return (tb, r, c);
                    }
            // Inline tables live in a paragraph's inlines (milestone B); descend into them too so Tab/
            // merge/menus find a cell whose caret sits inside an inline table.
            else if (b is Paragraph para)
                foreach (var inl in para.Inlines)
                    if (inl is InlineTable it && FindCellIn(new[] { it.Table }, p) is { } hit)
                        return hit;
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
        var (ar, ac) = tb.AnchorOf(r, c);
        var current = tb.Cells[ar][ac];
        // Document-order anchor cells across ALL tables, descending into nested tables (P4-2b) so Tab/
        // Shift+Tab traverse the whole structure — entering a nested table and stepping back out at its
        // edges. Covered (merged) cells are excluded (LogicalCells yields anchors only).
        var all = AllCellsInOrder();
        int idx = all.IndexOf(current);
        if (idx < 0) return;

        if (shift)
        {
            if (idx > 0) FocusCell(all[idx - 1].Para); // else: first cell of the document -> no-op
        }
        else if (idx + 1 < all.Count)
        {
            FocusCell(all[idx + 1].Para);
        }
        else
        {
            // Past the document's last cell: add a row to the TOP-LEVEL table (nested tables don't grow
            // via Tab — use the right-click menu), walking up the parent chain if the last cell is nested.
            var top = tb;
            while (top.Parent is TableCell pcell && pcell.Parent is TableBlock gp) top = gp;
            if (Document != null) PushUndo();
            top.InsertRow(top.Rows);
            if (Document != null) UpdateParents(Document);
            FocusCell(top.Cells[top.Rows - 1][0].Para);
        }
    }

    // All anchor cells in document order, descending into nested tables: each cell is followed by the
    // cells of any tables nested inside it, so Tab traversal enters a nested table right after its host
    // cell and resumes at the host's sibling once the nested cells are exhausted.
    private System.Collections.Generic.List<TableCell> AllCellsInOrder()
    {
        var result = new System.Collections.Generic.List<TableCell>();
        if (Document != null) CollectCells(Document.Blocks, result);
        return result;
    }

    private static void CollectCells(System.Collections.Generic.IEnumerable<Block> blocks, System.Collections.Generic.List<TableCell> outList)
    {
        foreach (var b in blocks)
        {
            if (b is TableBlock tb)
                foreach (var (_, _, cell) in tb.LogicalCells())
                {
                    outList.Add(cell);
                    CollectCells(cell.Blocks, outList);
                }
            // Inline-table cells join the Tab order right after their host paragraph (milestone B).
            else if (b is Paragraph para)
                foreach (var inl in para.Inlines)
                    if (inl is InlineTable it)
                        foreach (var (_, _, cell) in it.Table.LogicalCells())
                        {
                            outList.Add(cell);
                            CollectCells(cell.Blocks, outList);
                        }
        }
    }

    // ---- Milestone B P4: insert / treat-as-character ----------------------

    /// <summary>Inserts a <paramref name="rows"/>×<paramref name="cols"/> table inline at the caret,
    /// treated as a single character (HWP-style "treat as character"). The caret lands just after it.
    /// For a block-level grid use <see cref="InsertTable"/> instead.</summary>
    public void InsertInlineTable(int rows, int cols)
    {
        if (Document == null || IsReadOnly || !AllowTables) return;
        if (_caretPosition.Paragraph is not { } p) return;
        if (rows < 1) rows = 1;
        if (cols < 1) cols = 1;
        PushUndo();
        var it = new InlineTable { Table = new TableBlock(rows, cols) };
        int at = SplitInlinesAt(p, _caretPosition.Offset);
        p.Inlines.Insert(at, it);
        UpdateParents(Document);
        _caretPosition = new TextPointer(p, _caretPosition.Offset + 1); // after the table's ObjChar
        CollapseSelectionToCaret();
        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();
    }

    // HWP-style toggle: demote a top-level block table to an inline table anchored on an adjacent
    // paragraph (mirror of ConvertImageBlockToInline). Top-level only — a table inside a cell is already
    // a block sibling there, so the menu offers this only for FlowDocument-rooted tables.
    internal void ConvertTableBlockToInline(TableBlock tb)
    {
        if (Document == null) return;
        int idx = Document.Blocks.IndexOf(tb);
        if (idx < 0) return;
        Paragraph? anchor = null;
        bool atEnd = true;
        if (idx > 0 && Document.Blocks[idx - 1] is Paragraph prev) anchor = prev;
        else
            for (int i = idx + 1; i < Document.Blocks.Count && anchor == null; i++)
                if (Document.Blocks[i] is Paragraph next) { anchor = next; atEnd = false; }
        if (anchor == null) return;

        PushUndo();
        var it = new InlineTable { Table = (TableBlock)tb.Clone() };
        Document.Blocks.Remove(tb);
        if (atEnd) anchor.Inlines.Add(it);
        else anchor.Inlines.Insert(0, it);
        if (ReferenceEquals(_selectedBlock, tb)) _selectedBlock = null;
        UpdateParents(Document);

        int off = 0;
        foreach (var inl in anchor.Inlines) { off += InlineLen(inl); if (ReferenceEquals(inl, it)) break; }
        _caretPosition = new TextPointer(anchor, off);
        CollapseSelectionToCaret();
        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Reverse of ConvertTableBlockToInline: promote an inline table to a sibling block table after its
    // host paragraph. Top-level paragraphs only — table cells cannot host block siblings (the menu
    // disables this inside cells, mirroring the inline-image guard).
    internal void ConvertInlineTableToBlock(Paragraph host, InlineTable it)
    {
        if (Document == null) return;
        int idx = Document.Blocks.IndexOf(host);
        if (idx < 0) return;

        PushUndo();
        var tb = (TableBlock)it.Table.Clone();
        host.Inlines.Remove(it);
        Document.Blocks.Insert(idx + 1, tb);
        UpdateParents(Document);
        _selectedBlock = tb;
        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();
    }

    // Removes an inline table from its host paragraph (the menu's "Delete table" for an inline table —
    // RemoveBlockAnywhere only walks block lists, where an inline table never lives). The caret lands
    // where the table was.
    internal void DeleteInlineTable(Paragraph host, InlineTable it)
    {
        if (Document == null) return;
        int off = OffsetOfInline(host, it);
        PushUndo();
        host.Inlines.Remove(it);
        if (ReferenceEquals(_selectedBlock, it.Table)) _selectedBlock = null;
        UpdateParents(Document);
        _caretPosition = new TextPointer(host, off);
        CollapseSelectionToCaret();
        InvalidateMeasure();
        ResetCaretBlink();
        InvalidateVisual();
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
