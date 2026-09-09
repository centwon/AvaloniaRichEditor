using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Cell vertical alignment, backported from the WinUI peer.
/// <para>A cell's content sat at the top and nothing could move it. The property itself is small; what
/// is not small is that EIGHT walks place cell content — the render walk, the two hit-test walks, the
/// link lookups, at top level and inside nested and inline tables — and they all used a hardcoded
/// <c>rect.Y + 5</c>. They now share one <c>CellContentOffsetY</c>, because if the draw walk offsets
/// content and a hit-test walk does not, the text is drawn in one place and clicked in another.</para>
/// <para>That agreement is what the geometry test below is for; the format tests cover the round trips.</para>
/// </summary>
public class CellVerticalAlignmentTests
{
    private static Paragraph P(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        return p;
    }

    // One row, one column, a tall row and a short line: the slack is what alignment moves the content
    // through, so there has to be some.
    private static (FlowDocument doc, TableBlock tb) TallCell(CellVerticalAlignment va)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph());
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 300;
        tb.RowHeights.Clear();
        tb.RowHeights.Add(200);
        tb.Cells[0][0].Blocks.Clear();
        // TWO paragraphs: a click anywhere in a single-paragraph cell resolves to that paragraph by
        // fallback, so paragraph identity alone cannot tell where the content actually sits — the
        // boundary BETWEEN two paragraphs can.
        tb.Cells[0][0].Blocks.Add(P("first line"));
        tb.Cells[0][0].Blocks.Add(P("second line"));
        tb.Cells[0][0].VerticalAlignment = va;
        doc.Blocks.Add(tb);
        doc.Blocks.Add(new Paragraph());
        return (doc, tb);
    }

    private static TableBlock FirstTable(FlowDocument d) => d.Blocks.OfType<TableBlock>().First();

    private static CellVerticalAlignment VAlignOf(FlowDocument d) =>
        FirstTable(d).Cells[0][0].VerticalAlignment;

    // ---- the model ------------------------------------------------------------------------------

    [Fact]
    public void TopIsTheDefault()
    {
        Assert.Equal(CellVerticalAlignment.Top, new TableCell().VerticalAlignment);
    }

    // An undo state is a clone; an alignment the clone drops would revert at the first Ctrl+Z.
    [Fact]
    public void CloneCarriesTheAlignment()
    {
        var cell = new TableCell { VerticalAlignment = CellVerticalAlignment.Bottom };

        Assert.Equal(CellVerticalAlignment.Bottom, ((TableCell)cell.Clone()).VerticalAlignment);
    }

    // ---- the formats ----------------------------------------------------------------------------

    [Theory]
    [InlineData(CellVerticalAlignment.Center)]
    [InlineData(CellVerticalAlignment.Bottom)]
    public void JsonRoundTripsTheAlignment(CellVerticalAlignment va)
    {
        var (doc, _) = TallCell(va);

        Assert.Equal(va, VAlignOf(DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc))));
    }

    [Fact]
    public void FlowPackageRoundTripsTheAlignment()
    {
        var (doc, _) = TallCell(CellVerticalAlignment.Center);
        using var ms = new MemoryStream();
        DocumentPackage.Save(doc, ms);
        ms.Position = 0;

        Assert.Equal(CellVerticalAlignment.Center, VAlignOf(DocumentPackage.Load(ms)));
    }

    // The default must not appear in the file, so documents written before this feature stay identical.
    [Fact]
    public void TopIsNotWritten()
    {
        var (doc, _) = TallCell(CellVerticalAlignment.Top);

        Assert.DoesNotContain("VAlign", DocumentSerializer.Serialize(doc));
        Assert.DoesNotContain("valign", HtmlDocumentFormatter.ToHtml(doc));
        Assert.DoesNotContain("clvertal", RtfDocumentFormatter.Write(doc));
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Center, "middle")]
    [InlineData(CellVerticalAlignment.Bottom, "bottom")]
    public void HtmlWritesTheValignAttribute(CellVerticalAlignment va, string expected)
    {
        var (doc, _) = TallCell(va);

        Assert.Contains($"valign=\"{expected}\"", HtmlDocumentFormatter.ToHtml(doc));
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Center)]
    [InlineData(CellVerticalAlignment.Bottom)]
    public void HtmlRoundTripsTheAlignment(CellVerticalAlignment va)
    {
        var (doc, _) = TallCell(va);

        Assert.Equal(va, VAlignOf(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))));
    }

    // Foreign HTML writes it both ways, so both are read.
    [Theory]
    [InlineData("<td valign=\"middle\">x</td>", CellVerticalAlignment.Center)]
    [InlineData("<td valign=\"bottom\">x</td>", CellVerticalAlignment.Bottom)]
    [InlineData("<td style=\"vertical-align:middle\">x</td>", CellVerticalAlignment.Center)]
    [InlineData("<td style=\"vertical-align:bottom\">x</td>", CellVerticalAlignment.Bottom)]
    [InlineData("<td style=\"vertical-align:baseline\">x</td>", CellVerticalAlignment.Top)] // unknown = default
    [InlineData("<td>x</td>", CellVerticalAlignment.Top)]
    public void ForeignHtmlIsRead(string td, CellVerticalAlignment expected)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<table><tr>{td}</tr></table>");

        Assert.Equal(expected, VAlignOf(doc));
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Center, @"\clvertalc")]
    [InlineData(CellVerticalAlignment.Bottom, @"\clvertalb")]
    public void RtfWritesTheControlWord(CellVerticalAlignment va, string word)
    {
        var (doc, _) = TallCell(va);

        Assert.Contains(word, RtfDocumentFormatter.Write(doc));
    }

    [Theory]
    [InlineData(CellVerticalAlignment.Center)]
    [InlineData(CellVerticalAlignment.Bottom)]
    public void RtfRoundTripsTheAlignment(CellVerticalAlignment va)
    {
        var (doc, _) = TallCell(va);

        Assert.Equal(va, VAlignOf(RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc))));
    }

    // ---- draw and hit-test have to agree ---------------------------------------------------------

    // The one that matters, and the reason the offset lives in ONE helper: if the render walk moves a
    // cell's content but a hit-test walk keeps the old top-aligned arithmetic, both still "work" — on
    // different geometry — and the caret lands where the text is not.
    //
    // No absolute coordinates: the same document is rendered at each alignment and what is compared is
    // the y at which clicking stops finding the cell's FIRST paragraph and starts finding its second.
    // That boundary is where the content actually sits. A hit-test that ignored the alignment reports
    // the same boundary every time, which is what this fails on.
    //
    // ⚠ Paragraph identity alone would NOT do: a click anywhere in a one-paragraph cell resolves to that
    // paragraph by fallback, so the first version of this test measured the same band (26..234) for both
    // alignments and could not have failed. Same family as the caret round's AdjacentTopLevelParagraph
    // trap — the fallback answers correctly for the wrong reason.
    [AvaloniaFact]
    public void TheParagraphBoundaryFollowsTheContentDown()
    {
        double top = SecondParagraphStartsAt(CellVerticalAlignment.Top);
        double bottom = SecondParagraphStartsAt(CellVerticalAlignment.Bottom);

        Assert.True(top > 0, "the cell's second paragraph was never hit when top-aligned");
        Assert.True(bottom > 0, "the cell's second paragraph was never hit when bottom-aligned");
        Assert.True(bottom > top + 50,
            $"bottom-aligned content did not move down: boundary {top} -> {bottom}");
    }

    // Centre lands between the two — a single-sided assertion would pass on an editor that only ever
    // pushed content to the bottom.
    [AvaloniaFact]
    public void CentreLandsBetweenTopAndBottom()
    {
        double top = SecondParagraphStartsAt(CellVerticalAlignment.Top);
        double middle = SecondParagraphStartsAt(CellVerticalAlignment.Center);
        double bottom = SecondParagraphStartsAt(CellVerticalAlignment.Bottom);

        Assert.True(middle > top && middle < bottom,
            $"centre ({middle}) is not between top ({top}) and bottom ({bottom})");
    }

    private const System.Reflection.BindingFlags NP =
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    // Renders the fixture at the given alignment and returns the lowest y at which a click lands in the
    // cell's SECOND paragraph — i.e. where the first paragraph's line ends and the second begins.
    private static double SecondParagraphStartsAt(CellVerticalAlignment va)
    {
        var (doc, tb) = TallCell(va);
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Size(600, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, 600, ed.DesiredSize.Height));
        using (var rtb = new RenderTargetBitmap(new PixelSize(600, (int)System.Math.Max(1, ed.DesiredSize.Height))))
            rtb.Render(ed);

        var second = (Paragraph)tb.Cells[0][0].Blocks[1];
        var hit = typeof(RichEditor).GetMethod("GetPositionFromPoint", NP)!;
        for (double y = 0; y < ed.DesiredSize.Height; y += 2)
            if ((hit.Invoke(ed, new object?[] { new Point(40.0, y) }) as TextPointer)?.Paragraph == second)
                return y;
        return -1;
    }
}
