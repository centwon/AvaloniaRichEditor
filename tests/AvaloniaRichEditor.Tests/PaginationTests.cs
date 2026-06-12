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
}
