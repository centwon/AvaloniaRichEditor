using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Table interaction: cell lookup, Tab navigation, cell-range selection (merge geometry) and the
// row/column structure commands. Part of RichEditor (split out of the main file for readability).
public partial class RichEditor
{
    // The innermost table + cell holding paragraph p, resolved through the parent chain
    // (Paragraph -> TableCell -> TableBlock, wired by UpdateParents at any nesting depth, inline
    // tables included). Was a full-document scan that recursed into every table and every inline
    // table on each call — and this runs per keystroke, per pointer move and per menu build.
    private static (TableBlock tb, int r, int c)? FindCell(Paragraph p)
    {
        if (p.Parent is not TableCell cell || cell.Parent is not TableBlock tb) return null;
        for (int r = 0; r < tb.Rows; r++)
            for (int c = 0; c < tb.Columns; c++)
                if (ReferenceEquals(tb.Cells[r][c], cell)) return (tb, r, c);
        return null;
    }

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

    // Drops the cached geometry that a change to `t`'s size invalidates: the table itself, every table
    // enclosing it, and the host paragraph of any inline table along the way.
    //
    // The host paragraph matters because an inline table is laid out INSIDE that paragraph's line box.
    // Both a resize drag and an IME composition mutate size without going through an edit, so the frame
    // runs as a "trusted" pass — which returns the cached layout without re-checking its signature. The
    // table cache alone was evicted, so a resized inline table kept its old line box and the paragraph
    // only reflowed on the next real edit (measured: 80 -> 80 on resize, 80 -> 206 once the host
    // paragraph is evicted too).
    private void InvalidateTableChain(TableBlock t)
    {
        for (var cur = t; cur != null; cur = EnclosingTableOf(cur))
        {
            _tableLayoutCache.Remove(cur);
            if (cur.Parent is InlineTable it && it.Parent is Paragraph host) _layoutCache.Remove(host);
        }
    }

    // ---- staged Ctrl+A (HWP/Excel) ----------------------------------------

    // One stage of Ctrl+A while the caret is inside a table: the cell's contents -> the whole table
    // -> the enclosing table (one level per press). Returns false when there's no table stage left,
    // and the caller selects the whole document. The stage is read back off the current selection
    // rather than counted, so a click or an arrow key in between resets the sequence by itself.
    private bool TrySelectAllStage()
    {
        var p = _caretPosition.Paragraph;
        if (p == null) return false;

        // A whole table is already selected -> climb to the table that contains it, if any; otherwise
        // let the caller take the last step and select the document.
        if (_cellSelTable is { } cur && WholeTableSelected(cur))
            return EnclosingTableOf(cur) is { } outer && SelectWholeTable(outer);

        if (FindCell(p) is not { } loc) return false;
        var cell = loc.tb.Cells[loc.r][loc.c];
        // Cell contents already fully selected -> the whole table. A single-cell table has no distinct
        // table stage, so climb straight to the table around it (or fall through to the document).
        if (CellEnds(cell) is { } ends && SelectionSpans(ends.first, ends.last))
            return SelectWholeTable(loc.tb)
                || (EnclosingTableOf(loc.tb) is { } up && SelectWholeTable(up));
        return SelectCellContents(cell);
    }

    // The table one level further out: a table nested in a cell, or — for an inline table — the table
    // holding the cell its host paragraph lives in. Null when the table is already top-level, so the
    // climb ends and the next press selects the document.
    private static TableBlock? EnclosingTableOf(TableBlock t)
    {
        if (t.Parent is TableCell c && c.Parent is TableBlock outer) return outer;
        if (t.Parent is InlineTable it && it.Parent is Paragraph host
            && host.Parent is TableCell hc && hc.Parent is TableBlock ht) return ht;
        return null;
    }

    // First and last paragraph of a cell's contents in document order, descending into anything
    // nested inside it (a nested or inline table's own cells count as cell content).
    private static (Paragraph first, Paragraph last)? CellEnds(TableCell cell)
    {
        Paragraph? first = null, last = null;
        foreach (var q in ParagraphsInBlocks(cell.Blocks)) { first ??= q; last = q; }
        return first != null ? (first, last!) : null;
    }

