using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The layout cache holds what was DRAWN or hit-tested, not the document (2026-09-28). Measure, pagination and
// every walk that only steps past a paragraph read its height (_heightCache) instead of taking its layout,
// which made the cache hold one per paragraph: measured with the Tests.Render LayoutCacheProbe in an 800x600
// window, 8,000 paragraphs held 8,000 layouts and 270 MB of managed heap at load; now 29 layouts, 39 MB. A render
// trims it back to the most recently used, or scrolling to the end would fill it again.
//
// What these guard: the height is the layout's height exactly (and forgets what the layout forgets); the
// walks that now skip layouts still number, split and hit-test as before; the cache stays bounded — and never
// evicts what the frame drawing it is using.
public class LayoutCacheBoundTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type T = typeof(RichEditor);

    private static Avalonia.Media.TextFormatting.TextLayout Layout(RichEditor ed, Paragraph p, double w)
        => (Avalonia.Media.TextFormatting.TextLayout)T.GetMethod("BuildTextLayout", NP)!.Invoke(ed, [p, w, -1, null])!;
    private static double Height(RichEditor ed, Paragraph p, double w)
        => (double)T.GetMethod("ParagraphHeight", NP)!.Invoke(ed, [p, w])!;

    private static string Words(int i) => $"paragraph {i} — " + string.Concat(Enumerable.Repeat("some words that wrap the line ", 1 + i % 5));

    private static FlowDocument Paragraphs(int n, ListKind list = ListKind.None)
    {
        var doc = new FlowDocument();
        for (int i = 0; i < n; i++)
            doc.Blocks.Add(new Paragraph { ListType = list, Inlines = { new Run { Text = list == ListKind.None ? Words(i) : $"item {i}" } } });
        return doc;
    }

    // The editor in a ScrollViewer in a window: the only arrangement in which render culling (and so the
    // bound) applies, since culling keys on the scroller's viewport.
    private sealed class Host
    {
        public readonly Window Window;
        public readonly ScrollViewer Scroller;
        public readonly RichEditor Editor;
        public Host(FlowDocument doc, double w = 800, double h = 600)
        {
            Editor = new RichEditor { Document = doc };
            Scroller = new ScrollViewer { Content = Editor };
            Window = new Window { Width = w, Height = h, Content = Scroller };
            Window.Show();
            Frame();
        }
        public void Frame()
        {
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var rtb = new RenderTargetBitmap(new PixelSize((int)Window.Width, (int)Window.Height));
            rtb.Render(Window);
        }
        public void ScrollTo(double y) { Scroller.Offset = new Vector(0, y); Frame(); }
        // Content only: closing the last headless top-level shuts the dispatcher down for the tests after it.
        public void Close() { Window.Content = null; Dispatcher.UIThread.RunJobs(); }
    }

    // ---- the height is the layout's height ------------------------------------------------------------

    [AvaloniaFact]
    public void TheCachedHeight_IsTheLayoutsHeight_ForEveryKindOfParagraph()
    {
        var img = new InlineImage { Width = 30, Height = 44 };
        img.SetImageData([1, 2, 3], "image/png");
        var paras = new List<Paragraph>
        {
            new() { Inlines = { new Run { Text = Words(3) } } },
            new() { HeadingLevel = 1, Inlines = { new Run { Text = "a heading that is long enough to wrap at a narrow width" } } },
            new() { ListType = ListKind.Ordered, Inlines = { new Run { Text = "one\ntwo\nthree" } } },
            new() { LineSpacing = 2.5, Inlines = { new Run { Text = Words(2) } } },
            new() { Inlines = { new Run { Text = "with a picture " }, img, new Run { Text = " after it", FontSize = 18 } } },
            new() { Inlines = { new Run { Text = "" } } },
        };
        var doc = new FlowDocument();
        doc.Blocks.AddRange(paras);
        var ed = new RichEditor { Document = doc };
        foreach (var w in new[] { 80.0, 250, 700 })
            foreach (var p in paras)
            {
                double measured = Height(ed, p, w);
                Assert.Equal(Layout(ed, p, w).Height, measured, 6);
            }
    }

    [AvaloniaFact]
    public void TheCachedHeight_FollowsAnEdit_AndADefaultFontChange()
    {
        var p = new Paragraph { Inlines = { new Run { Text = Words(4), FontSize = 0 } } }; // 0 = the default size
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc };
        double before = Height(ed, p, 200);

        ed.DefaultFontSize = 24; // not part of any signature: the caches are cleared for it
        double bigger = Height(ed, p, 200);
        Assert.True(bigger > before, $"height {before} -> {bigger} after the default size grew");
        var fresh = new RichEditor { Document = new FlowDocument(), DefaultFontSize = 24 };
        Assert.Equal(Height(fresh, p, 200), bigger, 6);

        ((Run)p.Inlines[0]).Text += " and a good deal more text to push it onto further lines of the paragraph";
        Assert.Equal(Layout(ed, p, 200).Height, Height(ed, p, 200), 6);
    }

    // Select all + line spacing sets a paragraph's LineSpacing AND its inline table's cell's, NaN -> value.
    // NaN and 1.5 differ only in a double's top bits, and FNV's xor-multiply carries a difference only
    // upward, so the two changes cancelled in the host's signature (1 in 28 of these pairs) and it kept its
    // old layout and height. Every pair here must move the signature.
    [Fact]
    public void TwoWideChangesInOneEdit_NeverCancelInTheSignature()
    {
        var sig = T.GetMethod("ParagraphSig", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0].Para;
        cell.Inlines.Add(new Run { Text = "cell" });
        var host = new Paragraph { Inlines = { new Run { Text = "ab" }, new InlineTable { Table = tb }, new Run { Text = "cdef" } } };
        var cancelled = new List<string>();
        foreach (var v in new[] { 1.0, 1.5, 2.0, 2.5, 3.0, 1.6, 0.8 })
            foreach (var heading in new[] { 0, 1, 2, 3 })
            {
                host.HeadingLevel = heading;
                host.LineSpacing = cell.LineSpacing = double.NaN;
                long before = (long)sig.Invoke(null, [host])!;
                host.LineSpacing = cell.LineSpacing = v;
                if ((long)sig.Invoke(null, [host])! == before) cancelled.Add($"{v}/h{heading}");
            }
        Assert.Empty(cancelled);
    }

    // Bullets and numbers take a gutter from the wrap width, so toggling a list changes the height; the list
    // commands repainted without invalidating the measure, and the editor kept the height it had before.
    [AvaloniaFact]
    public void TogglingAList_ChangesTheMeasuredHeight()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = string.Concat(Enumerable.Repeat("word ", 400)) } } });
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var size = new Size(300, double.PositiveInfinity);
        ed.Measure(size);
        double plain = ed.DesiredSize.Height;
        T.GetMethod("SelectAll", NP, Type.EmptyTypes)!.Invoke(ed, null);

        ed.ToggleBullet();
        ed.Measure(size);
        double bulleted = ed.DesiredSize.Height;
        Assert.True(bulleted > plain, $"height {plain} -> {bulleted}: the gutter narrowed the lines, so there are more of them");

        ed.ToggleBullet();
        ed.Measure(size);
        Assert.Equal(plain, ed.DesiredSize.Height, 2);
    }

    // ---- the walks that stopped taking layouts ---------------------------------------------------------

    // Pagination reads line ends only for a paragraph that crosses a page, from the cached LineBottoms.
    // Oracle: the old loop over the layout's own line tops, for every page height in a range, so a break can
    // land at every line of every paragraph (a line-parity test would miss an atom made two lines tall).
    [AvaloniaFact]
    public void PageBreaks_MatchTheLayoutsOwnLineTops_AtEveryPageHeight()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "short opener" } } });
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = string.Concat(Enumerable.Repeat("a long paragraph of many lines ", 40)) } } });
        doc.Blocks.Add(new Paragraph { LineSpacing = 2, Inlines = { new Run { Text = string.Concat(Enumerable.Repeat("spaced out lines here ", 25)) } } });
        var ed = new RichEditor { Document = doc };
        const double width = 300;

        for (double pch = 40; pch <= 400; pch += 7)
        {
            var got = ed.ComputePageBreaks(width, pch);
            Assert.Equal(Oracle(ed, doc, width, pch), got);
        }
    }

    private static List<double> Oracle(RichEditor ed, FlowDocument doc, double contentWidth, double pch)
    {
        var breaks = new List<double> { 0 };
        double y = 0, pageStart = 0;
        const double eps = 0.01;
        var wrap = T.GetMethod("ParagraphWrapWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (Paragraph p in doc.Blocks.Cast<Paragraph>())
        {
            y += ed.TopGapOf(p);
            var layout = Layout(ed, p, (double)wrap.Invoke(null, [p, contentWidth])!);
            double paraTop = y, atomTop = 0;
            var lines = layout.TextLines;
            for (int li = 1; li <= lines.Count; li++)
            {
                double atomBottom = li < lines.Count ? layout.HitTestTextPosition(lines[li].FirstTextSourceIndex).Y : layout.Height;
                if (paraTop + atomBottom > pageStart + pch + eps && paraTop + atomTop > pageStart)
                {
                    breaks.Add(paraTop + atomTop);
                    pageStart = paraTop + atomTop;
                }
                atomTop = atomBottom;
            }
            y = paraTop + layout.Height + p.MarginBottom;
        }
        return breaks;
    }

    // Culled paragraphs build no layout now, but still advance the ordered numbering, one per hard line.
    [AvaloniaFact]
    public void OrderedNumbering_CountsTheParagraphsCulledAboveTheViewport()
    {
        var doc = Paragraphs(300, ListKind.Ordered);
        ((Run)((Paragraph)doc.Blocks[5]).Inlines[0]).Text = "two\nlines"; // a culled item holding two numbers
        var host = new Host(doc);
        try
        {
            host.ScrollTo(host.Scroller.Extent.Height); // the end: everything above is culled
            host.Editor.DrawnListMarkers = new();
            host.Editor.InvalidateVisual();
            host.Frame();

            var drawn = host.Editor.DrawnListMarkers;
            Assert.NotEmpty(drawn);
            var blocks = host.Editor.Document!.Blocks;
            foreach (var (p, num) in drawn)
            {
                int i = blocks.IndexOf(p);
                Assert.True(i > 5, $"paragraph {i} was drawn — the test needs the item with two lines culled");
                Assert.Equal(i + 2, num); // item i is number i + 1, plus the extra line of item 5
            }
        }
        finally { host.Close(); }
    }

    // A link in a cell's SECOND paragraph: the cell link walk now steps past the first on its height alone.
    [AvaloniaFact]
    public void ALinkInACellsSecondParagraph_IsFound()
    {
        var link = new Run { Text = "the link", NavigateUri = "https://example.com" };
        var tb = new TableBlock(1, 1);
        tb.Cells[0][0].Blocks.Clear();
        tb.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = Words(2) } } });
        var second = new Paragraph { Inlines = { link } };
        tb.Cells[0][0].Blocks.Add(second);
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "before" } } });
        doc.Blocks.Add(tb);
        var host = new Host(doc);
        try
        {
            T.GetField("_caretPosition", NP)!.SetValue(host.Editor, new TextPointer(second, 2));
            host.Editor.InvalidateVisual();
            host.Frame();
            var caret = (Point)T.GetField("_lastCaretPoint", NP)!.GetValue(host.Editor)!;
            var hit = T.GetMethod("GetLinkRunAtPoint", NP)!.Invoke(host.Editor, [new Point(caret.X + 1, caret.Y + 4)]);
            Assert.Same(link, hit);
        }
        finally { host.Close(); }
    }

    // ---- the bound -------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Loading_KeepsLayoutsForWhatIsOnScreen_NotTheDocument()
    {
        var host = new Host(Paragraphs(2000));
        try
        {
            Assert.True(host.Scroller.Extent.Height > 10 * host.Scroller.Viewport.Height, "the document must be much taller than the window");
            Assert.InRange(host.Editor.LayoutCacheCount, 1, 100);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void ScrollingToTheEnd_KeepsTheCacheBounded_AndTheScreenCached()
    {
        var host = new Host(Paragraphs(2000));
        try
        {
            var sv = host.Scroller;
            while (sv.Offset.Y + sv.Viewport.Height < sv.Extent.Height - 1)
                host.ScrollTo(sv.Offset.Y + sv.Viewport.Height * 0.9);
            Assert.InRange(host.Editor.LayoutCacheCount, 1, 256 + 128);
            var cache = (IDictionary)T.GetField("_layoutCache", NP)!.GetValue(host.Editor)!;
            Assert.True(cache.Contains(host.Editor.Document!.Blocks[^1]), "the last paragraph, on screen, is not cached");
            Assert.False(cache.Contains(host.Editor.Document!.Blocks[0]), "the first paragraph, 2,000 back, is still cached");
        }
        finally { host.Close(); }
    }

    // A visible table draws every cell; its cells are one frame's working set and must all survive the trim,
    // or each caret blink would shape them again.
    [AvaloniaFact]
    public void ATrim_NeverEvictsWhatTheFrameDrawingItUsed()
    {
        var tb = new TableBlock(400, 1);
        for (int r = 0; r < 400; r++) tb.Cells[r][0].Para.Inlines.Add(new Run { Text = $"cell {r}" });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "before" } } });
        doc.Blocks.Add(tb);
        var host = new Host(doc);
        try
        {
            host.Editor.InvalidateVisual();
            host.Frame();
            Assert.True(host.Editor.LayoutCacheCount >= 400, $"{host.Editor.LayoutCacheCount} layouts after drawing a 400-cell table");
        }
        finally { host.Close(); }
    }
}
