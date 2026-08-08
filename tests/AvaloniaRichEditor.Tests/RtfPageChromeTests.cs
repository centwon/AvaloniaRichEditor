using System.Linq;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Header / footer / page numbers through RTF — both directions. Backported from the WinUI peer,
/// where this was found; the defect was present here identically.
/// <para>The model has carried these since page setup existed and JSON/.flow persist them, but the RTF
/// writer never wrote them and the reader had no case for the destinations. That is two defects: exporting
/// to Word silently dropped the header, and opening a Word document INSERTED its header into the body as
/// the document's first paragraph.</para></summary>
public class RtfPageChromeTests
{
    private static string Plain(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    private static FlowDocument Doc(PageSetup ps, string body = "본문")
    {
        var d = new FlowDocument { PageSetup = ps };
        d.Blocks.Add(new Paragraph { Inlines = { new Run { Text = body } } });
        return d;
    }

    [Fact]
    public void WordHeaderAndFooter_DoNotLandInTheBody()
    {
        const string wordish = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Arial;}}
{\header\pard\qc My Header\par}
{\footer\pard\qc Page \chpgn\par}
\pard\sectd Body text here.\par}";

        var doc = RtfDocumentFormatter.Parse(wordish);

        var paras = doc.Blocks.OfType<Paragraph>().Where(p => Plain(p).Length > 0).ToList();
        Assert.Single(paras);
        Assert.Equal("Body text here.", Plain(paras[0]));

        Assert.NotNull(doc.PageSetup);
        Assert.Equal("My Header", doc.PageSetup!.Header);
        Assert.Equal("Page", doc.PageSetup.Footer);   // the \chpgn decoration is not text
        Assert.True(doc.PageSetup.ShowPageNumbers);
    }

    [Theory]
    [InlineData("headerl")]
    [InlineData("headerr")]
    [InlineData("headerf")]
    public void HeaderVariants_AreAllRead(string word)
    {
        var doc = RtfDocumentFormatter.Parse($@"{{\rtf1\ansi{{\{word}\pard Variant\par}}\pard Body\par}}");
        Assert.Equal("Variant", doc.PageSetup?.Header);
        Assert.DoesNotContain("Variant", string.Concat(doc.Blocks.OfType<Paragraph>().Select(Plain)));
    }

    [Fact]
    public void Rtf_CarriesHeaderFooterAndPageNumbers()
    {
        // ASCII body on purpose: the ordering assertion searches for it literally, and non-ASCII text goes
        // out as \uN escapes.
        string rtf = RtfDocumentFormatter.Write(Doc(new PageSetup
        {
            PageSize = RichEditorPageSize.A4,
            Header = "Report",
            Footer = "Confidential",
            ShowPageNumbers = true,
        }, body: "BODYTEXT"));

        Assert.Contains(@"{\header", rtf);
        Assert.Contains("Report", rtf);
        Assert.Contains(@"{\footer", rtf);
        Assert.Contains("Confidential", rtf);
        // The page number sits at an explicit RIGHT tab stop on the content edge, which is where the editor
        // draws it. A4 = 794 DIPs wide, minus two 48-DIP margins, times 15 twips = 10470.
        Assert.Contains(@"\tqr\tx10470", rtf);
        Assert.Contains(@"\chpgn", rtf);

        int header = rtf.IndexOf(@"{\header", System.StringComparison.Ordinal);
        int body = rtf.IndexOf("BODYTEXT", System.StringComparison.Ordinal);
        Assert.True(header >= 0 && body > header, $"header at {header}, body at {body}");
    }

    [Fact]
    public void Rtf_OmitsThemWhenTheDocumentHasNone()
    {
        Assert.DoesNotContain(@"{\header", RtfDocumentFormatter.Write(Doc(new PageSetup())));

        var d = new FlowDocument();
        d.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "x" } } });
        Assert.DoesNotContain(@"{\header", RtfDocumentFormatter.Write(d)); // PageSetup == null
    }

    // TWO cycles, because the peer's first version leaked the page-number decoration into the footer text:
    // the writer follows \chpgn with " / " and a NUMPAGES field, and collecting that made the separator
    // ACCUMULATE — "꼬리말/" after one cycle, "꼬리말//" after two. Cycle 1 alone looked fine.
    [Theory]
    [InlineData("머리말 텍스트", "꼬리말", true)]
    [InlineData("Header only", null, false)]
    [InlineData(null, "Footer only", false)]
    [InlineData(null, null, true)]
    public void Rtf_RoundTripsPageChrome_Twice(string? header, string? footer, bool numbers)
    {
        var cur = Doc(new PageSetup
        {
            PageSize = RichEditorPageSize.A4, Header = header, Footer = footer, ShowPageNumbers = numbers,
        });

        for (int cycle = 1; cycle <= 2; cycle++)
        {
            cur = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(cur));
            Assert.Equal(header, cur.PageSetup?.Header);
            Assert.Equal(footer, cur.PageSetup?.Footer);
            Assert.Equal(numbers, cur.PageSetup?.ShowPageNumbers ?? false);
            Assert.Equal("본문", string.Concat(cur.Blocks.OfType<Paragraph>().Select(Plain)));
        }
    }

    // ---- paper size ----------------------------------------------------------------------------

    // Round 6 wrote the header/footer half of the page setup and left the paper out entirely: nothing
    // emitted \paperw/\paperh, so a document set to A4 arrived on whatever paper the reader defaults to
    // (Letter in a US install) and came back from our OWN reader as Continuous. The chrome assertions
    // above could not see it — they only ever looked at the header and footer.
    [Theory]
    [InlineData(RichEditorPageSize.A4, RichEditorPageOrientation.Portrait)]
    [InlineData(RichEditorPageSize.A4, RichEditorPageOrientation.Landscape)]
    [InlineData(RichEditorPageSize.Letter, RichEditorPageOrientation.Portrait)]
    [InlineData(RichEditorPageSize.B5, RichEditorPageOrientation.Landscape)]
    public void Rtf_RoundTripsThePaperSize(RichEditorPageSize size, RichEditorPageOrientation orientation)
    {
        var cur = Doc(new PageSetup { PageSize = size, Orientation = orientation });

        for (int cycle = 1; cycle <= 2; cycle++)
        {
            cur = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(cur));
            Assert.Equal(size, cur.PageSetup?.PageSize);
            Assert.Equal(orientation, cur.PageSetup?.Orientation);
        }
    }

    // The paper goes out at BOTH levels. Word reads the document-level pair and HWP reads only the
    // section-level one — an A4 document opened in HWP as Letter until \pgwsxn/\pghsxn were there
    // (found by a human opening the generated file, since the Word harness was happy).
    [Fact]
    public void ThePaperIsStatedAtDocumentAndSectionLevel()
    {
        string rtf = RtfDocumentFormatter.Write(Doc(new PageSetup
        {
            PageSize = RichEditorPageSize.A4, Orientation = RichEditorPageOrientation.Landscape,
        }));

        Assert.Contains(@"\paperw", rtf);
        Assert.Contains(@"\pgwsxn", rtf);
        Assert.Contains(@"\landscape", rtf);
        Assert.Contains(@"\lndscpsxn", rtf);
    }

    // A file that states ONLY the section-level paper (HWP's own output) has to arrive complete.
    [Fact]
    public void ASectionOnlyPaper_IsStillRead()
    {
        var doc = RtfDocumentFormatter.Parse(@"{\rtf1\ansi\sectd\pgwsxn11910\pghsxn16845 x\par}");
        Assert.Equal(RichEditorPageSize.A4, doc.PageSetup?.PageSize);
    }

    // Continuous is "no paper": it states nothing and leaves the reader on its own default.
    [Fact]
    public void AContinuousDocument_StatesNoPaper()
    {
        string rtf = RtfDocumentFormatter.Write(Doc(new PageSetup { PageSize = RichEditorPageSize.Continuous }));
        Assert.DoesNotContain(@"\paperw", rtf);
        Assert.DoesNotContain(@"\pgwsxn", rtf);
        Assert.DoesNotContain(@"\landscape", rtf);
    }

    // A paper this control has no name for must not be forced onto the nearest one — an unmatched size
    // leaves PageSize alone rather than guessing, the same rule foreign margins follow. The second half
    // keeps that honest: a size it DOES know is matched from the very same shape, so the first assertion
    // is about the size being unknown and not about \paperw being ignored altogether.
    [Fact]
    public void AnUnknownPaper_LeavesThePageSizeAlone()
    {
        var unknown = RtfDocumentFormatter.Parse(@"{\rtf1\ansi\paperw9999\paperh12345 x\par}");
        Assert.Equal("x", string.Concat(unknown.Blocks.OfType<Paragraph>().Select(Plain)));
        Assert.True(unknown.PageSetup == null || unknown.PageSetup.PageSize == RichEditorPageSize.Continuous);

        var a4 = RtfDocumentFormatter.Parse(@"{\rtf1\ansi\paperw11910\paperh16845 x\par}");
        Assert.Equal(RichEditorPageSize.A4, a4.PageSetup?.PageSize);
    }
}
