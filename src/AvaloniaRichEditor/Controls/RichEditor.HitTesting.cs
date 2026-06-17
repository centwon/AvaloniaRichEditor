using System;
using System.Collections.Generic;
using Avalonia;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Hit-testing: point -> document position/block/run mapping, shared table geometry (LayoutTable)
// and the inline-image offset helpers. All consumers (render, caret, selection, context menu)
// derive geometry from the same code here so they can never disagree. Part of RichEditor (split
// out of the main file for readability).
public partial class RichEditor
{
    // Returns the Run directly under the point if the point lands on rendered text, else null.
    // Used for hyperlink hover/click detection.
    private Run? GetLinkRunAtPoint(Point p)
    {
        if (Document == null) return null;
        double yOffset = 0, maxWidth = ContentLayoutWidth;
        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            double top = yOffset;
            double h = BlockExtent(block, maxWidth, top, out var ft, out var tl);
            yOffset += h + block.MarginBottom;
            if (block is TableBlock tb && tl is { } t)
            {
                foreach (var (r, c, rect) in t.AnchorRects)
                    if (rect.Contains(p))
                    {
                        var cell = tb.Cells[r][c];
                        var layout = BuildTextLayout(cell, Math.Max(10, rect.Width - 10));
                        var hit = layout.HitTestPoint(new Point(p.X - (rect.X + 5), p.Y - (rect.Y + 5)));
                        return hit.IsInside ? RunAtOffset(cell, hit.TextPosition) : null;
                    }
            }
            else if (block is Paragraph paragraph && ft != null && p.Y >= top && p.Y <= top + h)
            {
                double plink = ParaLeft(paragraph);
                var hit = ft.HitTestPoint(new Point(p.X - plink, p.Y - top));
                return hit.IsInside ? RunAtOffset(paragraph, hit.TextPosition) : null;
            }
        }
        return null;
    }

    private static bool IsCellOf(TableBlock tb, Paragraph p)
    {
        for (int r = 0; r < tb.Rows; r++)
            for (int c = 0; c < tb.Columns; c++)
                if (tb.Cells[r][c] == p) return true;
        return false;
    }

    // Pixel geometry of one table. Anchor cells get a rect spanning their merged columns/rows;
    // covered cells are absorbed into their anchor and never appear here.
    private readonly struct TableLayout
    {
        public readonly double[] ColX;       // length Columns+1: left edge of each column + right end
        public readonly double[] RowY;       // length Rows+1: top edge of each row + bottom end
        public readonly double TableWidth;
        public readonly double TotalHeight;
        public readonly List<(int r, int c, Rect rect)> AnchorRects;
        public TableLayout(double[] colX, double[] rowY, double w, double h, List<(int, int, Rect)> anchors)
        { ColX = colX; RowY = rowY; TableWidth = w; TotalHeight = h; AnchorRects = anchors; }
    }

    // Single source of truth for a table's geometry. Render and all three hit-tests consume this so
    // merged-cell rects and skipped (covered) cells stay identical across every consumer.
    private TableLayout LayoutTable(TableBlock tb, double startX, double top)
    {
        int cols = tb.Columns, rows = tb.Rows;

        if (_trustLayoutCache && _tableLayoutCache.TryGetValue(tb, out var ct) && ct.startX == startX
            && ct.rowH.Length == rows)
        {
            // Exact match (same startX AND top): reuse the cached geometry verbatim — zero allocation,
            // the common case in continuous mode across blink/scroll/hover frames.
            if (ct.top == top) return ct.layout;
            // Same startX, different top (every frame in page view: pagination measures at continuous y,
            // render at per-page slice y): the column geometry and the measured row heights are unchanged,
            // so reuse them and only re-place the rows/anchors at the new top — skips re-measuring cells.
            var moved = AssembleTableLayout(tb, ct.layout.ColX, ct.rowH, startX, top);
            _tableLayoutCache[tb] = (startX, top, ct.rowH, moved);
            return moved;
        }

        var colX = new double[cols + 1];
        colX[0] = startX;
        for (int c = 0; c < cols; c++)
            colX[c + 1] = colX[c] + ((c < tb.ColumnWidths.Count) ? tb.ColumnWidths[c] : 100);

        var rowH = new double[rows];
        for (int r = 0; r < rows; r++) rowH[r] = 20;

        // Base row heights come from single-row cells (rowSpan == 1) measured at their merged width.
        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            var (cs, rs) = tb.SpanOf(r, c);
            if (rs != 1) continue;
            double w = colX[Math.Min(c + cs, cols)] - colX[c];
            var l = BuildTextLayout(cell, Math.Max(10, w - 10));
            if (l.Height + 10 > rowH[r]) rowH[r] = l.Height + 10;
        }
        for (int r = 0; r < rows; r++)
            if (r < tb.RowHeights.Count && tb.RowHeights[r] > rowH[r]) rowH[r] = tb.RowHeights[r];

        // Row-spanning cells: if content needs more than the spanned rows provide, grow the last row.
        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            var (cs, rs) = tb.SpanOf(r, c);
            if (rs <= 1) continue;
            double w = colX[Math.Min(c + cs, cols)] - colX[c];
            var l = BuildTextLayout(cell, Math.Max(10, w - 10));
            double need = l.Height + 10, have = 0;
            for (int rr = r; rr < r + rs && rr < rows; rr++) have += rowH[rr];
            int last = Math.Min(r + rs - 1, rows - 1);
            if (need > have) rowH[last] += need - have;
        }

        var result = AssembleTableLayout(tb, colX, rowH, startX, top);
        // Refresh the cache (even on an untrusted/edit pass) so the next trusted frame can reuse it.
        if (_tableLayoutCache.Count > 2000) _tableLayoutCache.Clear();
        _tableLayoutCache[tb] = (startX, top, rowH, result);
        return result;
    }

    // Places the rows at `top` and builds the anchor rects from the (position-independent) measured
    // column edges `colX` and row heights `rowH`. Split out so a cached table can be re-placed at a new
    // `top` without re-measuring its cells (the costly part).
    private static TableLayout AssembleTableLayout(TableBlock tb, double[] colX, double[] rowH, double startX, double top)
    {
        int cols = tb.Columns, rows = tb.Rows;
        var rowY = new double[rows + 1];
        rowY[0] = top;
        for (int r = 0; r < rows; r++) rowY[r + 1] = rowY[r] + rowH[r];

        var anchors = new List<(int, int, Rect)>();
        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            var (cs, rs) = tb.SpanOf(r, c);
            int cEnd = Math.Min(c + cs, cols), rEnd = Math.Min(r + rs, rows);
            anchors.Add((r, c, new Rect(colX[c], rowY[r], colX[cEnd] - colX[c], rowY[rEnd] - rowY[r])));
        }
        return new TableLayout(colX, rowY, colX[cols] - startX, rowY[rows] - top, anchors);
    }

    // G1 — single source of a block's vertical extent (height, EXCLUDING MarginTop/MarginBottom) at the
    // given top, plus the layout objects the walkers reuse (a paragraph's TextLayout / a table's
    // TableLayout; null otherwise). Every read-only document walk — measure, hit-tests, block-at-y —
    // advances through this so they can never disagree on a block's height the way the duplicated
    // per-walker `switch`es used to (the historical hardcoded-10 MarginBottom bug came from exactly that
    // drift). Pagination also advances through this (it adds only the within-block row/line atom split
    // on top). Render still computes its own advance (it needs the draw/cull logic); migrating it is the
    // last G1 phase.
    private double BlockExtent(Block block, double maxWidth, double top,
        out Avalonia.Media.TextFormatting.TextLayout? paraLayout, out TableLayout? tableLayout)
    {
        paraLayout = null;
        tableLayout = null;
        switch (block)
        {
            case TableBlock tb:
                var tl = LayoutTable(tb, 10 + tb.Indent, top);
                tableLayout = tl;
                return tl.TotalHeight;
            case ImageBlock img:
                return img.Height > 0 ? img.Height : 200;
            case DividerBlock:
                return DividerHeight;
            case Paragraph p:
                if (GetParagraphLength(p) == 0)
                {
                    if (!double.IsNaN(p.LineSpacing))
                    {
                        bool hd = p.HeadingLevel is >= 1 and <= 6;
                        double basePt = hd ? HeadingFontSize(p.HeadingLevel) : DefaultFontSize;
                        return Math.Max(p.LineSpacing, 1.0) * PtToPx(basePt) * NaturalLineFactor;
                    }
                    return !double.IsNaN(p.LineHeight) ? p.LineHeight : 20;
                }
                paraLayout = BuildTextLayout(p, Math.Max(10, maxWidth - 20 - ParaLeft(p) - p.MarginRight));
                return paraLayout.Height;
            default:
                return 0;
        }
    }

    // The block (image or table) whose rendered rectangle contains the point, or null.
    private Block? GetBlockAtPoint(Point p)
    {
        if (Document == null) return null;
        double yOffset = 0, listIndent = 10, maxWidth = ContentLayoutWidth;
        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            double top = yOffset;
            double h = BlockExtent(block, maxWidth, top, out _, out var tl);
            yOffset += h + block.MarginBottom;
            if (block is TableBlock tb && tl is { } t)
            {
                if (p.X >= 10 + tb.Indent && p.X <= 10 + tb.Indent + t.TableWidth && p.Y >= top && p.Y <= top + h) return tb;
            }
            else if (block is ImageBlock img)
            {
                double w = img.Width > 0 ? img.Width : 200;
                if (p.X >= listIndent + img.Indent && p.X <= listIndent + img.Indent + w && p.Y >= top && p.Y <= top + h) return img;
            }
        }
        return null;
    }

    // Top y and geometry of a given table, mirroring the block advancement used by the hit-tests.
    private (double top, TableLayout tl)? GetTableRect(TableBlock target)
    {
        if (Document == null) return null;
        double yOffset = 0, maxWidth = ContentLayoutWidth;
        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            double top = yOffset;
            double h = BlockExtent(block, maxWidth, top, out _, out var tl);
            if (block == target && tl is { } t) return (top, t);
            yOffset += h + block.MarginBottom;
        }
        return null;
    }

    // True when the point sits on the table's outer left or top border (a thin band). The right/bottom
    // borders are reserved for resize handles, so only left/top trigger whole-table selection.
    private bool IsOnTableLeftOrTopBorder(TableBlock tb, Point p)
    {
        if (GetTableRect(tb) is not { } tr) return false;
        double top = tr.top, w = tr.tl.TableWidth, h = tr.tl.TotalHeight, left = 10 + tb.Indent;
        const double m = 4;
        bool inY = p.Y >= top - m && p.Y <= top + h + m;
        bool inX = p.X >= left - m && p.X <= left + w + m;
        return (inY && Math.Abs(p.X - left) <= m) || (inX && Math.Abs(p.Y - top) <= m);
    }

    private static Run? RunAtOffset(Paragraph p, int offset)
    {
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            int len = InlineLen(inl);
            if (inl is Run run && offset >= idx && offset < idx + len) return run;
            idx += len;
        }
        return null;
    }

    // The inline image whose logical position ends exactly at `offset` (i.e. the caret sits right
    // after it). Used to correct the caret X next to a trailing image.
    private static InlineImage? InlineImageEndingAt(Paragraph p, int offset)
    {
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            idx += InlineLen(inl);
            if (inl is InlineImage img && idx == offset) return img;
            if (idx > offset) break;
        }
        return null;
    }

    // Works around an Avalonia hit-test quirk: a DrawableTextRun at the very end of a line is
    // excluded from caret-distance computation, so the position right after a trailing inline
    // image collapses to the image's *left* edge (typing there visibly lands "behind" the image
    // and Right-arrow appears stuck). When the caret sits right after an image and the reported X
    // didn't advance past the image's start, pin it to the image's right edge.
    internal static Rect FixCaretAfterTrailingImage(Avalonia.Media.TextFormatting.TextLayout layout,
        Paragraph p, int logicalOffset, int displayIndex, Rect cr)
    {
        if (displayIndex <= 0 || InlineImageEndingAt(p, logicalOffset) is not { } img) return cr;
        var ir = layout.HitTestTextPosition(displayIndex - 1);
        double w = Math.Max(8, img.Width > 0 ? img.Width : 16);
        return cr.X <= ir.X + 0.5 ? cr.WithX(ir.X + w) : cr;
    }

    // The inline image occupying the logical position at `offset`. An image is one position wide, so a
    // click on it can land on either edge — check both the position and the one before it.
    private static InlineImage? InlineImageAt(Paragraph p, int offset)
    {
        int idx = 0;
        foreach (var inl in p.Inlines)
        {
            int len = InlineLen(inl);
            if (inl is InlineImage img && (offset == idx || offset == idx + len)) return img;
            idx += len;
        }
        return null;
    }

    private int HitTestIndex(Avalonia.Media.TextFormatting.TextLayout layout, Point localPoint)
    {
        var hit = layout.HitTestPoint(localPoint);
        return hit.TextPosition + (hit.IsTrailing ? 1 : 0);
    }

    private TextPointer GetPositionFromPoint(Point p)
    {
        if (Document == null || Document.Blocks.Count == 0)
            return new TextPointer(null, 0);

        double yOffset = 0;
        double maxWidth = ContentLayoutWidth;
        double bestDistY = double.MaxValue;
        Paragraph? bestPara = null;
        int bestLocalIndex = 0;

        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            double top = yOffset;
            double h = BlockExtent(block, maxWidth, top, out var ft, out var tl);
            yOffset += h + block.MarginBottom;
            if (block is TableBlock tb && tl is { } t)
            {
                foreach (var (r, c, rect) in t.AnchorRects)
                {
                    var cell = tb.Cells[r][c];
                    var (cs, _) = tb.SpanOf(r, c);
                    bool lastCol = c + cs >= tb.Columns;
                    bool xInside = (p.X >= rect.X && p.X <= rect.Right) || (lastCol && p.X > rect.Right);
                    if (xInside && p.Y >= rect.Y && p.Y <= rect.Bottom)
                    {
                        var cft = BuildTextLayout(cell, Math.Max(10, rect.Width - 10));
                        int idx = HitTestIndex(cft, new Point(p.X - (rect.X + 5), p.Y - (rect.Y + 5)));
                        return new TextPointer(cell, idx);
                    }
                }
                // Outside any cell: remember the nearest anchor (by vertical distance) as a fallback.
                foreach (var (r, c, rect) in t.AnchorRects)
                {
                    double distY = p.Y < rect.Y ? rect.Y - p.Y : (p.Y > rect.Bottom ? p.Y - rect.Bottom : 0);
                    if (distY < bestDistY)
                    {
                        bestDistY = distY;
                        bestPara = tb.Cells[r][c];
                        bestLocalIndex = GetParagraphLength(bestPara);
                    }
                }
            }
            else if (block is Paragraph paragraph)
            {
                if (ft == null) // empty paragraph: extent is a single line height
                {
                    double dY = p.Y < top ? top - p.Y : (p.Y > top + h ? p.Y - (top + h) : 0);
                    if (dY < bestDistY) { bestDistY = dY; bestPara = paragraph; bestLocalIndex = 0; }
                }
                else
                {
                    double ppos = ParaLeft(paragraph);
                    double distY2 = p.Y < top ? top - p.Y : (p.Y > top + h ? p.Y - (top + h) : 0);
                    if (distY2 < bestDistY)
                    {
                        bestDistY = distY2;
                        bestPara = paragraph;
                        bestLocalIndex = HitTestIndex(ft, new Point(p.X - ppos, p.Y - top));
                    }
                }
            }
        }
        return bestPara != null ? new TextPointer(bestPara, bestLocalIndex) : new TextPointer(Document.Blocks[0] as Paragraph, 0);
    }
}