    private bool SelectionSpans(Paragraph first, Paragraph last)
        => ReferenceEquals(_selectionStart.Paragraph, first) && _selectionStart.Offset == 0
        && ReferenceEquals(_selectionEnd.Paragraph, last) && _selectionEnd.Offset == GetParagraphLength(last);

    private bool WholeTableSelected(TableBlock tb)
        => TableEnds(tb) is { } e && SelectionSpans(e.first, e.last);

    // First/last paragraph of a whole table, taking the first and last LOGICAL (anchor) cells.
    private static (Paragraph first, Paragraph last)? TableEnds(TableBlock tb)
    {
        TableCell? first = null, last = null;
        foreach (var (_, _, cell) in tb.LogicalCells()) { first ??= cell; last = cell; }
        if (first == null || ReferenceEquals(first, last)) return null; // single-cell: same as the cell stage
        return CellEnds(first) is { } f && CellEnds(last!) is { } l ? (f.first, l.last) : null;
    }

    private bool SelectCellContents(TableCell cell)
    {
        if (CellEnds(cell) is not { } e) return false;
        _cellSelMode = false; _cellSelTable = null;
        SetSelection(e.first, e.last);
        return true;
    }

    // Selects every cell of `tb`. Cell-selection mode makes the renderer fill the cells as a block
    // (the same chrome a multi-cell drag produces). False for a single-cell table, where this would
    // repeat the cell stage — the caller then moves on to the next level out.
    private bool SelectWholeTable(TableBlock tb)
    {
        if (TableEnds(tb) is not { } e) return false;
        _cellSelMode = true;
        _cellSelTable = tb;
        SetSelection(e.first, e.last);
        return true;
    }

    private void SetSelection(Paragraph first, Paragraph last)
    {
        _selectedBlock = null;
        _caretBlock = null;
        _selectionStart = new TextPointer(first, 0);
        _selectionEnd = new TextPointer(last, GetParagraphLength(last));
        _caretPosition = new TextPointer(last, GetParagraphLength(last));
        ResetCaretBlink();
        InvalidateVisual();
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
        // Both endpoints in the same cell = a caret/text selection inside one cell, NOT a cell block —
        // unless cell-selection mode is on for this table, where a single click selects exactly that one
        // cell as a block (HWP/Excel). (Must compare the cells directly: a merged cell spans rows/cols,
        // so a span-expanded bounding box would otherwise look multi-cell even for a single merged cell.)
        if (s.r == e.r && s.c == e.c)
        {
            if (!_cellSelMode || !ReferenceEquals(_cellSelTable, tb)) return null;
            var (cs1, rs1) = tb.SpanOf(s.r, s.c);
            return (s.r, s.c, s.r + rs1 - 1, s.c + cs1 - 1);
        }
        var (scs, srs) = tb.SpanOf(s.r, s.c);
        var (ecs, ers) = tb.SpanOf(e.r, e.c);
        int r0 = Math.Min(s.r, e.r), c0 = Math.Min(s.c, e.c);
        int r1 = Math.Max(s.r + srs - 1, e.r + ers - 1), c1 = Math.Max(s.c + scs - 1, e.c + ecs - 1);
        return (r0, c0, r1, c1);
    }

