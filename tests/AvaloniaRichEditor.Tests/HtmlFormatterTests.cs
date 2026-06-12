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
    [Fact]
    public void ParseHtml_SplitsInlineFormatting()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p>Hello <b>world</b></p>");
        var p = Assert.IsType<Paragraph>(doc.Blocks[0]);

        Assert.Equal("Hello world", p.Text());
        var bold = p.Inlines.OfType<Run>().Single(r => r.Text == "world");
        Assert.Equal(FontWeight.Bold, bold.FontWeight);
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
    public void ParseHtml_BuildsTable()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            "<table><tr><td>A</td><td>B</td></tr></table>");
        var tb = doc.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(2, tb.Columns);
        Assert.Equal("A", tb.Cells[0][0].Text());
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
    public void RoundTrip_PreservesList()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<ul><li>one</li><li>two</li></ul>");
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.ListType == ListKind.Bullet);
        Assert.Contains("<ul>", HtmlDocumentFormatter.ToHtml(doc));
    }
}
