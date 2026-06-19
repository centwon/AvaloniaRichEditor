using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class HtmlFormatterTests
{
    // ParseHtmlAsync (issue #1 pattern, B2): remote images download off the UI thread, but for
    // network-free input it must build exactly what the synchronous ParseHtml does.
    [Fact]
    public async System.Threading.Tasks.Task ParseHtmlAsync_TextOnly_MatchesSync()
    {
        const string html = "<p>Hello <b>world</b></p><p>second</p>";
        var sync = HtmlDocumentFormatter.ParseHtml(html);
        var async = await HtmlDocumentFormatter.ParseHtmlAsync(html);
        Assert.Equal(sync.Blocks.Count, async.Blocks.Count);
        Assert.Equal(((Paragraph)sync.Blocks[0]).Text(), ((Paragraph)async.Blocks[0]).Text());
        Assert.Equal(((Paragraph)sync.Blocks[1]).Text(), ((Paragraph)async.Blocks[1]).Text());
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task ParseHtmlAsync_DataImage_BuildsSameAsSync()
    {
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        string html = $"<p>icon <img src=\"{png}\"></p>";
        var sync = HtmlDocumentFormatter.ParseHtml(html);
        var async = await HtmlDocumentFormatter.ParseHtmlAsync(html);
        int syncImages = sync.Blocks.OfType<Paragraph>().Sum(p => p.Inlines.OfType<InlineImage>().Count());
        int asyncImages = async.Blocks.OfType<Paragraph>().Sum(p => p.Inlines.OfType<InlineImage>().Count());
        Assert.Equal(1, syncImages);          // data: image (1×1 < icon size) lands inline
        Assert.Equal(syncImages, asyncImages); // async path produces the same structure
    }

    [Fact]
    public void ParseHtml_SplitsInlineFormatting()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p>Hello <b>world</b></p>");
        var p = Assert.IsType<Paragraph>(doc.Blocks[0]);

        Assert.Equal("Hello world", p.Text());
        var bold = p.Inlines.OfType<Run>().Single(r => r.Text == "world");
        Assert.Equal(FontWeight.Bold, bold.FontWeight);
    }

    // Regression: HTML-imported text without an explicit font-size must use the 10pt body default, not
    // the stale 14 (px-era) that ParseInlines defaulted to — a table cell is the common trigger.
    [Fact]
    public void ParseHtml_TableCellText_UsesBodyFontSize()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<table><tr><td>cell</td></tr></table>");
        var tb = doc.Blocks.OfType<TableBlock>().Single();
        var run = tb.Cells[0][0].Para.Inlines.OfType<Run>().Single(r => r.Text == "cell");
        Assert.Equal(10, run.FontSize);
    }

    [Fact]
    public void ParseHtml_InlineWrappedText_UsesBodyFontSize()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<span>hi</span>");
        var run = doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<Run>()).Single(r => r.Text == "hi");
        Assert.Equal(10, run.FontSize);
    }

    [Fact]
    public void ToHtml_EmitsBoldTag()
    {
        var html = HtmlDocumentFormatter.ToHtml(
            HtmlDocumentFormatter.ParseHtml("<p><b>hi</b></p>"));
        Assert.Contains("<b>", html);
    }

    [Fact]
    public void RoundTrip_PreservesBold()
    {
        var once = HtmlDocumentFormatter.ParseHtml("<p>a <b>b</b></p>");
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));
        var p = Assert.IsType<Paragraph>(twice.Blocks[0]);
        var bold = p.Inlines.OfType<Run>().Single(r => r.Text == "b");
        Assert.Equal(FontWeight.Bold, bold.FontWeight);
    }

    [Fact]
    public void RoundTrip_PreservesJustifyAlignment()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p style=\"text-align:justify\">hello</p>");
        Assert.Equal(TextAlignment.Justify, ((Paragraph)doc.Blocks[0]).TextAlignment);

        var html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.Contains("justify", html);
        var rt = HtmlDocumentFormatter.ParseHtml(html);
        Assert.Equal(TextAlignment.Justify, ((Paragraph)rt.Blocks[0]).TextAlignment);
    }

    [Fact]
    public void ParseHtml_BuildsTable()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            "<table><tr><td>A</td><td>B</td></tr></table>");
        var tb = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(2, tb.Columns);
        Assert.Equal("A", tb.Cells[0][0].Para.Text());
    }

    [AvaloniaFact] // needs the Avalonia session: the allowed path decodes the image bitmap
    public void ParseHtml_FileImage_RespectsAllowLocalFileImages()
    {
        // Minimal valid 1×1 PNG.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var path = Path.Combine(Path.GetTempPath(), "are_filegate_" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, png);
        try
        {
            // Uri builds the platform-correct file URL ("file:///C:/…" on Windows, "file:///tmp/…"
            // on Unix — hand-concatenating "file:///" + path doubles the leading slash there).
            string html = $"<p>x</p><img src=\"{new Uri(path).AbsoluteUri}\" width=\"100\" height=\"100\">";

            var allowed = HtmlDocumentFormatter.ParseHtml(html, allowLocalFileImages: true);
            Assert.Contains(allowed.Blocks, b => b is ImageBlock);

            var blocked = HtmlDocumentFormatter.ParseHtml(html, allowLocalFileImages: false);
            Assert.DoesNotContain(blocked.Blocks, b => b is ImageBlock);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ToHtml_EmitsClipboardFriendlyMarkup()
    {
        // Word/HWP clipboard import drops CSS text-decoration and px font-size, and renders <ol> as
        // bullets without an explicit list-style. So strike/underline go out as <s>/<u> tags, sizes in
        // pt, and ordered lists carry list-style-type:decimal.
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run
        {
            Text = "x",
            FontSize = 20,
            TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } }
        });
        doc.Blocks.Add(p);
        var ol = new Paragraph { ListType = ListKind.Ordered };
        ol.Inlines.Add(new Run { Text = "one" });
        doc.Blocks.Add(ol);

        var html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.Contains("<s>", html);
        Assert.DoesNotContain("text-decoration", html);
        Assert.Contains("20pt", html);                   // model pt emitted directly
        Assert.DoesNotContain("font-size:20px", html);
        Assert.Contains("list-style-type:decimal", html);

        // Round-trips back: <s> -> strikethrough, pt -> pt, <ol> -> ordered list.
        var rt = HtmlDocumentFormatter.ParseHtml(html);
        Assert.Contains(rt.Blocks.OfType<Paragraph>(),
            q => q.Inlines.OfType<Run>().Any(run => run.TextDecorations != null
                && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough)));
        Assert.Contains(rt.Blocks.OfType<Paragraph>(), q => q.ListType == ListKind.Ordered);
    }

    [Fact]
    public void RoundTrip_PreservesList()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<ul><li>one</li><li>two</li></ul>");
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.ListType == ListKind.Bullet);
        Assert.Contains("<ul", HtmlDocumentFormatter.ToHtml(doc)); // may carry a list-style-type attribute
    }
}
