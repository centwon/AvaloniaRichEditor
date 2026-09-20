using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Page margins were two constants (48 x 40 DIP) that nothing could reach: a host could pick the paper but
// not how much of it to write on, and an RTF from Word or HWP had its margins dropped, so a document
// opened here paginated differently from what its author saw. Now they are four sides on the document's
// PageSetup, saved with it and applied on load — the same path the paper size takes.
//
// A margin is also the one page setting that can make the page unusable: negative, or two sides that add
// up past the paper. That arrives from a file (untrusted) and from a binding (XAML), so both entries are
// tested, as is the case that decides it — the editor keeps a page to write on either way.
public class PageMarginTests
{
    private static readonly PageMargins Wide = new(25, 20, 25, 20); // mm

    private static FlowDocument A4Doc(PageMargins? margin = null)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(TestHelpers.Para(new Run { Text = "page" }));
        doc.PageSetup = new PageSetup { PageSize = RichEditorPageSize.A4, Margin = margin ?? PageSetup.DefaultMargin };
        return doc;
    }

    [AvaloniaFact]
    public void TheContentColumnFollowsTheMargins()
    {
        var ed = new RichEditor { Document = A4Doc(), PageSize = RichEditorPageSize.A4 };
        double before = ed.ContentLayoutWidth;

        ed.PageMargin = Wide;

        // A4 is 794 DIP (210 mm) wide. Default 15 mm a side = 56.7 DIP: 794 - 113 = 681 before.
        // 25 mm a side = 94.5 DIP: 794 - 189 = 605 after.
        Assert.Equal(794 - 2 * 15 * PageSetup.DipsPerMm, before, 0);
        Assert.Equal(794 - 2 * 25 * PageSetup.DipsPerMm, ed.ContentLayoutWidth, 0);
    }

    [AvaloniaFact]
    public void ChangingTheMarginsRepaginates()
    {
        var blocks = Enumerable.Range(0, 40).Select(_ => (Block)TestHelpers.Para(new Run { Text = "line" })).ToArray();
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.A4 };
        ed.Measure(new Size(900, 1200));
        int before = ed.GetPrintPageCount();

        ed.PageMargin = new PageMargins(12.7, 105, 12.7, 105); // a tall band top and bottom: less page to write on
        ed.Measure(new Size(900, 1200));

        Assert.True(ed.GetPrintPageCount() > before, $"{before} pages before, {ed.GetPrintPageCount()} after");
    }

    // The margins belong to the document, like the paper: set them on the editor and a save carries them.
    [AvaloniaFact]
    public void TheMarginsAreCapturedIntoTheDocument_AndRoundTripThroughJson()
    {
        var ed = new RichEditor { Document = A4Doc(), PageSize = RichEditorPageSize.A4 };

        ed.PageMargin = Wide;

        Assert.Equal(Wide, ed.Document!.PageSetup!.Margin);
        var back = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(ed.Document));
        Assert.Equal(Wide, back.PageSetup!.Margin);
    }

    // Opening a document applies its margins, as it applies its paper.
    [AvaloniaFact]
    public void OpeningADocumentAppliesItsMargins()
    {
        var ed = new RichEditor();

        ed.Document = A4Doc(Wide);

        Assert.Equal(Wide, ed.PageMargin);
    }

    // A document that never touched the margins must keep its bytes — the whole reason the fields are
    // omitted at the default.
    [Fact]
    public void DefaultMarginsAreNotWrittenToJson()
    {
        string json = DocumentSerializer.Serialize(A4Doc());

        // Paragraphs carry margins of their own (MarginTop/MarginRight/...), so the check has to look
        // inside the page setup rather than at the whole string — the first version of this test didn't
        // and failed on a block's fields.
        var setup = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("PageSetup");
        foreach (string side in new[] { "MarginLeft", "MarginTop", "MarginRight", "MarginBottom" })
            Assert.False(setup.TryGetProperty(side, out _), $"{side} was written at its default");
    }

    // RTF carries lengths as whole twips (1/1440 inch), so a millimetre does not survive exactly: 25 mm
    // is 1417.32 twips, written as 1417 and read back as 24.994. The contract is that the page comes back
    // the same to within a twip — not that the number is identical.
    [Fact]
    public void MarginsRoundTripThroughRtf_ToWithinATwip()
    {
        var doc = A4Doc(Wide);

        var m = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc)).PageSetup!.Margin;

        const double twipMm = 25.4 / 1440;
        Assert.Equal(Wide.Left, m.Left, twipMm);
        Assert.Equal(Wide.Top, m.Top, twipMm);
        Assert.Equal(Wide.Right, m.Right, twipMm);
        Assert.Equal(Wide.Bottom, m.Bottom, twipMm);
    }

    // The point of reading them: a file from another word processor keeps its own margins. 1440 twips = 1
    // inch = 96 DIP, the Word default; 720 = half an inch. (A4 here is 11910 x 16845 twips — the paper
    // table's rounded DIPs, within the reader's 2-twip tolerance of Word's own 11906 x 16838.)
    [Fact]
    public void AnExternalRtfKeepsItsOwnMargins()
    {
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi\paperw11910\paperh16845\margl1440\margr720\margt1440\margb720 hello\par}");

        Assert.Equal(new PageMargins(25.4, 25.4, 12.7, 12.7), doc.PageSetup!.Margin);
    }

    // A file that states only some sides keeps ours for the rest, rather than falling to zero.
    [Fact]
    public void AnRtfThatStatesOneSideKeepsTheDefaultsForTheOthers()
    {
        var doc = RtfDocumentFormatter.Parse(@"{\rtf1\ansi\paperw11910\paperh16845\margl1440 hello\par}");

        var d = PageSetup.DefaultMargin;
        Assert.Equal(new PageMargins(25.4, d.Top, d.Right, d.Bottom), doc.PageSetup!.Margin);
    }

    // ---- margins that leave no page ---------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(-10.0, 10.0)]            // negative
    [InlineData(120.0, 10.0)]            // 2 x 120 mm > A4's 210 mm across
    [InlineData(12.7, 160.0)]            // 2 x 160 mm > A4's 297 mm down
    [InlineData(double.NaN, 10.0)]
    [InlineData(double.PositiveInfinity, 10.0)]
    public void AMarginThatLeavesNoPage_IsRefusedByTheProperty(double x, double y)
    {
        var ed = new RichEditor { Document = A4Doc(), PageSize = RichEditorPageSize.A4 };

        ed.PageMargin = new PageMargins(x, y, x, y);

        Assert.Equal(PageSetup.DefaultMargin, ed.PageMargin); // kept the last usable value
        Assert.True(ed.ContentLayoutWidth > 0);
    }

    [Fact]
    public void AJsonFileWithAMarginThatLeavesNoPage_FallsBackToTheDefault()
    {
        string json = DocumentSerializer.Serialize(A4Doc(PageSetup.DefaultMargin))
            .Replace("\"ShowPageNumbers\": false", "\"ShowPageNumbers\": false, \"MarginLeft\": 900");

        var doc = DocumentSerializer.Deserialize(json);

        Assert.Equal(PageSetup.DefaultMargin, doc.PageSetup!.Margin);
    }

    [Fact]
    public void AnRtfWithAMarginThatLeavesNoPage_FallsBackToTheDefault()
    {
        // 20000 twips = 353 mm, wider than A4's 210 on its own.
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi\paperw11910\paperh16845\margl20000\margr20000 hello\par}");

        Assert.Equal(PageSetup.DefaultMargin, doc.PageSetup!.Margin);
    }

    // The margins can arrive before the paper does, and a margin that fits A4 (the fallback used while the
    // paper is unknown) need not fit the A5 the file goes on to declare.
    [Fact]
    public void MarginsStatedBeforeASmallerPaper_AreRecheckedAgainstIt()
    {
        // 4400 twips = 77.6 mm a side: 155 together fits A4 (210 across), not A5 (148).
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi\margl4400\margr4400\paperw8385\paperh11910 hello\par}");

        Assert.Equal(RichEditorPageSize.A5, doc.PageSetup!.PageSize);
        Assert.Equal(PageSetup.DefaultMargin, doc.PageSetup.Margin);
    }
}
