using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Two defects from the WinUI peer's 2026-08-26 audit round that reproduced here, both
/// confirmed by RUNNING them rather than by reading the source.
/// <para>That distinction earned its place in this round. A third candidate — Backspace/Delete
/// splitting a UTF-16 surrogate pair — was reported as missing here on the strength of a grep for
/// <c>IsSurrogatePair</c>, which finds nothing in this project. It is handled all the same, by
/// <c>PrevCharBoundary</c>/<c>NextCharBoundary</c>, at all four sites. Nothing to port; the grep just
/// asked for the wrong token.</para></summary>
public class BackportRound6Tests
{
    private static string PlainText(FlowDocument doc)
        => string.Concat(doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<Run>().Select(r => r.Text)));

    // A control word's parameter is an optional '-' followed by DIGITS. The reader consumed a '-' with
    // no digit after it as though it were one, so the character disappeared: `\fs-x hello` came out as
    // "x hello". The document is brace-balanced and complete, and Word reads it as `\fs` followed by the
    // literal text "-x hello".
    [AvaloniaFact]
    public void RtfControlWord_BareMinusIsLiteralText_NotAParameter()
    {
        Assert.Equal("-x hello", PlainText(RtfDocumentFormatter.Parse(@"{\rtf1\ansi\fs-x hello}")));

        // The control: a real negative parameter is still consumed as one (\li-720 is a hanging indent,
        // not the text "-720").
        Assert.Equal("x", PlainText(RtfDocumentFormatter.Parse(@"{\rtf1\ansi\li-720 x}")));
    }

    // CSS allows a channel to be a percentage, and browsers are not the only source of pasted HTML.
    // The regex took `\d+` only, so `rgb(100%, 0%, 0%)` failed the match and the colour was dropped.
    [AvaloniaFact]
    public void CssColour_AcceptsPercentageChannels()
    {
        Assert.Equal(Colour("rgb(255, 0, 0)"), Colour("rgb(100%, 0%, 0%)"));
        Assert.Equal(Colour("rgb(0, 0, 0)"), Colour("rgb(0%, 0%, 0%)"));
    }

    // `\d+` puts no ceiling on the digit run, and the channels were int.Parse'd: an out-of-range value
    // threw OverflowException out of ParseCssColor, out of the walk, and out of ParseHtml itself. A
    // malformed colour in pasted HTML must cost that colour, not the paste.
    [AvaloniaFact]
    public void CssColour_OutOfRangeChannel_DoesNotThrow()
    {
        var ex = Record.Exception(() => Colour("rgb(99999999999, 0, 0)"));
        Assert.Null(ex);
        Assert.Equal(Colour("rgb(255, 0, 0)"), Colour("rgb(99999999999, 0, 0)")); // clamped, as CSS does

        // Not a number at all: the colour is dropped, and still nothing throws.
        Assert.Null(Record.Exception(() => Colour("rgb(., 0, 0)")));
    }

    // The foreground of the first run of HTML that paints one colour, or null when it was dropped.
    private static string? Colour(string css)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><span style=\"color: {css}\">x</span></p>");
        return doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<Run>())
            .FirstOrDefault()?.Foreground?.ToString();
    }
}
