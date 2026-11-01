using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Code review of src (2026-11-01): HTML attribute entities, astral characters in HTML, RTF header/footer text.
public class ReviewFixes20261101Tests
{
    private static Run FirstRun(FlowDocument doc)
        => doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => !string.IsNullOrEmpty(r.Text));

    private static string Text(FlowDocument doc)
        => string.Concat(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).Select(r => r.Text));

    // HtmlAgilityPack returns attribute text raw, so `&amp;` stayed in the link — and the writer escaped it again,
    // one more `amp;` per round trip.
    [AvaloniaFact]
    public void AnEscapedAmpersandInAHref_IsDecoded_AndStaysPutAcrossRoundTrips()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p><a href=\"https://x.com/?a=1&amp;b=2\">click</a></p>");
        Assert.Equal("https://x.com/?a=1&b=2", FirstRun(doc).NavigateUri);

        for (int i = 0; i < 2; i++) doc = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));
        Assert.Equal("https://x.com/?a=1&b=2", FirstRun(doc).NavigateUri);
    }

    // Chrome's clipboard HTML quotes a family name as &quot; inside the style attribute; the regex stopped at its `;`.
    [AvaloniaFact]
    public void AQuotedFontFamilyFromChrome_ReadsAsTheFamily()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            "<p><span style=\"font-family: &quot;Malgun Gothic&quot;, sans-serif; color: rgb(255, 0, 0)\">x</span></p>");
        Assert.Equal("Malgun Gothic", FirstRun(doc).FontFamily);
    }

    // Entitize numbered UTF-16 units: an emoji went out as two surrogate references, which other readers show as U+FFFD.
    [AvaloniaFact]
    public void AnEmoji_IsWrittenAsOneCodePointReference_AndReadBack()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "hi \U0001F600" } } });

        string html = HtmlDocumentFormatter.ToHtml(doc);
        Assert.Contains("&#128512;", html);
        Assert.DoesNotContain("&#55357;", html);
        Assert.Equal("hi \U0001F600", Text(HtmlDocumentFormatter.ParseHtml(html)));
    }

    // DeEntitize makes a char of the number, which fails past U+FFFF and leaves the reference as text.
    [AvaloniaTheory]
    [InlineData("&#128512;")]
    [InlineData("&#x1F600;")]
    public void AnAstralNumericReference_IsDecoded(string reference)
    {
        Assert.Equal("a\U0001F600b", Text(HtmlDocumentFormatter.ParseHtml($"<p>a{reference}b</p>")));
    }

    // Control symbols and \'hh were taken only in body text: escaped braces in a header vanished on our own round trip.
    [AvaloniaFact]
    public void BracesInAHeader_SurviveAnRtfRoundTrip()
    {
        var doc = new FlowDocument { PageSetup = new PageSetup { Header = "Report {draft} \\ 1" } };
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "body" } } });

        var back = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc));
        Assert.Equal("Report {draft} \\ 1", back.PageSetup?.Header);
    }

    [AvaloniaFact]
    public void CodePageBytesInAHeader_AreRead()
    {
        var doc = RtfDocumentFormatter.Parse(@"{\rtf1\ansi\ansicpg1252{\header \pard \'e9t\'e9\par}body\par}");
        Assert.Equal("été", doc.PageSetup?.Header?.Trim());
    }

    // Word writes a non-breaking hyphen as \_; it was dropped.
    [AvaloniaFact]
    public void ANonBreakingHyphen_IsKept()
    {
        Assert.Equal("e\u2011mail", Text(RtfDocumentFormatter.Parse(@"{\rtf1\ansi e\_mail\par}")));
    }

    // The RTF writer dropped a run's highlight and a link's address (a link was only \ul); backported from the WinUI port.
    [AvaloniaFact]
    public void AHighlightAndALink_SurviveAnRtfRoundTrip()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph
        {
            Inlines =
            {
                new Run { Text = "marked ", Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Colors.Yellow) },
                new Run { Text = "link", NavigateUri = "https://example.com/a?b=1" },
            }
        });

        string rtf = RtfDocumentFormatter.Write(doc);
        Assert.Contains("HYPERLINK", rtf);
        var runs = RtfDocumentFormatter.Parse(rtf).Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).ToList();

        var marked = runs.Single(r => r.Text == "marked ");
        Assert.Equal(Avalonia.Media.Colors.Yellow, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(marked.Background).Color);
        Assert.Equal("https://example.com/a?b=1", runs.Single(r => r.Text == "link").NavigateUri);
    }

    // Word's spelling of a link. A script link is dropped as the HTML reader drops it.
    [AvaloniaTheory]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("javascript:alert(1)", null)]
    public void AWordHyperlinkField_IsRead(string url, string? expected)
    {
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi before {\field{\*\fldinst{HYPERLINK """ + url + @"""}}{\fldrslt{\ul click}}} after\par}");
        var runs = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).ToList();
        Assert.Equal("before click after", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(expected, runs.Single(r => r.Text == "click").NavigateUri);
        Assert.Null(runs.First().NavigateUri);
    }

    // \fldrslt switched its group to Normal, so the result of a field inside a skipped destination came into the body.
    [AvaloniaFact]
    public void AFieldInsideASkippedDestination_StaysOutOfTheBody()
    {
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi a{\*\unknowndest {\field{\*\fldinst PAGE}{\fldrslt LEAK}}}b\par}");
        Assert.Equal("ab", Text(doc));
    }

    // The writer escapes the address (\uN, \\); the instruction reader dropped both.
    [AvaloniaTheory]
    [InlineData("https://ko.wikipedia.org/wiki/한글")]
    [InlineData(@"file:///C:\docs\a.txt")]
    public void ALinkAddressWithNonAsciiOrBackslash_SurvivesAnRtfRoundTrip(string url)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "link", NavigateUri = url } } });
        var back = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc));
        Assert.Equal(url, FirstRun(back).NavigateUri);
    }
}
