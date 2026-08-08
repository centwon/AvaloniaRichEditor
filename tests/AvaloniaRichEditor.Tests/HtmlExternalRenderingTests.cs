using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Two defects found by opening this exporter's HTML in a BROWSER, which is the only way they
/// could be found: both round-tripped through this project's own reader perfectly, and it is the external
/// rendering that was wrong. The assertions here therefore read the WRITTEN MARKUP, not just the model —
/// a symmetry check cannot see a line break that was never written.</summary>
public class HtmlExternalRenderingTests
{
    // ---- 1. An author's blank line was invisible outside this editor ------------------------------

    // The blank line went out as `<p data-are-empty="1"></p>`. The marker told THIS reader about it, but
    // an element with no content has zero height, so a browser showed nothing: measured, the gap across
    // the blank line was the same 16px as between any two adjacent paragraphs. Paragraph spacing already
    // follows the both-ways rule (real CSS *and* a marker); the blank line only had the marker.
    [Fact]
    public void AnAuthorsBlankLine_IsGivenALineForOutsideRenderers()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "위" } } });
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "아래" } } });

        string html = HtmlDocumentFormatter.ToHtml(doc);

        int at = html.IndexOf("data-are-empty");
        Assert.True(at >= 0, "the blank line must still be marked for this reader");
        // The <br> has to be INSIDE the marked element — that is what gives it height.
        string marked = html.Substring(at, html.IndexOf("</p>", at) - at);
        Assert.Contains("<br>", marked);
    }

    // The <br> is the blank line's rendering, not its content. Read it back as content and every
    // save/load doubles the gap — the same accumulating shape as the RTF footer separator and the blank
    // paragraph under an image. Two cycles, because one looked fine in both of those cases too.
    [Fact]
    public void AnAuthorsBlankLine_DoesNotGrowOnRepeatedRoundTrips()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "위" } } });
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "아래" } } });

        var once = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));

        foreach (var round in new[] { once, twice })
        {
            Assert.Equal(3, round.Blocks.Count);
            var blank = (Paragraph)round.Blocks[1];
            // Not just "no text": a <br> read as content leaves an inline holding "\n", which renders as
            // a SECOND line inside the one blank paragraph.
            Assert.Empty(blank.Inlines);
            Assert.Equal("", blank.Text());
        }
    }

    // Foreign EMPTY elements must keep being dropped — web pages are full of `<p>`/`<div>` used for
    // spacing, and honouring them gives every paste that page's vertical rhythm. Only the marker opts in.
    [Fact]
    public void AForeignEmptyParagraph_IsStillDropped()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p>a</p><p></p><p>b</p>");
        Assert.Equal(2, doc.Blocks.Count);
    }

    // A foreign `<p><br></p>` is NOT empty and never was dropped — it is what every contenteditable
    // editor writes for a blank line, and it arrives as one. Pinned because it is the reason the new
    // `<br>` is safe: a consumer that strips data- attributes still gets the blank line, and this reader
    // only needs the marker to know the `<br>` is not content of its own.
    [Fact]
    public void AForeignBrOnlyParagraph_IsAStillABlankLine()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p>a</p><p><br></p><p>b</p>");
        Assert.Equal(3, doc.Blocks.Count);
        Assert.Equal("", ((Paragraph)doc.Blocks[1]).Text().Trim());
    }

    // ---- 2. A picture in a cell sat beside the text instead of under it ---------------------------

    // A cell's paragraph keeps the bare-inline form when the bare form can represent it, and the rule
    // counted PARAGRAPHS to decide. A cell holding one paragraph and a block image has only one, so the
    // paragraph went out bare — and `<img>` is inline, so the picture landed on the text's line.
    // `<hr>` and `<table>` are block-level and break the line themselves; only an image forces this.
    [AvaloniaFact]
    public void APictureInACell_StartsItsOwnLineInsteadOfJoiningTheText()
    {
        var cellTable = new TableBlock(1, 1);
        var cell = cellTable.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "캡션" } } });
        var pic = new ImageBlock { Width = 80, Height = 50 };
        pic.SetImageData(OnePixelPng, "image/png");
        cell.Blocks.Add(pic);

        var doc = new FlowDocument();
        doc.Blocks.Add(cellTable);
        string html = HtmlDocumentFormatter.ToHtml(doc);

        // The caption must be a block element, which is what puts the picture on the next line.
        int td = html.IndexOf("<td");
        string cellHtml = html.Substring(td, html.IndexOf("</td>", td) - td);
        Assert.Contains("<p", cellHtml);
        Assert.True(cellHtml.IndexOf("<p") < cellHtml.IndexOf("<img"),
                    "the caption's element must come before the picture");
    }

    // The promotion must not change what comes back: still a paragraph and an image, still two blocks.
    [AvaloniaFact]
    public void APictureInACell_StillRoundTripsAsTwoBlocks()
    {
        var cellTable = new TableBlock(1, 1);
        var cell = cellTable.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "캡션" } } });
        var pic = new ImageBlock { Width = 80, Height = 50 };
        pic.SetImageData(OnePixelPng, "image/png");
        cell.Blocks.Add(pic);

        var doc = new FlowDocument();
        doc.Blocks.Add(cellTable);

        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var backCell = back.Blocks.OfType<TableBlock>().Single().Cells[0][0];

        Assert.Equal("캡션", backCell.Blocks.OfType<Paragraph>().First().Text());
        Assert.Single(backCell.Blocks.OfType<ImageBlock>());
    }

    // A plain one-paragraph cell with nothing else keeps the bare form — the reason the rule exists, and
    // the whitespace behaviour earned there depends on those exact bytes.
    [Fact]
    public void APlainCell_KeepsItsBareForm()
    {
        var t = new TableBlock(1, 1);
        t.Cells[0][0].Para.Inlines[0] = new Run { Text = "평문" };
        var doc = new FlowDocument();
        doc.Blocks.Add(t);

        string html = HtmlDocumentFormatter.ToHtml(doc);
        int td = html.IndexOf("<td");
        string cellHtml = html.Substring(td, html.IndexOf("</td>", td) - td);
        Assert.DoesNotContain("<p", cellHtml);
    }

    // 1x1 transparent PNG.
    private static readonly byte[] OnePixelPng = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
