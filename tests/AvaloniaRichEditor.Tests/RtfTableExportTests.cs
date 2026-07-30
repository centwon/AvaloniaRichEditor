using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RTF is the format HWP prefers, and the table writer only ever emitted plain runs from a cell: merged
// cells, cell shading, images, list markers, nested tables and inline tables were all dropped on export.
// Our own parser doesn't read every one of those back (merge flags and shading are export-only, and it
// flattens nested rows), so those are asserted on the emitted RTF; content is asserted by round-trip.
public class RtfTableExportTests
{
    private static string Write(FlowDocument doc) => RtfDocumentFormatter.Write(doc);
    private static FlowDocument RoundTrip(FlowDocument doc) => RtfDocumentFormatter.Parse(Write(doc));
    private static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));
    private static string CellText(TableCell c) => string.Concat(c.Blocks.OfType<Paragraph>().Select(Text));

    private static FlowDocument DocOf(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        return doc;
    }

    private static TableBlock Grid(int rows, int cols)
    {
        var tb = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"{r}{c}";
        return tb;
    }

    // ---- merged cells -------------------------------------------------------

    [Fact]
    public void HorizontallyMergedCells_EmitTheMergeFlags()
    {
        var plain = Write(DocOf(Grid(1, 3)));
        var merged = Grid(1, 3);
        merged.MergeCells(0, 0, 0, 1);

        string rtf = Write(DocOf(merged));

        Assert.DoesNotContain(@"\clmgf", plain); // the flags only appear when something is merged
        Assert.Contains(@"\clmgf", rtf);         // the anchor opens the range
        Assert.Contains(@"\clmrg", rtf);         // the column it covers continues it
    }

    [Fact]
    public void VerticallyMergedCells_EmitTheVerticalMergeFlags()
    {
        var merged = Grid(2, 1);
        merged.MergeCells(0, 0, 1, 0);

        string rtf = Write(DocOf(merged));

        Assert.Contains(@"\clvmgf", rtf);
        Assert.Contains(@"\clvmrg", rtf);
    }

    [Fact]
    public void MergingKeepsTheCellCountPerRow()
    {
        // RTF wants a \cellx for every column even when merged, so the row geometry stays intact.
        var merged = Grid(1, 3);
        merged.MergeCells(0, 0, 0, 1);

        string rtf = Write(DocOf(merged));

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(rtf, @"\\cellx").Count);
    }

    // ---- cell shading -------------------------------------------------------

    [Fact]
    public void CellBackground_EmitsShadingFromTheColourTable()
    {
        var tb = Grid(1, 2);
        tb.Cells[0][0].Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        string rtf = Write(DocOf(tb));

        Assert.Contains(@"\clcbpat", rtf);
        Assert.Contains(@"\red229\green57\blue53", rtf); // and the colour reached the table
    }

    [Fact]
    public void NoCellBackground_EmitsNoShading()
    {
        Assert.DoesNotContain(@"\clcbpat", Write(DocOf(Grid(1, 2))));
    }

    // ---- cell content beyond plain runs -------------------------------------

    [Fact]
    public void AnImageInACell_IsWritten()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7 };
        var tb = Grid(1, 1);
        var img = new InlineImage { Width = 20, Height = 20 };
        img.SetImageData(bytes, "image/png");
        tb.Cells[0][0].Para.Inlines.Add(img);

        string rtf = Write(DocOf(tb));

        // The picture has to sit inside the row, not after it: an escaped cell image landed in the body.
        int rowStart = rtf.IndexOf(@"\trowd", System.StringComparison.Ordinal);
        int rowEnd = rtf.IndexOf(@"\row", rowStart, System.StringComparison.Ordinal);
        Assert.Contains(@"\pict", rtf.Substring(rowStart, rowEnd - rowStart));
        Assert.Contains("89504e47", rtf); // the bytes themselves
    }

    [Fact]
    public void AListParagraphInACell_KeepsItsMarker()
    {
        var tb = Grid(1, 1);
        tb.Cells[0][0].Para.ListType = ListKind.Bullet;

        var back = RoundTrip(DocOf(tb));

        var cell = back.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        Assert.Contains("•", CellText(cell));
        Assert.Contains("00", CellText(cell)); // the text is still there too
    }

    [Fact]
    public void MultipleParagraphsInACell_StaySeparate()
    {
        var tb = Grid(1, 1);
        var second = new Paragraph();
        second.Inlines.Add(new Run { Text = "second" });
        tb.Cells[0][0].Blocks.Add(second);

        string rtf = Write(DocOf(tb));

        Assert.Contains("second", rtf);
        Assert.Contains(@"\par ", rtf); // separated inside the cell, not merged into one line
    }

    // ---- nested tables ------------------------------------------------------

    [Fact]
    public void ANestedTableInACell_EmitsNestedRows()
    {
        var outer = new TableBlock(1, 1);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(Grid(1, 2));

        string rtf = Write(DocOf(outer));

        Assert.Contains(@"\nestcell", rtf);
        Assert.Contains(@"\nesttableprops", rtf);
        Assert.Contains(@"\itap2", rtf);
        Assert.Contains(@"\nestrow", rtf);
    }

    [Fact]
    public void ANestedTableRoundTripsAsANestedTable()
    {
        var outer = new TableBlock(1, 1);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(Grid(1, 2));

        var back = RoundTrip(DocOf(outer));

        var cell = back.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        var inner = Assert.Single(cell.Blocks.OfType<TableBlock>());
        Assert.Equal(1, inner.Rows);
        Assert.Equal(2, inner.Columns);
        Assert.Equal("00", CellText(inner.Cells[0][0]).Trim());
        Assert.Equal("01", CellText(inner.Cells[0][1]).Trim());
    }

    // Two rows deep in the nested table, to prove the row accumulator isn't single-row.
    [Fact]
    public void AMultiRowNestedTableKeepsItsRows()
    {
        var outer = new TableBlock(1, 1);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(Grid(2, 2));

        var back = RoundTrip(DocOf(outer));

        var inner = Assert.Single(back.Blocks.OfType<TableBlock>().Single().Cells[0][0].Blocks.OfType<TableBlock>());
        Assert.Equal(2, inner.Rows);
        Assert.Equal(2, inner.Columns);
        Assert.Equal("11", CellText(inner.Cells[1][1]).Trim());
    }

    // A table nested two levels down: cell → table → cell → table.
    [Fact]
    public void ATableNestedTwoLevelsDeepSurvives()
    {
        var outer = new TableBlock(1, 1);
        var middle = new TableBlock(1, 1);
        middle.Cells[0][0].Blocks.Clear();
        middle.Cells[0][0].Blocks.Add(Grid(1, 1));
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(middle);

        var back = RoundTrip(DocOf(outer));

        var lvl1 = Assert.Single(back.Blocks.OfType<TableBlock>().Single().Cells[0][0].Blocks.OfType<TableBlock>());
        var lvl2 = Assert.Single(lvl1.Cells[0][0].Blocks.OfType<TableBlock>());
        Assert.Equal("00", CellText(lvl2.Cells[0][0]).Trim());
    }

    // The parent cell's own text and its nested table both survive, in that order.
    [Fact]
    public void AParentCellKeepsItsTextAlongsideTheNestedTable()
    {
        var outer = new TableBlock(1, 1);
        ((Run)outer.Cells[0][0].Para.Inlines[0]).Text = "parent";
        outer.Cells[0][0].Blocks.Add(Grid(1, 1));

        var back = RoundTrip(DocOf(outer));

        var cell = back.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        Assert.Contains("parent", string.Concat(cell.Blocks.OfType<Paragraph>().Select(Text)));
        Assert.Single(cell.Blocks.OfType<TableBlock>());
    }

    // Word writes a nested table's row definition inside an ignorable group. Acting on the \trowd in
    // there restarted the row mid-cell and dropped everything the parent cell had accumulated, so a Word
    // document with a nested table imported with the parent cell's text missing.
    [Fact]
    public void WordStyleNestedTableProps_DoNotDiscardTheParentCellsText()
    {
        const string rtf = @"{\rtf1\ansi\trowd\cellx2000" +
                           @"\pard\intbl\itap2 inner\nestcell" +
                           @"{\*\nesttableprops\trowd\itap2\cellx1000\nestrow}{\nonesttables\par}" +
                           @"outer\cell\row\pard done\par}";

        var doc = RtfDocumentFormatter.Parse(rtf);

        var cell = doc.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        Assert.Contains("outer", CellText(cell));           // the text that used to be discarded
        var inner = Assert.Single(cell.Blocks.OfType<TableBlock>());
        Assert.Contains("inner", CellText(inner.Cells[0][0]));
    }

    // Same root cause, outside tables: any ignorable group mid-paragraph (bookmarks, fields — routine in
    // Word output) discarded the text that came before it, because the closing brace flushed the pending
    // run while the skipped destination was still active.
    [Fact]
    public void AnIgnorableGroupMidParagraph_DoesNotEatThePrecedingText()
    {
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi before{\*\bkmkstart mark}{\*\bkmkend mark} after\par}");

        string text = string.Concat(doc.Blocks.OfType<Paragraph>().Select(Text));
        Assert.Contains("before", text);
        Assert.Contains("after", text);
    }

    // ---- inline tables ------------------------------------------------------

    [Fact]
    public void AnInlineTable_ExportsAsATableBetweenTheSurroundingText()
    {
        // RTF has no inline table, so the host paragraph splits around it. The three pieces must come out
        // in order — before it, the table, then the rest. Previously the table was dropped entirely.
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before" });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "inside";
        host.Inlines.Add(it);
        host.Inlines.Add(new Run { Text = "after" });

        string rtf = Write(DocOf(host));

        int before = rtf.IndexOf("before", System.StringComparison.Ordinal);
        int table = rtf.IndexOf(@"\trowd", System.StringComparison.Ordinal);
        int after = rtf.IndexOf("after", System.StringComparison.Ordinal);
        Assert.True(before < table && table < after, $"order was before={before} table={table} after={after}");

        var back = RoundTrip(DocOf(host));
        Assert.Equal("inside", CellText(back.Blocks.OfType<TableBlock>().Single().Cells[0][0]));
        string plain = string.Concat(back.Blocks.OfType<Paragraph>().Select(Text));
        Assert.Contains("before", plain);
        Assert.Contains("after", plain);
    }

    [Fact]
    public void AnInlineTableInACell_IsWrittenAsANestedTable()
    {
        var outer = new TableBlock(1, 1);
        var hostPara = outer.Cells[0][0].Para;
        ((Run)hostPara.Inlines[0]).Text = "host";
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "deep";
        hostPara.Inlines.Add(it);

        string rtf = Write(DocOf(outer));

        Assert.Contains(@"\nestcell", rtf);
        Assert.Contains("deep", rtf);
    }
}
