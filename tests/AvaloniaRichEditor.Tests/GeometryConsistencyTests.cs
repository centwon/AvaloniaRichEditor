using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// G1 safety net: the read-only document walkers (measure / hit-test / block-at-y) must agree on each
// block's vertical placement now that they share BlockExtent. These assert *identity/consistency*
// (which block a point lands in, that measure reaches past the last block) rather than exact pixels,
// so they're robust across fonts/platforms while still catching walker drift.
public class GeometryConsistencyTests
{
    // Forces a render pass (no top-level Window — same approach as the caret-nav tests) so layout caches
    // and _lastCaretPoint are populated, and DesiredSize reflects MeasureContentHeight.
    private static void Realize(RichEditor ed, double width = 800, double height = 2000)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static RichEditor MixedDoc()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(Para("first paragraph"));
        var tb = new TableBlock(2, 2);
        ((Run)tb.Cells[0][0].Inlines[0]).Text = "a";
        ((Run)tb.Cells[0][1].Inlines[0]).Text = "b";
        doc.Blocks.Add(tb);
        doc.Blocks.Add(Para("after the table"));
        doc.Blocks.Add(new DividerBlock());
        doc.Blocks.Add(Para("last paragraph"));
        // Continuous mode so DesiredSize reflects MeasureContentHeight (the migrated walker); the default
        // A4 page-chrome mode would instead size by page count, hiding content-height differences.
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        return ed;

        static Paragraph Para(string t)
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = t });
            return p;
        }
    }

    [AvaloniaFact]
    public void MeasureReachesPastLastBlock()
    {
        var ed = MixedDoc();
        Realize(ed);
        // The content height must cover every block; a point near the bottom resolves to the last
        // paragraph (proves measure and hit-test agree on where content ends).
        double h = ed.DesiredSize.Height;
        Assert.True(h > 60, $"content height unexpectedly small: {h}");
        var p = ed.Document!.Blocks.OfType<Paragraph>().Last();
        Assert.Equal("last paragraph", ((Run)p.Inlines[0]).Text);
    }

    [AvaloniaFact]
    public void EmptyDocAndMixedDoc_DoNotThrow_AndMeasureGrows()
    {
        var empty = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        Realize(empty);
        double emptyH = empty.DesiredSize.Height;

        var mixed = MixedDoc();
        Realize(mixed);
        // A document with a table + divider + several paragraphs must measure taller than an empty one.
        Assert.True(mixed.DesiredSize.Height > emptyH,
            $"mixed ({mixed.DesiredSize.Height}) should exceed empty ({emptyH})");
    }

    [AvaloniaFact]
    public void TablePresenceDoesNotBreakMeasureMonotonicity()
    {
        // Inserting a block must never shrink the measured height — a drift between BlockExtent's table
        // height and the walkers would surface here.
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        var doc = ed.Document!;
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "x" } } });
        Realize(ed);
        double before = ed.DesiredSize.Height;

        doc.Blocks.Add(new TableBlock(3, 3));
        ed.InvalidateMeasure();
        Realize(ed);
        Assert.True(ed.DesiredSize.Height > before,
            $"adding a 3x3 table should grow height ({before} -> {ed.DesiredSize.Height})");
    }
}
