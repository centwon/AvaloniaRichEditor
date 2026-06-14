using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// P-milestone Phase 1: ComputePageBreaks. Empty paragraphs with explicit LineHeight and zeroed
// margins make break positions exact arithmetic (no text shaping involved); the shaped-text tests
// assert structural invariants instead of absolute positions.
public class PaginationTests
{
    private static Paragraph EmptyPara(double lineHeight) => new()
    {
        LineHeight = lineHeight,
        MarginTop = 0,
        MarginBottom = 0
    };

    private static RichEditor EditorWith(params Block[] blocks)
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        ed.Document = doc;
        return ed;
    }

    [AvaloniaFact]
    public void ShortDocument_IsSinglePage()
    {
        var ed = EditorWith(EmptyPara(50), EmptyPara(50));
        var breaks = ed.ComputePageBreaks(698, 1000);
        Assert.Equal(new List<double> { 0 }, breaks);
    }

    [AvaloniaFact]
    public void EmptyParagraphs_BreakAtExactAtomBoundaries()
    {
        // 10 atoms of exactly 50px, page holds 4 → pages start at 0, 200, 400.
        var ed = EditorWith(Enumerable.Range(0, 10).Select(_ => (Block)EmptyPara(50)).ToArray());
        var breaks = ed.ComputePageBreaks(698, 200);
        Assert.Equal(3, breaks.Count);
        Assert.Equal(0, breaks[0], 3);
        Assert.Equal(200, breaks[1], 3);
        Assert.Equal(400, breaks[2], 3);
    }

    [AvaloniaFact]
    public void Image_TooTallForRemainder_IsPushedToNextPage()
    {
        // 150px of text, then a 100px image: 150+100 > 200 → the image starts page 2 at y=150.
        var img = new ImageBlock { Width = 100, Height = 100, MarginTop = 0, MarginBottom = 0 };
        var ed = EditorWith(EmptyPara(150), img);
        var breaks = ed.ComputePageBreaks(698, 200);
        Assert.Equal(2, breaks.Count);
        Assert.Equal(150, breaks[1], 3);
    }

    [AvaloniaFact]
    public void OversizedAtom_GetsOwnPage_AndOverflows()
    {
        // A 500px image on a 200px page: it gets its own page (break at its top), overflows it,
        // and the following paragraph starts the next page right after the image.
        var img = new ImageBlock { Width = 100, Height = 500, MarginTop = 0, MarginBottom = 0 };
        var ed = EditorWith(EmptyPara(50), img, EmptyPara(50));
        var breaks = ed.ComputePageBreaks(698, 200);
        Assert.Equal(new List<double> { 0, 50, 550 }, breaks.Select(b => System.Math.Round(b, 3)).ToList());
    }

    [AvaloniaFact]
    public void ShapedParagraphs_NeverExceedPageCapacity()
    {
        var blocks = Enumerable.Range(0, 60).Select(i =>
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = $"paragraph {i} with some real shaped text content", FontSize = 14 });
            return (Block)p;
        }).ToArray();
        var ed = EditorWith(blocks);
        const double pageH = 300;
        var breaks = ed.ComputePageBreaks(698, pageH);

        Assert.True(breaks.Count > 1, $"expected multiple pages, got {breaks.Count}");
        for (int i = 1; i < breaks.Count; i++)
        {
            Assert.True(breaks[i] > breaks[i - 1], "breaks must be strictly increasing");
            // A page's span = its atoms (≤ pageH) plus the margins trailing its last block (10 default
            // MarginBottom). No atom here is oversized, so spans must stay within that bound.
            Assert.True(breaks[i] - breaks[i - 1] <= pageH + 11,
                $"page {i - 1} spans {breaks[i] - breaks[i - 1]:F1}px > capacity");
        }
    }

    // ---- Page view (Phase 2): the doc<->view mapping choke point ----

    [AvaloniaFact]
    public void FreeMode_MappingIsIdentity()
    {
        var ed = EditorWith(EmptyPara(50));
        ed.PageSize = RichEditorPageSize.Continuous;
        var p = new Avalonia.Point(123, 456);
        Assert.Equal(p, ed.MapDocToView(p));
        Assert.Equal(p, ed.MapViewToDoc(p));
    }

    [AvaloniaFact]
    public void PageView_MeasuresStackOfPages()
    {
        // 30 atoms of 50px = 1500px content; page capacity 1043 -> 20 atoms (1000px) per page -> 2 pages.
        var ed = EditorWith(Enumerable.Range(0, 30).Select(_ => (Block)EmptyPara(50)).ToArray());
        ed.PageSize = RichEditorPageSize.A4; // default, but explicit for the test's intent
        ed.Measure(new Avalonia.Size(900, double.PositiveInfinity));
        double expected = RichEditor.PageGap + 2 * (RichEditor.A4PageHeight + RichEditor.PageGap);
        Assert.Equal(expected, ed.DesiredSize.Height, 3);
    }

    [AvaloniaFact]
    public void PageView_MapRoundTrips_AcrossPages()
    {
        var ed = EditorWith(Enumerable.Range(0, 30).Select(_ => (Block)EmptyPara(50)).ToArray());
        ed.PageSize = RichEditorPageSize.A4; // breaks: [0, 1000]
        foreach (double docY in new[] { 0.0, 500, 999, 1000, 1250, 1499 })
        {
            var view = ed.MapDocToView(new Avalonia.Point(30, docY));
            var back = ed.MapViewToDoc(view);
            Assert.Equal(docY, back.Y, 3);
            Assert.Equal(30, back.X, 3);
        }
        // X gains the paper margin (desk centering is 0 while unmeasured: Bounds is empty).
        Assert.Equal(30 + RichEditor.PagePadX, ed.MapDocToView(new Avalonia.Point(30, 0)).X, 3);
    }

    [AvaloniaFact]
    public void PageView_ClickInGap_ClampsToNearestPageContent()
    {
        var ed = EditorWith(Enumerable.Range(0, 30).Select(_ => (Block)EmptyPara(50)).ToArray());
        ed.PageSize = RichEditorPageSize.A4; // page 1 holds doc 0..1000, paper 1 spans view 24..1147, gap 1147..1171
        double gapY = RichEditor.PageGap + RichEditor.A4PageHeight + RichEditor.PageGap / 2.0;
        var doc = ed.MapViewToDoc(new Avalonia.Point(100, gapY));
        Assert.True(doc.Y < 1000 && doc.Y > 990, $"gap click should clamp to page 1's end, got {doc.Y:F1}");
        // Click on the top paper margin of page 2 clamps to page 2's content start.
        double page2MarginY = RichEditor.PageGap * 2 + RichEditor.A4PageHeight + RichEditor.PagePadY / 2.0;
        var doc2 = ed.MapViewToDoc(new Avalonia.Point(100, page2MarginY));
        Assert.Equal(1000, doc2.Y, 3);
    }

    // ---- Print rendering (Phase 3) ----

    [AvaloniaFact]
    public void PrintPageCount_MatchesPagination_EvenInFreeMode()
    {
        var ed = EditorWith(Enumerable.Range(0, 30).Select(_ => (Block)EmptyPara(50)).ToArray());
        ed.PageSize = RichEditorPageSize.Continuous; // print still paginates (A4 fallback)
        Assert.Equal(2, ed.GetPrintPageCount());
    }

    [AvaloniaFact]
    public void RenderPrintPage_ProducesA4Bitmap_AndRejectsBadIndex()
    {
        var ed = EditorWith(EmptyPara(50));
        var bmp = ed.RenderPrintPage(0, dpi: 96);
        Assert.Equal(794, bmp.PixelSize.Width);
        Assert.Equal(1123, bmp.PixelSize.Height);
        var bmp300 = ed.RenderPrintPage(0, dpi: 300);
        Assert.Equal(2481, bmp300.PixelSize.Width, 1.0); // 794 * 300/96 ≈ 2481
        Assert.Throws<System.ArgumentOutOfRangeException>(() => ed.RenderPrintPage(1));
    }

    [AvaloniaFact]
    public void TallTable_SplitsAtRowBoundaries()
    {
        // 10 rows forced to exactly 100px each (RowHeights override beats empty-cell content);
        // page capacity 250 holds 2 rows -> breaks at 200/400/600/800, all row boundaries.
        var ed = new RichEditor();
        ed.LoadHtml("<table>" + string.Concat(Enumerable.Repeat("<tr><td></td></tr>", 10)) + "</table>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        for (int r = 0; r < 10; r++) tb.RowHeights.Add(100);
        tb.MarginTop = 0;
        foreach (var b in ed.Document.Blocks) { b.MarginTop = 0; b.MarginBottom = 0; }
        if (ed.Document.Blocks[0] is Paragraph p0) p0.LineHeight = 0; // NormalizeBlocks pads ends

        var breaks = ed.ComputePageBreaks(698, 250);
        Assert.True(breaks.Count >= 4, $"expected row-boundary splits, got {breaks.Count} page(s)");
        foreach (double b in breaks.Skip(1))
            Assert.True(System.Math.Abs(b % 100) < 0.01, $"break {b:F1} is not at a row boundary");
    }

    [AvaloniaFact]
    public void PageMarginChrome_RendersInPrintOutput()
    {
        var ed = EditorWith(EmptyPara(50));
        ed.PageHeader = "Header";
        ed.PageFooter = "Footer";
        ed.ShowPageNumbers = true;
        var bmp = ed.RenderPrintPage(0); // smoke: margin chrome must not throw or affect paging
        Assert.Equal(1, ed.GetPrintPageCount());
        Assert.Equal(794, bmp.PixelSize.Width);
    }

    [AvaloniaFact]
    public void SavePdf_WritesParseableMultiPagePdf()
    {
        var ed = EditorWith(Enumerable.Range(0, 30).Select(_ => (Block)EmptyPara(50)).ToArray()); // 2 pages
        using var ms = new System.IO.MemoryStream();
        ed.SavePdf(ms, dpi: 96); // low dpi keeps the test fast
        var bytes = ms.ToArray();
        string head = System.Text.Encoding.ASCII.GetString(bytes, 0, 8);
        string text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", head);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page ").Count);
        Assert.Contains("/Count 2", text);
        Assert.Contains("/Filter /FlateDecode", text);
    }

    [AvaloniaFact]
    public void LongParagraph_SplitsAtLineBoundaries()
    {
        // One paragraph, 50 uniform hard lines. Pages must break between lines: every full page
        // carries the same whole number of lines, so consecutive break gaps are all equal.
        var p = new Paragraph { MarginTop = 0, MarginBottom = 0 };
        p.Inlines.Add(new Run { Text = string.Join("\n", Enumerable.Range(0, 50).Select(i => $"line {i}")), FontSize = 14 });
        var ed = EditorWith(p);
        var breaks = ed.ComputePageBreaks(698, 200);

        Assert.True(breaks.Count >= 3, $"expected several pages, got {breaks.Count}");
        var gaps = new List<double>();
        for (int i = 1; i < breaks.Count; i++) gaps.Add(breaks[i] - breaks[i - 1]);
        Assert.All(gaps, g => Assert.True(g <= 200.011, $"page span {g:F2} exceeds capacity"));
        // Uniform lines → identical whole-line count per page → identical gaps.
        Assert.All(gaps, g => Assert.Equal(gaps[0], g, 2));
    }

    // ---- PageSize / ShowPageBoundaries: the three layout states ----

    [AvaloniaFact]
    public void Default_IsA4WithBoundaries()
    {
        var ed = new RichEditor();
        Assert.Equal(RichEditorPageSize.A4, ed.PageSize);
        Assert.True(ed.ShowPageBoundaries);
    }

    [AvaloniaFact]
    public void PaperSize_FixesContentLayoutWidth_FreeReflows()
    {
        var ed = EditorWith(EmptyPara(50));
        ed.PageSize = RichEditorPageSize.A4;
        Assert.Equal(698, ed.ContentLayoutWidth, 3);   // 794 - 2*48
        ed.PageSize = RichEditorPageSize.Letter;
        Assert.Equal(720, ed.ContentLayoutWidth, 3);   // 816 - 2*48
        ed.PageSize = RichEditorPageSize.Continuous;
        Assert.Equal(0, ed.ContentLayoutWidth, 3);     // reflows to control width (0 unmeasured)
    }

    [AvaloniaFact]
    public void FreeMode_MeasuresToControlWidth()
    {
        var ed = EditorWith(EmptyPara(50));
        ed.PageSize = RichEditorPageSize.Continuous;
        ed.Measure(new Avalonia.Size(640, double.PositiveInfinity));
        Assert.Equal(640, ed.DesiredSize.Width, 3);
    }

    [AvaloniaFact]
    public void Landscape_SwapsPaperDimensions()
    {
        var ed = EditorWith(EmptyPara(50));
        ed.PageSize = RichEditorPageSize.A4;
        var portrait = ed.GetPaperPixelSize();
        Assert.Equal(794, portrait.Width, 3);
        Assert.Equal(1123, portrait.Height, 3);

        ed.PageOrientation = RichEditorPageOrientation.Landscape;
        var land = ed.GetPaperPixelSize();
        Assert.Equal(1123, land.Width, 3);   // dimensions swapped
        Assert.Equal(794, land.Height, 3);
        Assert.Equal(1123 - 2 * 48, ed.ContentLayoutWidth, 3); // wider content column
    }

    [AvaloniaFact]
    public void PaperWithoutBoundaries_InjectsSmallGapsBetweenPages()
    {
        // Paged but no chrome: a small whitespace gap is injected at each page boundary (so pages read
        // as separate), but no full desk/paper chrome. 30 atoms of 50px break at 1000 (A4 capacity 1043).
        var ed = EditorWith(Enumerable.Range(0, 30).Select(_ => (Block)EmptyPara(50)).ToArray());
        ed.PageSize = RichEditorPageSize.A4;
        ed.ShowPageBoundaries = false;

        // Page 0 (doc 0..1000): no gap yet.
        Assert.Equal(500, ed.MapDocToView(new Avalonia.Point(10, 500)).Y, 3);
        // Page 1 (doc 1000..): one gap injected.
        var v = ed.MapDocToView(new Avalonia.Point(10, 1500));
        Assert.Equal(1500 + RichEditor.NoChromePageGap, v.Y, 3);
        Assert.Equal(1500, ed.MapViewToDoc(v).Y, 3); // round-trips through the gap
    }
}
