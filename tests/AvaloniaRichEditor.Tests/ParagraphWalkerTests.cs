using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// G2 — the paragraph-order walkers that were still one level deep after milestones A/B gave the
// document real depth (cells holding sibling blocks, nested tables, inline tables). Each of these
// silently skipped anything below the first paragraph of a cell.
public class ParagraphWalkerTests
{
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static Paragraph NewPara(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        return p;
    }

    // A cell holding the given blocks (replacing the default empty paragraph).
    private static TableBlock CellWith(params Block[] blocks)
    {
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Blocks.Clear();
        foreach (var b in blocks) tb.Cells[0][0].Blocks.Add(b);
        return tb;
    }

    // ---- (2) TextRange.Delete: siblings in one container merge --------------

    [AvaloniaFact]
    public void Delete_AcrossTwoParagraphsOfOneCell_MergesThem()
    {
        var p1 = NewPara("AB");
        var p2 = NewPara("CD");
        var tb = CellWith(p1, p2);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        new TextRange(new TextPointer(p1, 1), new TextPointer(p2, 1)).Delete();

        var paras = tb.Cells[0][0].Blocks.OfType<Paragraph>().ToList();
        Assert.Single(paras);
        Assert.Equal("AD", paras[0].Text());
    }

    // A block spanned by the selection inside the cell goes with it, like at the top level.
    [AvaloniaFact]
    public void Delete_AcrossOneCell_RemovesSpannedBlocksBetween()
    {
        var p1 = NewPara("AB");
        var mid = NewPara("MIDDLE");
        var p2 = NewPara("CD");
        var tb = CellWith(p1, mid, p2);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        new TextRange(new TextPointer(p1, 1), new TextPointer(p2, 1)).Delete();

        var blocks = tb.Cells[0][0].Blocks;
        Assert.DoesNotContain(blocks, b => ReferenceEquals(b, mid));
        Assert.Single(blocks.OfType<Paragraph>());
        Assert.Equal("AD", blocks.OfType<Paragraph>().First().Text());
    }

    // Guard: a selection that really does cross cells must still NOT merge — that would drag the end
    // cell's remainder into the start cell, across the grid.
    [AvaloniaFact]
    public void Delete_AcrossTwoDifferentCells_KeepsTheGridStructure()
    {
        var tb = new TableBlock(1, 2);
        var left = tb.Cells[0][0].Para;
        var right = tb.Cells[0][1].Para;
        ((Run)left.Inlines[0]).Text = "AB";
        ((Run)right.Inlines[0]).Text = "CD";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        new TextRange(new TextPointer(left, 1), new TextPointer(right, 1)).Delete();

        Assert.Single(tb.Cells[0][0].Blocks.OfType<Paragraph>());
        Assert.Single(tb.Cells[0][1].Blocks.OfType<Paragraph>());
        Assert.Equal("A", left.Text());
        Assert.Equal("D", right.Text());
    }

    // ---- (3) TopLevelBlockOf reaches paragraphs at any depth ---------------

    [AvaloniaFact]
    public void Delete_FromANestedTableToATopLevelParagraph_RemovesTheBlocksBetween()
    {
        var inner = new TableBlock(1, 1);
        var innerPara = inner.Cells[0][0].Para;
        ((Run)innerPara.Inlines[0]).Text = "IN";
        var outer = CellWith(inner);

        var mid = NewPara("MIDDLE");
        var last = NewPara("END");
        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        doc.Blocks.Add(mid);
        doc.Blocks.Add(last);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        new TextRange(new TextPointer(innerPara, 1), new TextPointer(last, 1)).Delete();

        Assert.DoesNotContain(ed.Document!.Blocks, b => ReferenceEquals(b, mid));
    }

    // ---- (4) GetPlainText reaches paragraphs at any depth -------------------

    [AvaloniaFact]
    public void GetPlainText_IncludesNestedAndInlineTableText()
    {
        var inner = new TableBlock(1, 1);
        ((Run)inner.Cells[0][0].Para.Inlines[0]).Text = "NESTED";
        var outer = CellWith(inner);

        var host = NewPara("host");
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "INLINE";
        host.Inlines.Add(it);

        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        string text = ed.GetPlainText();
        Assert.Contains("NESTED", text);
        Assert.Contains("INLINE", text);
    }

    // ---- (low) GetImageCount reaches images at any depth --------------------

    [AvaloniaFact]
    public void GetImageCount_CountsImagesInNestedAndInlineTables()
    {
        var innerPara = new Paragraph();
        innerPara.Inlines.Add(new InlineImage());        // 1 — inside a nested table
        var inner = new TableBlock(1, 1);
        inner.Cells[0][0].Blocks.Clear();
        inner.Cells[0][0].Blocks.Add(innerPara);
        var outer = CellWith(inner);

        var host = new Paragraph();
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        it.Table.Cells[0][0].Blocks.Clear();
        it.Table.Cells[0][0].Blocks.Add(new ImageBlock()); // 2 — block image inside an inline table
        host.Inlines.Add(it);

        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        Assert.Equal(2, ed.GetImageCount());
    }
}
