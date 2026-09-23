using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of HtmlDocumentFormatter.cs (2026-09-23).
public class HtmlAuditTests
{
    // A pasted page's script link was kept as the run's address and written back out as an <a href> — into
    // exported HTML and the clipboard HTML other applications receive. The editor never launches it (only
    // http/https open), but it laundered the link into its own output.
    [AvaloniaTheory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("java\tscript:alert(1)")] // browsers drop tabs and newlines inside the scheme
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void AScriptLink_IsNotCarriedIntoTheDocument(string href)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><a href=\"{href.Replace("<", "&lt;").Replace(">", "&gt;")}\">click</a></p>");

        var run = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text == "click");
        Assert.Null(run.NavigateUri);
    }

    // A file: image on ANOTHER machine (file://host/share/x.png) is a UNC path, and on Windows File.Exists on
    // it opens an SMB connection to that host — which offers the user's NTLM credentials to it. Pasting HTML is
    // enough to trigger it, and a page controls the <img src> its copied selection carries. Local file images
    // are a host setting (AllowLocalFileImages); a remote share is never one. 192.0.2.1 is TEST-NET-1:
    // unroutable, so an attempt shows up as the connect timeout (measured before the fix: see the assert).
    [AvaloniaFact]
    public void AnImageOnANetworkShare_IsNeverOpened()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var doc = HtmlDocumentFormatter.ParseHtml("<p>x<img src=\"file://192.0.2.1/share/pic.png\" width=\"100\" height=\"100\"/></p>",
            allowLocalFileImages: true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"the parse waited {sw.ElapsedMilliseconds} ms on the network share");
        Assert.DoesNotContain(doc.Blocks, b => b is ImageBlock);
    }

    // Control: web and mail links stay.
    [AvaloniaTheory]
    [InlineData("https://example.com/a?b=1")]
    [InlineData("http://example.com")]
    [InlineData("mailto:someone@example.com")]
    public void AWebLink_IsKept(string href)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><a href=\"{href}\">click</a></p>");

        var run = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text == "click");
        Assert.Equal(href, run.NavigateUri);
    }

    // ---- the same rule on the other ways in (port audit, 2026-09-24) ----------------------------------------
    // The HTML reader drops script links, but a JSON/.flow file carried them in untouched and the HTML writer
    // sent them back out. A host's SetHyperlink reaches the writer too, so the writer is the backstop. (This
    // RTF reader does not read HYPERLINK fields; the port's does, and filters them the same way.)

    private static FlowDocument Linked(string href)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "click", NavigateUri = href } } });
        return doc;
    }

    private static Run Clicked(FlowDocument doc)
        => doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text.Contains("click"));

    [AvaloniaTheory]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    public void AScriptLinkInAJsonFile_IsNotCarriedIntoTheDocument(string href)
    {
        var doc = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Linked(href)));

        Assert.Null(Clicked(doc).NavigateUri);
    }

    [AvaloniaFact]
    public void AScriptLinkSetByTheHost_IsNotWrittenToHtml()
    {
        string html = HtmlDocumentFormatter.ToHtml(Linked("javascript:alert(1)"));

        Assert.DoesNotContain("javascript", html, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("click", html);
    }

    // The other half, so the guards cannot pass by dropping every link.
    [AvaloniaFact]
    public void AWebLink_SurvivesJsonAndTheHtmlWriter()
    {
        const string url = "https://example.com/a?b=1";

        Assert.Equal(url, Clicked(DocumentSerializer.Deserialize(DocumentSerializer.Serialize(Linked(url)))).NavigateUri);
        Assert.Contains("href=\"https://example.com/a?b=1\"", HtmlDocumentFormatter.ToHtml(Linked(url)).Replace("&amp;", "&"));
    }
}
