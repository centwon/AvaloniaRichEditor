using System;
using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round 4 of the two-way convergence with the WinUI port (WinUIRichEditor). Every defect below was found
// there by widening a model-level round-trip fuzz — 24 seeds to 400, then to 20000 — and every one of
// them was confirmed present in THIS source before being fixed here. None is a porting artefact.
//
// Two of them were found only after that fuzz was taught to generate images and dividers at all: the
// generator never produced them, so the whole axis read as covered and was not.
public class Round4BackportTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFElEQVR4nGP8//8/AzJgYkAD5AsAAP//A+8DTgn2rL0AAAAASUVORK5CYII=");

    private static Paragraph P(string text)
    {
        var p = new Paragraph();
        if (text.Length > 0) p.Inlines.Add(new Run { Text = text });
        return p;
    }

    private static Paragraph Bullet(string text, int level)
    {
        var p = new Paragraph { ListType = ListKind.Bullet, ListLevel = level };
        p.Inlines.Add(new Run { Text = text });
        return p;
    }

    private static ImageBlock Img(double w = 120, double h = 80)
    {
        var ib = new ImageBlock { Width = w, Height = h };
        ib.SetImageData(TinyPng, "image/png", null);
        return ib;
    }

    private static string Plain(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    private static string Shape(FlowDocument d)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in d.Blocks)
            switch (b)
            {
                case Paragraph p:
                    sb.Append("P[").Append(string.Concat(p.Inlines.Select(i => i is Run r ? r.Text : "<IMG>"))).Append(']');
                    break;
                case ImageBlock: sb.Append("IMGBLK"); break;
                case DividerBlock: sb.Append("HR"); break;
                case TableBlock t: sb.Append($"T{t.Rows}x{t.Columns}"); break;
            }
        return sb.ToString();
    }

    // ---- the merge grid --------------------------------------------------------------------------

    // A merge that cuts across an existing one used to overwrite the anchor without releasing the cells
    // that anchor owned. Those cells stayed flagged covered with nothing covering them: LogicalCells()
    // skips them for good, and enough of them leave a table with NO logical cells at all. The range is
    // now expanded to contain every merge it touches, the way a spreadsheet does it.
    // The exact shape the port's fuzz reduced to: a second merge that RE-ANCHORS a cell which already
    // owns a wider span. Overwriting (0,0)'s span left (0,1)..(0,3) flagged covered by an anchor that no
    // longer reaches them — AnchorOf then resolves each to ITSELF, the signature of an orphan.
    [Fact]
    public void MergeCells_ShrinkingAnExistingAnchor_LeavesNoUnreachableCells()
    {
        var tb = new TableBlock(4, 4);
        tb.MergeCells(0, 0, 0, 3);   // a wide anchor across the top row
        tb.MergeCells(0, 0, 1, 0);   // re-anchors (0,0) as a TALL, narrow merge

        AssertNoOrphans(tb);
        Assert.NotEmpty(tb.LogicalCells());
        // Every slot is reachable: the anchors plus what they cover must account for the whole grid.
        int covered = tb.LogicalCells().Sum(x => { var (cs, rs) = tb.SpanOf(x.r, x.c); return cs * rs; });
        Assert.Equal(tb.Rows * tb.Columns, covered);
    }

    [Fact]
    public void MergeCells_TouchingAMerge_ExpandsToContainItWhole()
    {
        var tb = new TableBlock(3, 3);
        tb.MergeCells(0, 0, 1, 1);        // a 2x2 block
        tb.MergeCells(1, 1, 2, 2);        // overlaps its bottom-right corner

        AssertNoOrphans(tb);
        // The result must still be one rectangle: the two ranges together span the whole grid.
        var (r, c, _) = Assert.Single(tb.LogicalCells());
        Assert.Equal((0, 0), (r, c));
        Assert.Equal((3, 3), tb.SpanOf(0, 0));
    }

    [Fact]
    public void MergeCells_RepeatedOnTheSameRange_IsStable()
    {
        var tb = new TableBlock(2, 2);
        tb.MergeCells(0, 0, 1, 1);
        var before = tb.LogicalCells().Count();
        tb.MergeCells(0, 0, 1, 1);
        Assert.Equal(before, tb.LogicalCells().Count());
        AssertNoOrphans(tb);
    }

    private static void AssertNoOrphans(TableBlock tb)
    {
        for (int r = 0; r < tb.Rows; r++)
            for (int c = 0; c < tb.Columns; c++)
            {
                if (!tb.IsCovered(r, c)) continue;
                var (ar, ac) = tb.AnchorOf(r, c);
                Assert.False(ar == r && ac == c, $"covered slot ({r},{c}) is its own anchor");
                Assert.False(tb.IsCovered(ar, ac), $"covered slot ({r},{c}) resolves to a covered cell");
                var (acs, ars) = tb.SpanOf(ar, ac);
                Assert.True(r >= ar && r < ar + ars && c >= ac && c < ac + acs,
                    $"covered slot ({r},{c}) claims an anchor whose span does not reach it");
            }
    }

    // ---- HTML ------------------------------------------------------------------------------------

    // A sub-bullet must stay UNDER the item it belongs to. The writer emits a deeper level as a <ul>
    // that is a SIBLING of the previous <li>, so the reader has to keep <li> and <ul> in document order.
    [Fact]
    public void Html_RoundTrips_NestedListItems_InDocumentOrder()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(Bullet("A", 0));
        doc.Blocks.Add(Bullet("B", 1));
        doc.Blocks.Add(Bullet("C", 0));

        var items = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))
            .Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(new[] { "A", "B", "C" }, items.Select(Plain));
        Assert.Equal(new[] { 0, 1, 0 }, items.Select(p => p.ListLevel));
    }

    // The shape only-<li> iteration never reached: a document whose ONLY list item is indented, so the
    // export nests <ul><ul><li> with no <li> at the outer level. Its items used to VANISH, and when they
    // were the whole document the parse produced zero blocks and the raw-text fallback dumped the file.
    [Fact]
    public void Html_RoundTrips_ListWhoseOnlyItemIsIndented()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(Bullet("deep", 2));

        var item = Assert.Single(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))
            .Blocks.OfType<Paragraph>());
        Assert.Equal("deep", Plain(item));
        Assert.Equal(2, item.ListLevel);
        Assert.Equal(ListKind.Bullet, item.ListType);
    }

    // Saving an empty document and reopening it put the editor's own tags on screen as body text: the
    // export is `<p style="…"></p>`, the walk yields no block, and the "input was not markup" fallback
    // dumped the source. Plain text must still come through that fallback.
    [Fact]
    public void Html_EmptyDocument_RoundTripsEmpty_NotAsItsOwnMarkup()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { HeadingLevel = 4, Indent = 40 });

        string text = string.Concat(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))
            .Blocks.OfType<Paragraph>().Select(Plain));
        Assert.DoesNotContain("<", text);
        Assert.Equal("", text);
    }

    [Fact]
    public void Html_PlainTextInput_StillBecomesItsText()
    {
        var back = HtmlDocumentFormatter.ParseHtml("just some text");
        Assert.Equal("just some text", string.Concat(back.Blocks.OfType<Paragraph>().Select(Plain)));
    }

    // Whitespace BETWEEN inline siblings is a word separator and has to survive; the same whitespace
    // before the closing tag is padding and must NOT become content. Fixing either alone breaks the
    // other — hence the deferred separator.
    [Theory]
    [InlineData("<span>a</span> <span>b</span>", "a b")]
    [InlineData("<span>a</span>\n<span>b</span>", "a b")]
    [InlineData("<span>a</span> ", "a")]
    [InlineData("<span>a</span> <span></span>", "a")]
    public void Html_WhitespaceBetweenInlineSiblings_IsASeparatorButNeverTrailingContent(string html, string expected)
    {
        var back = HtmlDocumentFormatter.ParseHtml(html);
        Assert.Equal(expected, string.Concat(back.Blocks.OfType<Paragraph>().Select(Plain)));
    }

    // MergeCells joins a covered cell's text with a space; the writer emits that as whitespace between
    // two <span>s, and the block walk used to drop it — so a merged cell lost the word boundary on the
    // SECOND round trip (the first still had one run).
    [Fact]
    public void Html_RoundTrips_MergedCellText_KeepsItsWordBoundary()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 2);
        foreach (var (r, c, cell) in tb.LogicalCells())
            ((Run)cell.Para.Inlines[0]).Text = $"c{r}{c}";
        tb.MergeCells(0, 0, 0, 1);
        doc.Blocks.Add(tb);

        var once = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));

        static string CellText(FlowDocument d) => string.Concat(
            d.Blocks.OfType<TableBlock>().SelectMany(t => t.LogicalCells())
             .SelectMany(x => x.cell.Blocks.OfType<Paragraph>()).Select(Plain));

        Assert.Equal("c00 c01", CellText(once));
        Assert.Equal(CellText(once), CellText(twice));
    }

    // HTML folds a run of whitespace to one space, so the editor's own consecutive spaces were gone on
    // the first save/load. They ride out as alternating space/&nbsp;, which is what Word emits.
    [Theory]
    [InlineData("a  b")]
    [InlineData("a   b")]
    [InlineData("a     b")]
    [InlineData("trailing  ")]
    [InlineData("a  b  c")]
    public void Html_RoundTrips_ConsecutiveSpaces(string text)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(P(text));

        var once = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        Assert.Equal(text, string.Concat(once.Blocks.OfType<Paragraph>().Select(Plain)));
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));
        Assert.Equal(text, string.Concat(twice.Blocks.OfType<Paragraph>().Select(Plain)));
    }

    // A run made only of spaces is authored content — MergeCells' join separator is exactly this shape.
    [Theory]
    [InlineData(true)]   // the space run OPENS the paragraph
    [InlineData(false)]  // it follows a run that already ends in a space
    public void Html_RoundTrips_ARunOfNothingButSpaces(bool atStart)
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        if (!atStart) p.Inlines.Add(new Run { Text = "before " });
        p.Inlines.Add(new Run { Text = " " });
        p.Inlines.Add(new Run { Text = "after", Foreground = Brushes.Red });
        doc.Blocks.Add(p);

        string expected = (atStart ? "" : "before ") + " after";
        var once = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        Assert.Equal(expected, string.Concat(once.Blocks.OfType<Paragraph>().Select(Plain)));
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));
        Assert.Equal(expected, string.Concat(twice.Blocks.OfType<Paragraph>().Select(Plain)));
    }

    // A text node of nothing but &nbsp; is content, not layout whitespace. Regex \s (and
    // IsNullOrWhiteSpace) both count U+00A0 as whitespace, which folded the very character that was
    // there to survive folding.
    [Fact]
    public void Html_ForeignNbsp_IsContent_NotACollapsibleSeparator()
    {
        var back = HtmlDocumentFormatter.ParseHtml("<p>a<span>&nbsp;&nbsp;</span>b</p>");
        Assert.Equal("a  b", string.Concat(back.Blocks.OfType<Paragraph>().Select(Plain)));
    }

    // A link keeps the colour the DOCUMENT gave it; a foreign page's anchor colour still yields to the
    // blue rule, which exists to stop dark/white site anchors disappearing in this editor.
    [Fact]
    public void Html_RoundTrips_HyperlinkColour_ButStillOverridesForeignAnchors()
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "link", NavigateUri = "https://example.com/1", Foreground = Brushes.Orange });
        doc.Blocks.Add(p);

        var run = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))
            .Blocks.OfType<Paragraph>().SelectMany(x => x.Inlines.OfType<Run>()).First(r => r.Text == "link");
        Assert.Equal(Brushes.Orange.Color, ((ISolidColorBrush)run.Foreground!).Color);
        Assert.Equal("https://example.com/1", run.NavigateUri);

        var fr = HtmlDocumentFormatter.ParseHtml(
                "<p><a href=\"https://example.com/2\"><span style=\"color:#FFFFFF\">btn</span></a></p>")
            .Blocks.OfType<Paragraph>().SelectMany(x => x.Inlines.OfType<Run>()).First(r => r.Text == "btn");
        Assert.Equal(Brushes.Blue.Color, ((ISolidColorBrush)fr.Foreground!).Color);
    }

    // A paragraph can be a list item AND a heading; <li> wins the tag, so the level rides a marker.
    [Fact]
    public void Html_RoundTrips_HeadingLevel_OnAListItem()
    {
        var doc = new FlowDocument();
        var p = Bullet("heading item", 1);
        p.HeadingLevel = 2;
        doc.Blocks.Add(p);

        var item = Assert.Single(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))
            .Blocks.OfType<Paragraph>());
        Assert.Equal(2, item.HeadingLevel);
        Assert.Equal(1, item.ListLevel);
        Assert.Equal("heading item", Plain(item));
    }

    // A blank line is content; a foreign page's empty <p>/<div> is layout scaffolding. Without the
    // marker, keeping one means adding blank lines to every web paste.
    [Fact]
    public void Html_RoundTrips_BlankParagraph_ButStillDropsForeignEmptyElements()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(P("a"));
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(P("b"));

        Assert.Equal("P[a]P[]P[b]", Shape(HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc))));
        Assert.Equal("P[a]P[b]", Shape(HtmlDocumentFormatter.ParseHtml("<p>a</p><p></p><div>  </div><p>b</p>")));
    }

    // A <p> holding nothing but an image is walked as a block (an <img> is block-or-media), so there is
    // no pending paragraph on import and the image used to rejoin the PRECEDING one.
    // [AvaloniaFact]: the HTML importer keeps an <img> only once it DECODES, and a Bitmap needs the
    // platform render interface — without it the image is dropped and the test passes for the wrong
    // reason. (The RTF path stores the bytes and does not need one.)
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Html_ImageAloneInItsParagraph_KeepsItsOwnLine()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(P("above"));
        var p = new Paragraph();
        var im = new InlineImage { Width = 20, Height = 20 };
        im.SetImageData(TinyPng, "image/png", null);
        p.Inlines.Add(im);
        doc.Blocks.Add(p);
        doc.Blocks.Add(P("below"));

        var once = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));
        Assert.Equal("P[above]P[<IMG>]P[below]", Shape(once));
        Assert.Equal("P[above]P[<IMG>]P[below]", Shape(twice));
    }

    // ---- RTF -------------------------------------------------------------------------------------

    // RTF spells a block picture as `\pard <pict>\par`. Reading that \par as content added a blank
    // paragraph under every image — and another on the next cycle, so the gap kept widening.
    [Fact]
    public void Rtf_BlockImage_DoesNotGrowABlankParagraph()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(P("a"));
        doc.Blocks.Add(Img());
        doc.Blocks.Add(P("b"));

        var once = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc));
        var twice = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(once));
        Assert.Equal("P[a]IMGBLKP[b]", Shape(once));
        Assert.Equal("P[a]IMGBLKP[b]", Shape(twice));
    }

    // A blank line the author typed still has to survive next to an image: the writer gives it its own
    // \pard\par, so the first \par is consumed as the picture's terminator and the second one lands.
    [Fact]
    public void Rtf_BlankLineAfterAnImage_Survives()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(Img());
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(P("b"));

        Assert.Equal("IMGBLKP[]P[b]", Shape(RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc))));
    }

    // A table is only pending rows until FinalizeTable runs, so a picture after `\row` was appended
    // first and jumped ahead of the table it followed.
    [Fact]
    public void Rtf_ImageAfterATable_StaysAfterIt()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new TableBlock(1, 1));
        doc.Blocks.Add(Img());
        doc.Blocks.Add(P("b"));

        Assert.Equal("T1x1IMGBLKP[b]", Shape(RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc))));
    }

    // RTF has no rule control word; Word (and this writer) spell one as an empty paragraph with a bottom
    // border. Only the writing half existed, so every divider came back as a blank line.
    [Fact]
    public void Rtf_RoundTrips_Dividers()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(P("a"));
        doc.Blocks.Add(new DividerBlock());
        doc.Blocks.Add(new DividerBlock());
        doc.Blocks.Add(P("b"));

        Assert.Equal("P[a]HRHRP[b]", Shape(RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc))));
    }
}
