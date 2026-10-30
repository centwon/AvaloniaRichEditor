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
        => doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text?.Contains("click") == true);

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
    // An empty list item. The writer marks it data-are-empty as it marks a blank paragraph, but the list
    // reader dropped every empty item regardless — a blank numbered item vanished on the first round trip,
    // and the items either side could then merge into one list and lose a marker on the second (the port's
    // fuzz, seed 8178 at 20000 seeds, 2026-09-24). Twice, as round trips are run here.
    [AvaloniaFact]
    public void AnEmptyListItem_RoundTrips_ButAForeignEmptyItemIsStillDropped()
    {
        static Paragraph Item(string? text)
        {
            var p = new Paragraph { ListType = ListKind.Ordered };
            if (text != null) p.Inlines.Add(new Run { Text = text });
            return p;
        }
        var doc = new FlowDocument();
        doc.Blocks.Add(Item("a"));
        doc.Blocks.Add(Item(null));
        doc.Blocks.Add(Item("b"));

        var once = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));

        foreach (var back in new[] { once, twice })
        {
            var items = back.Blocks.OfType<Paragraph>().Where(p => p.ListType == ListKind.Ordered).ToList();
            Assert.Equal(3, items.Count);
            Assert.DoesNotContain(items[1].Inlines.OfType<Run>(), r => !string.IsNullOrEmpty(r.Text));
        }

        var foreign = HtmlDocumentFormatter.ParseHtml("<ol><li>a</li><li></li><li>b</li></ol>");
        Assert.Equal(2, foreign.Blocks.OfType<Paragraph>().Count(p => p.ListType == ListKind.Ordered));
    }

    // Text that LOOKS like markup went out as markup: the writer's entitizer left & < > alone, so typed
    // "<img src=x onerror=…>" reached exported and clipboard HTML as a live tag, and came back as nothing
    // (found reviewing a host's table payloads, 2026-10-31). In a paragraph and in a cell, twice round.
    [AvaloniaFact]
    public void TextThatLooksLikeMarkup_IsWrittenAsText()
    {
        const string text = "<img src=x onerror=alert(1)> & <b>co</b> &amp;";
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Blocks.Clear();
        tb.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = text } } });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = text } } });
        doc.Blocks.Add(tb);

        string html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<b>co", html);

        var once = HtmlDocumentFormatter.ParseHtml(html);
        var twice = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(once));
        foreach (var back in new[] { once, twice })
        {
            static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)).Replace(' ', ' ');
            Assert.Equal(text, Text((Paragraph)back.Blocks[0]));
            var cell = (Paragraph)back.Blocks.OfType<TableBlock>().Single().Cells[0][0].Blocks[0];
            Assert.Equal(text, Text(cell));
        }
    }

    // A cell's lone formatted run went out bare (<td><b>x</b></td>), and the reader's block walk handed the
    // <b> itself to the inline parser — which reads only an element's CHILDREN, so the <b>'s own bold was
    // never applied. The same for i/u/s and a styled span. Our own round trip lost a bold cell.
    [AvaloniaFact]
    public void AFormattedRunAloneInACell_KeepsItsFormatting()
    {
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Blocks.Clear();
        tb.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "bold", FontWeight = Avalonia.Media.FontWeight.Bold } } });
        tb.Cells[0][1].Blocks.Clear();
        tb.Cells[0][1].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "red", Foreground = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Colors.Red) } } });
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);

        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc)).Blocks.OfType<TableBlock>().Single();
        var bold = ((Paragraph)back.Cells[0][0].Blocks[0]).Inlines.OfType<Run>().Single();
        Assert.Equal(Avalonia.Media.FontWeight.Bold, bold.FontWeight);
        var red = ((Paragraph)back.Cells[0][1].Blocks[0]).Inlines.OfType<Run>().Single();
        Assert.Equal(Avalonia.Media.Colors.Red, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(red.Foreground).Color);

        // Foreign HTML of the same shape: an <i> straight under <td>.
        var foreign = HtmlDocumentFormatter.ParseHtml("<table><tr><td><i>it</i> plain</td></tr></table>").Blocks.OfType<TableBlock>().Single();
        var runs = ((Paragraph)foreign.Cells[0][0].Blocks[0]).Inlines.OfType<Run>().ToList();
        Assert.Equal(Avalonia.Media.FontStyle.Italic, runs.First(r => r.Text == "it").FontStyle);
        Assert.Equal(Avalonia.Media.FontStyle.Normal, runs.Last().FontStyle);
    }
}