    // The cells the active cell block covers (row-major, anchors only, span-aware) — exactly the
    // rectangle the renderer fills. Null when no cell block is active, so callers fall back to the
    // linear text selection.
    //
    // This is what makes the painted selection and the operated-on range the same thing. The cell block
    // used to be render-only chrome (SelectedCellRange fed the renderer and the context menu, nothing
    // else), while every edit/format command walked the linear _selectionStart.._selectionEnd run. The
    // two disagree in both directions: the linear run starts at the drag's offset inside the first cell
    // (so the text before it survived a Delete), and document order between two corners sweeps in cells
    // that lie OUTSIDE the rectangle (a vertical block in a 3-column table also caught the cells to its
    // right). Every command now consults this first.
    private List<TableCell>? SelectedCellsBlock()
    {
        if (!_cellSelMode || _cellSelTable is not { } tb) return null;
        if (SelectedCellRange(tb) is not { } rg) return null;
        var cells = new List<TableCell>();
        var seen = new HashSet<TableCell>();
        for (int r = Math.Max(0, rg.r0); r <= rg.r1 && r < tb.Rows; r++)
            for (int c = Math.Max(0, rg.c0); c <= rg.c1 && c < tb.Columns; c++)
            {
                var (ar, ac) = tb.AnchorOf(r, c);
                var cell = tb.Cells[ar][ac];
                if (seen.Add(cell)) cells.Add(cell); // a merged cell is reached from each covered slot
            }
        return cells.Count > 0 ? cells : null;
    }

    // Every paragraph inside the active cell block, at any depth (nested and inline tables included).
    private List<Paragraph> CellBlockParagraphs(List<TableCell> cells)
    {
        var result = new List<Paragraph>();
        foreach (var cell in cells) result.AddRange(ParagraphsInBlocks(cell.Blocks));
        return result;
    }

    // Selects exactly one cell as a block and enters cell-selection mode: a further single click picks
    // another cell, a drag extends the block, a double-click drops back to a caret inside the cell.
    // SelectCellContents is the text-editing counterpart (staged Ctrl+A) — same range, but out of mode.
    private bool SelectCellAsBlock(TableBlock tb, TableCell cell)
    {
        if (CellEnds(cell) is not { } e) return false;
        _cellSelMode = true;
        _cellSelTable = tb;
        SetSelection(e.first, e.last);
        return true;
    }

    // Clears the contents of the selected cells, leaving the grid intact (Excel/HWP: Delete on a cell
    // block empties the cells; removing rows/columns stays an explicit menu action, never a side effect
    // of one key press). The caret lands in the first cleared cell and the block selection is consumed,
    // so typing straight after a Delete goes somewhere predictable.
    private void ClearSelectedCells(List<TableCell> cells)
    {
        foreach (var cell in cells)
        {
            cell.Blocks.Clear();
            cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
        }
        if (Document != null) UpdateParents(Document);
        _cellSelMode = false;
        _cellSelTable = null;
        _caretPosition = new TextPointer(cells[0].Para, 0);
        CollapseSelectionToCaret();
        MarkTextChanged();
        InvalidateMeasure(); // the rows shrink back to their empty height
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
            if (shift) { ShiftTabOutsideTable(); return; }
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

    // Shift+Tab outside a table has to undo what Tab did there. Tab types four spaces, so this removes
    // up to four spaces immediately before the caret; only when there are none — the paragraph was
    // indented from the toolbar or the shortcut instead — does it fall back to outdenting the paragraph.
    // Outdenting alone looked like the key did nothing after a Tab, because the two act on different
    // things: literal spaces in the text versus the paragraph's Indent.
    private void ShiftTabOutsideTable()
    {
        var p = _caretPosition.Paragraph;
        if (p != null && _selectionStart == _selectionEnd)
        {
            string plain = BuildPlain(p);
            int off = System.Math.Clamp(_caretPosition.Offset, 0, plain.Length);
            int start = off;
            while (start > 0 && off - start < 4 && plain[start - 1] == ' ') start--;
            if (start < off)
            {
                if (Document != null) PushUndo();
                DeleteLocalText(p, start, off - start);
                _caretPosition.Offset = start;
                CollapseSelectionToCaret();
                MarkTextChanged();
                InvalidateVisual();
                NotifyStatus();
                return;
            }
        }
        Indent(-20);
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
        // Tab navigation lands on the cell's own primary paragraph and highlights it. Selecting the cell
        // as a *unit* is a different operation (SelectCellAsBlock) — routing Tab through it would drag
        // the caret into whatever a nested table inside the cell ends with.
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
