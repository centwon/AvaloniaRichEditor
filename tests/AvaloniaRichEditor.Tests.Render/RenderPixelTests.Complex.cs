using System;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// P2: the render paths the other file doesn't reach. Those nine tests cover single paragraphs, one
// divider, a flat table and a two-page paragraph run; what the engine actually gets wrong is the
// recursive and clipped drawing — merged cells, nested tables, a table straddling a page break, and an
// inline table drawn inside a text line. Assertions stay structural (border counts, ink bounds, which
// band carries text), never golden images: exact pixels aren't portable across platforms.
public partial class RenderPixelTests
{
    // Ink bounding box (x0, y0, x1, y1), or null when nothing was drawn.
    private static (int x0, int y0, int x1, int y1)? InkBounds(byte[] px, int w, int h)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (IsInk(px, w, x, y))
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }
        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    // Columns inked down most of the band: a table's vertical borders. Glyph stems are far shorter than a
    // cell is tall, so they never reach the threshold. Each border counts once however wide it rasterizes.
    private static int VerticalLineCount(byte[] px, int w, int y0, int y1)
    {
        int need = (int)((y1 - y0) * 0.8), lines = 0;
        bool inLine = false;
        for (int x = 0; x < w; x++)
        {
            int run = 0;
            for (int y = y0; y < y1; y++) if (IsInk(px, w, x, y)) run++;
            bool isLine = run >= need;
            if (isLine && !inLine) lines++;
            inLine = isLine;
        }
        return lines;
    }

    // Widest continuous horizontal ink run in the band: a table's top or bottom border.
    private static int WidestHorizontalRun(byte[] px, int w, int y0, int y1)
    {
        int widest = 0;
        for (int y = y0; y < y1; y++)
        {
            int run = 0;
            for (int x = 0; x < w; x++)
            {
                if (IsInk(px, w, x, y)) { run++; if (run > widest) widest = run; }
                else run = 0;
            }
        }
        return widest;
    }

    // First and last rows in the band carrying a horizontal ink run of at least `minRun`: a table's
    // top and bottom borders. Used to find a nested table's own extent inside its parent.
    private static (int top, int bottom) BorderRows(byte[] px, int w, int y0, int y1, int minRun)
    {
        int top = -1, bottom = -1;
        for (int y = y0; y < y1; y++)
        {
            int run = 0, best = 0;
            for (int x = 0; x < w; x++)
            {
                if (IsInk(px, w, x, y)) { run++; if (run > best) best = run; }
                else run = 0;
            }
            if (best >= minRun) { if (top < 0) top = y; bottom = y; }
        }
        return (top, bottom);
    }

    private static int ColouredCountInRegion(byte[] px, int w, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * w + x) * 4;
                int max = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                int min = Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
                if (px[i + 3] > 20 && max - min > 40) n++;
            }
        return n;
    }

    private static RichEditor TableEditor(TableBlock tb, RichEditorPageSize size = RichEditorPageSize.Continuous)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        return new RichEditor { Document = doc, PageSize = size, DefaultFontFamily = Inter };
    }

    // A grid whose every cell carries its own "rc" text, so cells are distinguishable in ink terms.
    private static TableBlock Grid(int rows, int cols)
    {
        var tb = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"{r}{c}";
        return tb;
    }

    // ---- complex tables -----------------------------------------------------

    [AvaloniaFact]
    public void MergedCells_DropTheBorderBetweenThem()
    {
        // Merging is a span change in the model, but what the user sees is the internal border going
        // away. A merge that only widened the anchor cell would still paint the old divider.
        const int w = 400, h = 160;

        var plainPx = Render(TableEditor(Grid(1, 3)), w, h);
        var pb = InkBounds(plainPx, w, h)!.Value;
        int plainLines = VerticalLineCount(plainPx, w, pb.y0, pb.y1);

        var merged = Grid(1, 3);
        merged.MergeCells(0, 0, 0, 1); // the first two of three columns become one cell
        var mergedPx = Render(TableEditor(merged), w, h);
        var mb = InkBounds(mergedPx, w, h)!.Value;
        int mergedLines = VerticalLineCount(mergedPx, w, mb.y0, mb.y1);

        Assert.True(plainLines >= 4, $"test setup: a 1x3 table should show 4 vertical borders, saw {plainLines}");
        Assert.Equal(plainLines - 1, mergedLines);
        Assert.Equal(pb.x1, mb.x1); // and the table's outer width must not change
    }

    [AvaloniaFact]
    public void CellBackground_PaintsThatCellOnly()
    {
        // Per-cell shading is drawn by the same cell walk that has to skip covered cells; a bug there
        // bleeds the fill into the neighbour.
        const int w = 400, h = 160;
        var tb = Grid(1, 2);
        tb.Cells[0][0].Background = new SolidColorBrush(Color.Parse("#E53935")); // strongly chromatic
        var px = Render(TableEditor(tb), w, h);
        var b = InkBounds(px, w, h)!.Value;

        int mid = (b.x0 + b.x1) / 2;
        int left = ColouredCountInRegion(px, w, b.x0 + 2, b.y0 + 2, mid - 2, b.y1 - 1);
        int right = ColouredCountInRegion(px, w, mid + 2, b.y0 + 2, b.x1 - 1, b.y1 - 1);

        Assert.True(left > 200, $"the shaded cell should be filled, got {left} coloured pixels");
        Assert.True(right < 20, $"the neighbour must stay unshaded, got {right} coloured pixels");
    }

    [AvaloniaFact]
    public void NestedTable_DrawsInsideItsParentCell()
    {
        // Cells recurse into DrawCellBlockList/DrawNestedTable at arbitrary depth. At pixel level the
        // inner table's borders must appear within the outer cell and add ink of their own.
        const int w = 500, h = 260;

        int emptyInk = InkCount(Render(TableEditor(new TableBlock(1, 1)), w, h), w, h);

        var outer = new TableBlock(1, 1);
        outer.Cells[0][0].Blocks.Add(Grid(2, 2));
        var px = Render(TableEditor(outer), w, h);
        var bounds = InkBounds(px, w, h)!.Value;

        Assert.True(InkCount(px, w, h) > emptyInk + 200, "the nested table should add ink");

        // The inner table's own borders, found below the outer's top border and above its bottom one.
        var (innerTop, innerBottom) = BorderRows(px, w, bounds.y0 + 3, bounds.y1 - 2, 40);
        Assert.True(innerTop > bounds.y0 && innerBottom < bounds.y1,
            $"the inner table must be drawn strictly inside the outer cell (rows {innerTop}..{innerBottom} in {bounds})");

        // Across the inner table's own height: its left, middle and right borders.
        int lines = VerticalLineCount(px, w, innerTop + 2, innerBottom - 1);
        Assert.True(lines >= 3, $"expected the inner 2x2's three vertical borders, saw {lines}");
    }

    // ---- page splitting -----------------------------------------------------

    private static RichEditor LongTableOnA5(out int paperHeight)
    {
        var ed = TableEditor(Grid(40, 2), RichEditorPageSize.A5);
        paperHeight = (int)ed.GetPaperPixelSize().Height;
        return ed;
    }

    [AvaloniaFact]
    public void PageView_SplitsATableAcrossTwoPages()
    {
        // Pagination treats table ROWS as atoms, so a long table continues on the next page at a row
        // boundary, drawn by the page-stack clip+replay. A broken replay leaves page 2 blank.
        var ed = LongTableOnA5(out int paperH);
        Assert.True(ed.GetPrintPageCount() >= 2, "test setup: the table should span more than one page");

        int w = 700, h = paperH * 2 + 40;
        var px = Render(ed, w, h);

        Assert.True(AnyDarkTextInBand(px, w, 0, paperH), "page 1 should carry table text");
        Assert.True(AnyDarkTextInBand(px, w, paperH + 14, h), "page 2 should carry the remaining rows");
    }

    [AvaloniaFact]
    public void PageView_DrawsNothingInTheGapBetweenPages()
    {
        // The per-page clip is what stops a split block painting across the desk between two sheets.
        // Text in the gap band means the clip leaked — rows floating off the paper.
        var ed = LongTableOnA5(out int paperH);
        int w = 700, h = paperH * 2 + 40;
        var px = Render(ed, w, h);

        int gapTop = paperH + 2;
        int gapBottom = FirstPaperRowAfter(px, w, h, gapTop);
        Assert.True(gapBottom > gapTop + 2, $"test setup: expected a visible desk gap, page 2 started at {gapBottom}");
        // The detector has to be able to see text in this bitmap at all, or the check below is vacuous.
        Assert.True(AnyDarkTextInBand(px, w, 0, paperH), "test setup: page 1 should carry text");

        Assert.False(AnyDarkTextInBand(px, w, gapTop, gapBottom - 1),
            "content leaked into the desk gap between two pages");
    }

    // First row at or after `from` that is mostly white paper again: the top of the next sheet. The desk
    // between sheets is grey, so this finds the gap's end without hard-coding the gap size.
    private static int FirstPaperRowAfter(byte[] px, int w, int h, int from)
    {
        for (int y = from; y < h; y++)
        {
            int white = 0;
            for (int x = 0; x < w; x++)
            {
                var (b, g, r, _) = Px(px, w, x, y);
                if (r > 240 && g > 240 && b > 240) white++;
            }
            if (white > w / 2) return y;
        }
        return h;
    }

    // ---- inline tables ------------------------------------------------------

    // A paragraph carrying "before [inline table] after", or the same text without the table.
    private static RichEditor InlineTableParagraph(bool withTable)
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "before " });
        if (withTable)
        {
            var it = new InlineTable { Table = new TableBlock(1, 1) };
            ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "x";
            p.Inlines.Add(it);
        }
        p.Inlines.Add(new Run { Text = " after" });
        doc.Blocks.Add(p);
        return new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous, DefaultFontFamily = Inter };
    }

    [AvaloniaFact]
    public void InlineTable_DrawsItsBordersInsideTheTextLine()
    {
        // Milestone B draws an inline table through a DrawableTextRun, inside the paragraph's own layout.
        // The proof is a table-width horizontal border where plain text leaves only short glyph runs.
        const int w = 500, h = 220;

        var plain = Render(InlineTableParagraph(withTable: false), w, h);
        var withIt = Render(InlineTableParagraph(withTable: true), w, h);
        var pb = InkBounds(plain, w, h)!.Value;
        var ib = InkBounds(withIt, w, h)!.Value;

        Assert.True(WidestHorizontalRun(plain, w, pb.y0, pb.y1 + 1) < 30,
            "test setup: plain text should not leave a long continuous run");
        Assert.True(WidestHorizontalRun(withIt, w, ib.y0, ib.y1 + 1) > 40,
            "the inline table's border should draw as a long horizontal run");
    }

    [AvaloniaFact]
    public void InlineTable_MakesItsLineTaller()
    {
        // An inline table is one character logically but a block visually: the host line grows to its
        // height. This is the reflow round 3 got wrong (the paragraph kept its old height until the
        // next edit).
        const int w = 500, h = 220;
        int plainSpan = InkRowSpan(Render(InlineTableParagraph(withTable: false), w, h), w, h);
        int tableSpan = InkRowSpan(Render(InlineTableParagraph(withTable: true), w, h), w, h);

        Assert.True(plainSpan > 0, "test setup: the text should raster");
        Assert.True(tableSpan > plainSpan * 2,
            $"the line holding an inline table should be much taller ({tableSpan} vs {plainSpan})");
    }

    [AvaloniaFact]
    public void InlineTableInsideACell_StillDraws()
    {
        // The recursion the engine reuses everywhere: a cell holds a paragraph, which holds an inline
        // table, which holds cells. One missing hop and the innermost table silently vanishes.
        const int w = 500, h = 260;

        var bare = new TableBlock(1, 1);
        ((Run)bare.Cells[0][0].Para.Inlines[0]).Text = "host";
        int bareInk = InkCount(Render(TableEditor(bare), w, h), w, h);

        var outer = new TableBlock(1, 1);
        var hostPara = outer.Cells[0][0].Para;
        ((Run)hostPara.Inlines[0]).Text = "host";
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "x";
        hostPara.Inlines.Add(it);
        int nestedInk = InkCount(Render(TableEditor(outer), w, h), w, h);

        Assert.True(nestedInk > bareInk + 100,
            $"the inline table inside the cell should add ink ({nestedInk} vs {bareInk})");
    }
}
