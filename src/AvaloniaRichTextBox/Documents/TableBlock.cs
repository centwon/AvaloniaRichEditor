using System.Collections.Generic;

namespace AvaloniaRichTextBox.Documents;

public class TableBlock : Block
{
    public int Rows { get; set; } = 2;
    public int Columns { get; set; } = 2;
    public List<double> ColumnWidths { get; set; } = new();
    // User-specified minimum row heights (parallel to rows). Empty/0 means "auto" (content-driven);
    // the renderer uses Max(content height, this) so a manually dragged row can't shrink below its text.
    public List<double> RowHeights { get; set; } = new();
    public List<List<Paragraph>> Cells { get; set; } = new();

    // Cell-merge model (colspan/rowspan). Parallel grids to Cells, same Rows×Columns shape.
    //   anchor cell  : ColSpans[r][c] >= 1 && RowSpans[r][c] >= 1  (rendered/edited; spans its area)
    //   covered cell : ColSpans[r][c] == 0 && RowSpans[r][c] == 0  (overlapped by an anchor; skipped)
    // The grid is always kept dense/rectangular so Cells[r][c] indexing stays valid everywhere;
    // covered positions keep an (unused) empty Paragraph just to preserve the rectangle.
    public List<List<int>> ColSpans { get; set; } = new();
    public List<List<int>> RowSpans { get; set; } = new();

    public TableBlock()
    {
        InitializeCells(Rows, Columns);
    }

    public TableBlock(int rows, int cols)
    {
        Rows = rows;
        Columns = cols;
        InitializeCells(Rows, Columns);
    }

    private void InitializeCells(int rows, int cols)
    {
        for (int c = 0; c < cols; c++)
        {
            ColumnWidths.Add(100); // Default width
        }
        for (int r = 0; r < rows; r++)
        {
            var row = new List<Paragraph>();
            var csRow = new List<int>();
            var rsRow = new List<int>();
            for (int c = 0; c < cols; c++)
            {
                row.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
                csRow.Add(1);
                rsRow.Add(1);
            }
            Cells.Add(row);
            ColSpans.Add(csRow);
            RowSpans.Add(rsRow);
        }
    }

    private Paragraph NewCell() => new Paragraph { Inlines = { new Run { Text = "" } }, Parent = this };

    // ---- Span helpers ------------------------------------------------------

    private bool InBounds(int r, int c) => r >= 0 && r < Rows && c >= 0 && c < Columns;

    /// True when (r,c) is overlapped by a merged anchor and must be skipped in render/nav/hit-test.
    public bool IsCovered(int r, int c)
        => InBounds(r, c) && r < ColSpans.Count && c < ColSpans[r].Count && ColSpans[r][c] == 0;

    /// (colSpan, rowSpan) of an anchor; (1,1) for a plain cell. Covered cells report (0,0).
    public (int cs, int rs) SpanOf(int r, int c)
    {
        if (!InBounds(r, c) || r >= ColSpans.Count || c >= ColSpans[r].Count) return (1, 1);
        return (ColSpans[r][c], RowSpans[r][c]);
    }

    /// For any (r,c) — anchor or covered — returns the coordinates of the owning anchor.
    /// Scans up/left for the nearest anchor whose merged area contains (r,c).
    public (int ar, int ac) AnchorOf(int r, int c)
    {
        if (!IsCovered(r, c)) return (r, c);
        for (int ar = r; ar >= 0; ar--)
            for (int ac = c; ac >= 0; ac--)
            {
                var (cs, rs) = SpanOf(ar, ac);
                if (cs >= 1 && rs >= 1 && ar + rs - 1 >= r && ac + cs - 1 >= c)
                    return (ar, ac);
            }
        return (r, c); // shouldn't happen on a consistent grid
    }

