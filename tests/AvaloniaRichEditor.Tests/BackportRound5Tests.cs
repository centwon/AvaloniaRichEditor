using System.IO;
using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Six defects found in the WinUI peer's 2026-08-07 audit and confirmed here by direct source
/// comparison — every one of them was present in this project too, word for word in places.
/// <para>Five of the six are the same shape: the model was never wrong, so a round trip through this
/// project's own reader agreed with itself and the defect only existed in what the FORMAT told an outside
/// consumer. Three of them were found by a human pasting into Word and HWP, not by a test. That is why
/// several assertions below read the WRITTEN BYTES rather than a re-parsed document — a symmetry check
/// cannot see a value that was never written.</para></summary>
public class BackportRound5Tests
{
    private static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(255, 0, 0));
    private static readonly IBrush Blue = new SolidColorBrush(Color.FromRgb(0, 0, 255));

    private static string Plain(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    private static Paragraph P(string text) => new() { Inlines = { new Run { Text = text } } };

    private static Color? ColorOf(IBrush? b) => (b as ISolidColorBrush)?.Color;

    // ---- 1. the native formats must not lose a paragraph fill inside a cell ----------------------

    // The legacy one-paragraph cell encoding shares a single Background field between the cell's fill and
    // the paragraph's, and the cell's assignment came last: with no cell fill the paragraph's was
    // overwritten with null and gone, in this editor's own save format. Both cases are pinned, because the
    // second (fills on both) still reads as "the cell kept its colour" while the paragraph's is lost.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeFormats_KeepAParagraphFillInsideACell(bool cellFilledToo)
    {
        FlowDocument Build()
        {
            var doc = new FlowDocument();
            var tb = new TableBlock(1, 1);
            var cell = tb.Cells[0][0];
            cell.Blocks.Clear();
            cell.Blocks.Add(new Paragraph { Background = Red, Inlines = { new Run { Text = "filled" } } });
            if (cellFilledToo) cell.Background = Blue;
            doc.Blocks.Add(tb);
            return doc;
        }

        static Paragraph FirstCellParagraph(FlowDocument d)
            => Assert.IsType<Paragraph>(Assert.IsType<TableBlock>(d.Blocks[0]).Cells[0][0].Blocks[0]);

        var viaJson = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Build()));
        Assert.Equal(Colors.Red, ColorOf(FirstCellParagraph(viaJson).Background));

        using var ms = new MemoryStream();
        DocumentPackage.Save(Build(), ms);
        ms.Position = 0;
        var viaFlow = DocumentPackage.Load(ms);
        Assert.Equal(Colors.Red, ColorOf(FirstCellParagraph(viaFlow).Background));

        // The cell's own fill still round-trips — the two are separate values, not one.
        var cellAfter = Assert.IsType<TableBlock>(viaFlow.Blocks[0]).Cells[0][0];
        Assert.Equal(cellFilledToo ? Colors.Blue : (Color?)null, ColorOf(cellAfter.Background));
    }

    // ---- 2. HTML: a cell's paragraphs keep their own formatting, and stay separate ----------------

    // Cell paragraphs went out as BARE INLINES separated by <br>, and bare inlines carry nothing
    // paragraph-level — so a bulleted, centred, indented, shaded or heading cell paragraph lost all of it
    // on export, including into the clipboard's HTML flavour. The READER could always do it: foreign Word
    // tables with bulleted cells parse correctly, so this was the writer alone.
    [Fact]
    public void Html_RoundTrips_ParagraphFormatting_InsideACell()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 2);
        var c0 = tb.Cells[0][0];
        c0.Blocks.Clear();
        c0.Blocks.Add(new Paragraph { ListType = ListKind.Bullet, ListMarker = ListMarkerStyle.Square, Inlines = { new Run { Text = "one" } } });
        c0.Blocks.Add(new Paragraph { ListType = ListKind.Bullet, ListMarker = ListMarkerStyle.Square, ListLevel = 1, Inlines = { new Run { Text = "two" } } });
        var c1 = tb.Cells[0][1];
        c1.Blocks.Clear();
        c1.Blocks.Add(new Paragraph { HeadingLevel = 2, TextAlignment = TextAlignment.Center, Inlines = { new Run { Text = "head" } } });
        c1.Blocks.Add(new Paragraph { Indent = 40, Background = Red, Inlines = { new Run { Text = "body" } } });
        doc.Blocks.Add(tb);

        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var t = Assert.IsType<TableBlock>(back.Blocks.Single(b => b is TableBlock));

        var a = t.Cells[0][0].Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(2, a.Count);
        Assert.All(a, x => Assert.Equal(ListKind.Bullet, x.ListType));
        Assert.All(a, x => Assert.Equal(ListMarkerStyle.Square, x.ListMarker));
        Assert.Equal(0, a[0].ListLevel);
        Assert.Equal(1, a[1].ListLevel);

        var b = t.Cells[0][1].Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(2, b.Count);
        Assert.Equal(2, b[0].HeadingLevel);
        Assert.Equal(TextAlignment.Center, b[0].TextAlignment);
        Assert.Equal(40, b[1].Indent);
        Assert.Equal(Colors.Red, ColorOf(b[1].Background));
    }

    // <br> is NOT a paragraph boundary to the reader, so two plain paragraphs in one cell collapsed into
    // one on every cycle. The collapse is idempotent — cycle 2 matches cycle 1 — which is exactly why a
    // round-trip test could not see it. Two cycles here for the same reason.
    [Fact]
    public void Html_TwoPlainParagraphsInACell_DoNotCollapse()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(P("first"));
        cell.Blocks.Add(P("second"));
        doc.Blocks.Add(tb);

        var cur = doc;
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            cur = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(cur));
            var paras = Assert.IsType<TableBlock>(cur.Blocks.Single(x => x is TableBlock))
                              .Cells[0][0].Blocks.OfType<Paragraph>().ToList();
            Assert.Equal(2, paras.Count);
            Assert.Equal("first", Plain(paras[0]));
            Assert.Equal("second", Plain(paras[1]));
        }
    }

    // ---- 3. HTML: a paragraph that holds a picture keeps its own formatting ----------------------

    // The walker's "recurse into anything containing block-or-media" branch ran before the one that reads
    // a paragraph element's style, so an <img> anywhere in the paragraph turned <p style="…"> into a mere
    // container and every paragraph-level value was dropped — a captioned picture lost its centring on
    // every HTML load, at the top level as much as in a cell.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Html_ParagraphHoldingAnImage_KeepsItsOwnFormatting(bool inCell)
    {
        const string img = "<img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==\" width=\"20\" height=\"20\"/>";
        const string para = "<p style=\"text-align:center;margin-left:40px;\">caption" + img + "</p>";
        string html = inCell ? $"<table><tr><td>{para}</td></tr></table>" : para;

        var doc = HtmlDocumentFormatter.ParseHtml(html);
        var p = inCell
            ? Assert.IsType<TableBlock>(doc.Blocks.Single(b => b is TableBlock)).Cells[0][0].Blocks.OfType<Paragraph>().First()
            : doc.Blocks.OfType<Paragraph>().First();

        Assert.Equal(TextAlignment.Center, p.TextAlignment);
        Assert.Equal(40, p.Indent);
        Assert.Contains("caption", Plain(p));
    }

    // The narrowing that keeps that fix from changing foreign HTML: a <div> wrapping real block children
    // is still walked as a container, and its styling does NOT descend onto them.
    [Fact]
    public void Html_ContainerWrappingBlocks_DoesNotPushItsStyleOntoThem()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            "<div style=\"text-align:center;margin-left:60px\"><p>a</p><p>b</p></div>");
        var ps = doc.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(2, ps.Count);
        Assert.All(ps, p => Assert.Equal(TextAlignment.Left, p.TextAlignment));
        Assert.All(ps, p => Assert.Equal(0, p.Indent));
    }

    // ---- 4. HTML: paragraph spacing --------------------------------------------------------------

    // The three margins went out as nothing at all, so every save reset them to the defaults. They now go
    // out twice — as real CSS for other consumers, and as a marker because only the marker is read back.
    [Fact]
    public void Html_RoundTrips_ParagraphMargins_ButIgnoresForeignOnes()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { MarginTop = 25, MarginBottom = 30, MarginRight = 15, Inlines = { new Run { Text = "spaced" } } });

        string html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.Contains("margin-top:25px", html);   // a browser/Word sees real spacing
        Assert.Contains("data-are-m=", html);       // and the reader sees the marker

        var p = HtmlDocumentFormatter.ParseHtml(html).Blocks.OfType<Paragraph>().First();
        Assert.Equal(25, p.MarginTop);
        Assert.Equal(30, p.MarginBottom);
        Assert.Equal(15, p.MarginRight);

        // Foreign margins are deliberately NOT read: doing so would give every web paste that page's
        // vertical rhythm. Same reason data-are-empty exists.
        var foreign = HtmlDocumentFormatter.ParseHtml("<p style=\"margin-top:99px;margin-bottom:99px\">x</p>")
                                           .Blocks.OfType<Paragraph>().First();
        Assert.Equal(0, foreign.MarginTop);
    }

    // ---- 5. RTF: a list marker is structure, not text --------------------------------------------

    // RTF has no list element, so the marker went out as BARE TEXT + \tab — a trade-off the comment there
    // called deliberate ("our parser treats it as text"). It was not only cosmetic: the glyph came back as
    // CONTENT. A bulleted item reopened as the plain text "•\t항목", the list gone and the bullet now part
    // of what the user typed, and saving again kept it there.
    //
    // No round-trip test could see it, and that is the point: the result is PERFECTLY IDEMPOTENT, because
    // the marker is only written for a paragraph that still has a ListType and this one no longer does.
    // Three cycles here for the same reason — the corruption to guard against is stable, not accumulating.
    [Theory]
    [InlineData(ListKind.Bullet, ListMarkerStyle.Default)]
    [InlineData(ListKind.Bullet, ListMarkerStyle.Square)]
    [InlineData(ListKind.Ordered, ListMarkerStyle.Default)]
    [InlineData(ListKind.Ordered, ListMarkerStyle.LowerRoman)]
    [InlineData(ListKind.Ordered, ListMarkerStyle.DecimalParen)]
    public void Rtf_ListMarker_IsStructure_NotText(ListKind kind, ListMarkerStyle marker)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { ListType = kind, ListMarker = marker, Inlines = { new Run { Text = "item" } } });
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Blocks.Clear();
        tb.Cells[0][0].Blocks.Add(new Paragraph { ListType = kind, ListMarker = marker, Inlines = { new Run { Text = "cell item" } } });
        doc.Blocks.Add(tb);

        // The marker must ALSO still be literal text in the output, or readers that do not implement RTF
        // lists show no marker at all. That is not hypothetical: the WinUI peer tried the standard
        // {\pntext …}{\*\pn …} pair and HWP — which skips both — lost every bullet and number in a real
        // paste. The tag makes the text structure to US; it must stay text to everyone else.
        string rtf = RtfDocumentFormatter.Write(doc);
        Assert.Contains(kind == ListKind.Bullet ? @"{\*\armkb" : @"{\*\armkn", rtf);

        var cur = doc;
        for (int cycle = 1; cycle <= 3; cycle++)
        {
            cur = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(cur));
            var top = cur.Blocks.OfType<Paragraph>().First(p => Plain(p).Contains("item"));
            var cell = Assert.IsType<TableBlock>(cur.Blocks.Single(b => b is TableBlock))
                             .Cells[0][0].Blocks.OfType<Paragraph>().First();

            foreach (var (p, want) in new[] { (top, "item"), (cell, "cell item") })
            {
                Assert.Equal(want, Plain(p));      // the text is the text: no glyph, no tab
                Assert.Equal(kind, p.ListType);    // and the list is still a list
            }
            if (kind == ListKind.Ordered) Assert.Equal(marker, top.ListMarker);
        }
    }

    // A list item needs a real hanging indent, not a marker plus a bare \tab: without one the tab lands on
    // the reader's next DEFAULT tab stop, which in HWP is far to the right — the marker sat alone at the
    // margin and the text was thrown across the line.
    //
    // The gutter goes INTO \li, so the level tag lets the reader subtract it back out. Two cycles, because
    // getting that wrong makes the indent GROW by the gutter every time, which one cycle would not show.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 40)]
    [InlineData(2, 20)]
    public void Rtf_ListItem_HasAHangingIndent_AndKeepsTheAuthorsOwn(int level, double indent)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph
        {
            ListType = ListKind.Bullet, ListLevel = level, Indent = indent,
            Inlines = { new Run { Text = "item" } },
        });

        string rtf = RtfDocumentFormatter.Write(doc);
        int gutter = 720 * (level + 1);
        Assert.Contains(@"\fi-360", rtf);
        Assert.Contains($@"\li{(int)(indent * 15) + gutter}", rtf);
        Assert.Contains($@"\tx{gutter}", rtf);

        var cur = doc;
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            cur = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(cur));
            var p = cur.Blocks.OfType<Paragraph>().First();
            Assert.Equal(ListKind.Bullet, p.ListType);
            Assert.Equal(level, p.ListLevel);   // RTF has no standard place for this; the tag carries it
            Assert.Equal("item", Plain(p));
            // The gutter must never LEAK into the indent. This reader does not parse \li at all — a
            // long-standing limitation, so the author's own indent does not come back either — and the
            // subtraction in StartMarkerText is what keeps the two consistent if it ever starts to.
            Assert.Equal(0, p.Indent);
        }
    }

    // ---- 6. RTF: a cell paragraph states its own properties --------------------------------------

    // The cell writer opened every cell with `\pard\intbl` and never wrote the paragraph's properties, so
    // a centred or indented paragraph inside a table exported as neither — while the identical paragraph
    // at the top level exported correctly. Asserted on the WRITTEN bytes, because this reader does not
    // parse \qc or \li at all: a round trip would agree with itself while Word and HWP saw nothing.
    [Fact]
    public void Rtf_CellParagraph_StatesItsOwnAlignmentAndIndent()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Blocks.Clear();
        tb.Cells[0][0].Blocks.Add(new Paragraph { TextAlignment = TextAlignment.Center, Inlines = { new Run { Text = "centred" } } });
        tb.Cells[0][1].Blocks.Clear();
        tb.Cells[0][1].Blocks.Add(new Paragraph { TextAlignment = TextAlignment.Right, Indent = 40, Inlines = { new Run { Text = "right" } } });
        doc.Blocks.Add(tb);

        string rtf = RtfDocumentFormatter.Write(doc);
        Assert.Contains(@"\intbl \qc", rtf);       // the cell paragraph's own alignment, right after \intbl
        Assert.Contains(@"\qr\li600", rtf);        // and its own indent (40px * 15 = 600 twips)

        // No stray delimiter space became content — the trap that bit the peer twice while doing this.
        var t = Assert.IsType<TableBlock>(RtfDocumentFormatter.Parse(rtf).Blocks.Single(b => b is TableBlock));
        Assert.Equal("centred", Plain(t.Cells[0][0].Blocks.OfType<Paragraph>().First()));
        Assert.Equal("right", Plain(t.Cells[0][1].Blocks.OfType<Paragraph>().First()));
    }

    // Every paragraph states its alignment explicitly, including \ql for left. In the spec \pard resets to
    // left, but HWP treats \pard as "back to the current defaults" and keeps a previously seen \qr — so
    // one right-aligned paragraph turned every following one right-aligned on paste.
    [Fact]
    public void Rtf_LeftAlignment_IsStatedExplicitly()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { TextAlignment = TextAlignment.Right, Inlines = { new Run { Text = "right" } } });
        doc.Blocks.Add(P("back to left"));

        Assert.Contains(@"\pard\ql", RtfDocumentFormatter.Write(doc));
    }
}
