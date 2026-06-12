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
        double yOffset = 0;
        double maxWidth = ContentLayoutWidth;

        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            if (block is TableBlock tb)
            {
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    if (rect.Contains(p))
                    {
                        var cell = tb.Cells[r][c];
                        var layout = BuildTextLayout(cell, Math.Max(10, rect.Width - 10));
                        var hit = layout.HitTestPoint(new Point(p.X - (rect.X + 5), p.Y - (rect.Y + 5)));
                        return hit.IsInside ? RunAtOffset(cell, hit.TextPosition) : null;
                    }
                }
                yOffset += tl.TotalHeight + tb.MarginBottom;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);
                if (fullText == "")
                {
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }
                double plink = ParaLeft(paragraph);
                var layout = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - plink - paragraph.MarginRight));
                double height = layout.Height;
                if (p.Y >= yOffset && p.Y <= yOffset + height)
                {
                    var hit = layout.HitTestPoint(new Point(p.X - plink, p.Y - yOffset));
                    return hit.IsInside ? RunAtOffset(paragraph, hit.TextPosition) : null;
                }
                yOffset += height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                yOffset += (img.Height > 0 ? img.Height : 200) + img.MarginBottom;
            }
            else if (block is DividerBlock dv)
            {
                yOffset += DividerHeight + dv.MarginBottom;
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

    // The block (image or table) whose rendered rectangle contains the point, or null.
    private Block? GetBlockAtPoint(Point p)
    {
        if (Document == null) return null;
        double yOffset = 0, listIndent = 10, maxWidth = ContentLayoutWidth;
        foreach (var block in Document.Blocks)
        {
            yOffset += block.MarginTop;
            if (block is TableBlock tb)
            {
                double tableTop = yOffset;
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                yOffset += tl.TotalHeight;
                if (p.X >= 10 + tb.Indent && p.X <= 10 + tb.Indent + tl.TableWidth && p.Y >= tableTop && p.Y <= yOffset) return tb;
                yOffset += tb.MarginBottom;
            }
            else if (block is Paragraph paragraph)
            {
                if (BuildPlain(paragraph) == "")
                {
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }
                var layout = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ParaLeft(paragraph) - paragraph.MarginRight));
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is DividerBlock dv)
            {
                yOffset += DividerHeight + dv.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                double w = img.Width > 0 ? img.Width : 200;
                double h = img.Height > 0 ? img.Height : 200;
                if (p.X >= listIndent + img.Indent && p.X <= listIndent + img.Indent + w && p.Y >= yOffset && p.Y <= yOffset + h) return img;
                yOffset += h + img.MarginBottom;
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
            if (block is TableBlock tb)
            {
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                if (tb == target) return (yOffset, tl);
                yOffset += tl.TotalHeight + tb.MarginBottom;
            }
            else if (block is Paragraph paragraph)
            {
                if (BuildPlain(paragraph) == "")
                {
                    yOffset += paragraph.MarginBottom + (!double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20);
                    continue;
                }
                var layout = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ParaLeft(paragraph) - paragraph.MarginRight));
                yOffset += layout.Height + paragraph.MarginBottom;
            }
            else if (block is DividerBlock dv) yOffset += DividerHeight + dv.MarginBottom;
            else if (block is ImageBlock img) yOffset += (img.Height > 0 ? img.Height : 200) + img.MarginBottom;
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
            if (block is TableBlock tb)
            {
                var tl = LayoutTable(tb, 10 + tb.Indent, yOffset);
                foreach (var (r, c, rect) in tl.AnchorRects)
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
                foreach (var (r, c, rect) in tl.AnchorRects)
                {
                    double distY = p.Y < rect.Y ? rect.Y - p.Y : (p.Y > rect.Bottom ? p.Y - rect.Bottom : 0);
                    if (distY < bestDistY)
                    {
                        bestDistY = distY;
                        bestPara = tb.Cells[r][c];
                        bestLocalIndex = GetParagraphLength(bestPara);
                    }
                }
                yOffset += tl.TotalHeight + tb.MarginBottom;
            }
            else if (block is Paragraph paragraph)
            {
                string fullText = BuildPlain(paragraph);

                if (fullText == "")
                {
                    double lh = !double.IsNaN(paragraph.LineHeight) ? paragraph.LineHeight : 20;
                    double dY = p.Y < yOffset ? yOffset - p.Y : (p.Y > yOffset + lh ? p.Y - (yOffset + lh) : 0);
                    if (dY < bestDistY) { bestDistY = dY; bestPara = paragraph; bestLocalIndex = 0; }
                    yOffset += paragraph.MarginBottom + lh;
                    continue;
                }

                double ppos = ParaLeft(paragraph);
                var ft = BuildTextLayout(paragraph, Math.Max(10, maxWidth - 20 - ppos - paragraph.MarginRight));
                double height = ft.Height;

                double distY2 = p.Y < yOffset ? yOffset - p.Y : (p.Y > yOffset + height ? p.Y - (yOffset + height) : 0);
                if (distY2 < bestDistY)
                {
                    bestDistY = distY2;
                    bestPara = paragraph;
                    bestLocalIndex = HitTestIndex(ft, new Point(p.X - ppos, p.Y - yOffset));
                }
                yOffset += height + paragraph.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                double height = img.Height > 0 ? img.Height : 200;
                yOffset += height + img.MarginBottom;
            }
            else if (block is DividerBlock dv)
            {
                yOffset += DividerHeight + dv.MarginBottom;
            }
        }
        return bestPara != null ? new TextPointer(bestPara, bestLocalIndex) : new TextPointer(Document.Blocks[0] as Paragraph, 0);
    }
}