    /// Row-major enumeration of logical (anchor) cells only — covered cells are skipped.
    /// Used by navigation, selection, and text extraction.
    public IEnumerable<(int r, int c, Paragraph cell)> LogicalCells()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                if (!IsCovered(r, c))
                    yield return (r, c, Cells[r][c]);
    }

    // Stamps covered markers for an anchor's merged area; (anchorR,anchorC) stays the anchor.
    private void StampCovered(int anchorR, int anchorC, int cs, int rs)
    {
        for (int r = anchorR; r < anchorR + rs && r < Rows; r++)
            for (int c = anchorC; c < anchorC + cs && c < Columns; c++)
            {
                if (r == anchorR && c == anchorC) continue;
                ColSpans[r][c] = 0;
                RowSpans[r][c] = 0;
            }
    }

    // Defensive: clamp every anchor's span to the grid bounds. Cheap safety net after structural edits.
    private void ClampSpans()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                if (ColSpans[r][c] >= 1 && RowSpans[r][c] >= 1)
                {
                    if (c + ColSpans[r][c] > Columns) ColSpans[r][c] = Columns - c;
                    if (r + RowSpans[r][c] > Rows) RowSpans[r][c] = Rows - r;
                }
            }
    }

    // ---- Structural edits --------------------------------------------------
    // Each keeps Rows/Columns, Cells, ColumnWidths, (sparse) RowHeights and the span grids consistent.
    // Merge handling: an anchor whose span *crosses* the insert/delete boundary grows/shrinks; cells in
    // the new row/column that fall inside a crossing anchor's area are marked covered.

    public void InsertRow(int at)
    {
        at = System.Math.Clamp(at, 0, Rows);
        var row = new List<Paragraph>();
        var csRow = new List<int>();
        var rsRow = new List<int>();
        for (int c = 0; c < Columns; c++) { row.Add(NewCell()); csRow.Add(1); rsRow.Add(1); }
        Cells.Insert(at, row);
        ColSpans.Insert(at, csRow);
        RowSpans.Insert(at, rsRow);
        if (at < RowHeights.Count) RowHeights.Insert(at, 0);
        Rows++;
        // Grow rowspan anchors that straddle the inserted row, and cover the new cells they reach.
        for (int r = 0; r < at; r++)
            for (int c = 0; c < Columns; c++)
            {
                var (cs, rs) = SpanOf(r, c);
                if (cs >= 1 && rs >= 1 && r + rs - 1 >= at)
                {
                    RowSpans[r][c] = rs + 1;
                    for (int cc = c; cc < c + cs && cc < Columns; cc++)
                    {
                        ColSpans[at][cc] = 0;
                        RowSpans[at][cc] = 0;
                    }
                }
            }
        ClampSpans();
    }

    public void DeleteRow(int at)
    {
        if (Rows <= 1 || at < 0 || at >= Rows) return;
        // Promote anchors that live in this row but extend below: hand ownership to the next row.
        for (int c = 0; c < Columns; c++)
        {
            var (cs, rs) = SpanOf(at, c);
            if (cs >= 1 && rs >= 1 && rs > 1 && at + 1 < Rows)
            {
                Cells[at + 1][c] = Cells[at][c];      // keep the content
                Cells[at + 1][c].Parent = this;
                ColSpans[at + 1][c] = cs;
                RowSpans[at + 1][c] = rs - 1;
            }
        }
        // Shrink rowspan anchors above that cross this row.
        for (int r = 0; r < at; r++)
            for (int c = 0; c < Columns; c++)
            {
                var (cs, rs) = SpanOf(r, c);
                if (cs >= 1 && rs >= 1 && r + rs - 1 >= at) RowSpans[r][c] = rs - 1;
            }
        Cells.RemoveAt(at);
        ColSpans.RemoveAt(at);
        RowSpans.RemoveAt(at);
        if (at < RowHeights.Count) RowHeights.RemoveAt(at);
        Rows--;
        ClampSpans();
    }

    public void InsertColumn(int at)
    {
        at = System.Math.Clamp(at, 0, Columns);
        for (int r = 0; r < Rows; r++)
        {
            Cells[r].Insert(at, NewCell());
            ColSpans[r].Insert(at, 1);
            RowSpans[r].Insert(at, 1);
        }
        ColumnWidths.Insert(System.Math.Clamp(at, 0, ColumnWidths.Count), 100);
        Columns++;
        // Grow colspan anchors that straddle the inserted column, and cover the new cells they reach.
        for (int c = 0; c < at; c++)
            for (int r = 0; r < Rows; r++)
            {
                var (cs, rs) = SpanOf(r, c);
                if (cs >= 1 && rs >= 1 && c + cs - 1 >= at)
                {
                    ColSpans[r][c] = cs + 1;
                    for (int rr = r; rr < r + rs && rr < Rows; rr++)
                    {
                        ColSpans[rr][at] = 0;
                        RowSpans[rr][at] = 0;
                    }
                }
            }
        ClampSpans();
    }

    public void DeleteColumn(int at)
    {
        if (Columns <= 1 || at < 0 || at >= Columns) return;
        // Promote anchors that live in this column but extend right: hand ownership to the next column.
        for (int r = 0; r < Rows; r++)
        {
            var (cs, rs) = SpanOf(r, at);
            if (cs >= 1 && rs >= 1 && cs > 1 && at + 1 < Columns)
            {
                Cells[r][at + 1] = Cells[r][at];
                Cells[r][at + 1].Parent = this;
                ColSpans[r][at + 1] = cs - 1;
                RowSpans[r][at + 1] = rs;
            }
        }
        // Shrink colspan anchors to the left that cross this column.
        for (int c = 0; c < at; c++)
            for (int r = 0; r < Rows; r++)
            {
                var (cs, rs) = SpanOf(r, c);
                if (cs >= 1 && rs >= 1 && c + cs - 1 >= at) ColSpans[r][c] = cs - 1;
            }
        for (int r = 0; r < Rows; r++)
        {
            Cells[r].RemoveAt(at);
            ColSpans[r].RemoveAt(at);
            RowSpans[r].RemoveAt(at);
        }
        if (at < ColumnWidths.Count) ColumnWidths.RemoveAt(at);
        Columns--;
        ClampSpans();
    }

    /// Sets an anchor's span and stamps the covered cells it overlaps. Used by the HTML parser
    /// (placements are non-overlapping by construction). Clamps to grid bounds.
    public void SetSpan(int r, int c, int cs, int rs)
    {
        if (!InBounds(r, c)) return;
        cs = System.Math.Max(1, System.Math.Min(cs, Columns - c));
        rs = System.Math.Max(1, System.Math.Min(rs, Rows - r));
        ColSpans[r][c] = cs;
        RowSpans[r][c] = rs;
        StampCovered(r, c, cs, rs);
    }

    // ---- Merge / unmerge (driven by the editing UI) ------------------------

    /// Merges the rectangular block (r0..r1, c0..c1) into a single anchor at (r0,c0).
    /// Non-empty content of covered cells is appended to the anchor. No-op on a degenerate range.
    public void MergeCells(int r0, int c0, int r1, int c1)
    {
        if (!InBounds(r0, c0) || !InBounds(r1, c1)) return;
        if (r1 < r0 || c1 < c0 || (r0 == r1 && c0 == c1)) return;
        var anchor = Cells[r0][c0];
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                if (r == r0 && c == c0) continue;
                // Append non-empty covered content into the anchor.
                var src = Cells[r][c];
                bool hasText = false;
                foreach (var inl in src.Inlines)
                    if (inl is Run run && !string.IsNullOrEmpty(run.Text)) { hasText = true; break; }
                if (hasText)
                {
                    anchor.Inlines.Add(new Run { Text = " " });
                    foreach (var inl in src.Inlines) anchor.Inlines.Add(inl);
                    src.Inlines.Clear();
                    src.Inlines.Add(new Run { Text = "" });
                }
            }
        ColSpans[r0][c0] = c1 - c0 + 1;
        RowSpans[r0][c0] = r1 - r0 + 1;
        StampCovered(r0, c0, c1 - c0 + 1, r1 - r0 + 1);
    }

    /// Splits a merged anchor back into 1×1 cells (covered cells become empty anchors).
    public void UnmergeCell(int r, int c)
    {
        var (cs, rs) = SpanOf(r, c);
        if (cs <= 1 && rs <= 1) return;
        for (int rr = r; rr < r + rs && rr < Rows; rr++)
            for (int cc = c; cc < c + cs && cc < Columns; cc++)
            {
                ColSpans[rr][cc] = 1;
                RowSpans[rr][cc] = 1;
                if (!(rr == r && cc == c))
                    Cells[rr][cc] = new Paragraph { Inlines = { new Run { Text = "" } }, Parent = this };
            }
    }

    // Force ColSpans/RowSpans to exactly match the Cells grid, filling any gaps with 1 (plain cell).
    // A safety net so a table built via a path that didn't populate the span grids can't crash here.
    public void EnsureSpanConsistency()
    {
        while (ColSpans.Count < Cells.Count) ColSpans.Add(new List<int>());
        while (RowSpans.Count < Cells.Count) RowSpans.Add(new List<int>());
        if (ColSpans.Count > Cells.Count) ColSpans.RemoveRange(Cells.Count, ColSpans.Count - Cells.Count);
        if (RowSpans.Count > Cells.Count) RowSpans.RemoveRange(Cells.Count, RowSpans.Count - Cells.Count);
        for (int r = 0; r < Cells.Count; r++)
        {
            int cols = Cells[r].Count;
            while (ColSpans[r].Count < cols) ColSpans[r].Add(1);
            while (RowSpans[r].Count < cols) RowSpans[r].Add(1);
            if (ColSpans[r].Count > cols) ColSpans[r].RemoveRange(cols, ColSpans[r].Count - cols);
            if (RowSpans[r].Count > cols) RowSpans[r].RemoveRange(cols, RowSpans[r].Count - cols);
        }
    }

    public override TextElement Clone()
    {
        EnsureSpanConsistency();
        var tb = new TableBlock(Rows, Columns);
        tb.Indent = Indent;
        tb.Cells.Clear();
        tb.ColSpans.Clear();
        tb.RowSpans.Clear();
        tb.ColumnWidths.Clear();
        foreach(var w in ColumnWidths) tb.ColumnWidths.Add(w);
        foreach(var h in RowHeights) tb.RowHeights.Add(h);

        for (int r = 0; r < Rows; r++)
        {
            var row = new List<Paragraph>();
            for (int c = 0; c < Columns; c++)
            {
                var pClone = Cells[r][c].Clone() as Paragraph;
                if (pClone != null)
                {
                    pClone.Parent = tb;
                    row.Add(pClone);
                }
            }
            tb.Cells.Add(row);
            tb.ColSpans.Add(new List<int>(ColSpans[r]));
            tb.RowSpans.Add(new List<int>(RowSpans[r]));
        }
        return tb;
    }
}
