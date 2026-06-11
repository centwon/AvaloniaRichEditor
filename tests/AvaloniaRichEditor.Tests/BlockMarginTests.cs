using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Block-level margins (roadmap "블록 여백 제어"): MarginTop/MarginBottom moved up to Block so
// images/tables/dividers carry them too. JSON round-trip, legacy defaults (docs saved before the
// fields existed must keep the historical fixed 10px gap), and undo-clone propagation.
public class BlockMarginTests
{
    [AvaloniaFact]
    public void Margins_RoundTripThroughJson_ForAllBlockTypes()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var p = new Paragraph { MarginTop = 7, MarginBottom = 21, MarginRight = 15 };
        p.Inlines.Add(new Run { Text = "x" });
        var img = new ImageBlock { MarginTop = 4, MarginBottom = 0 };
        var tb = new TableBlock(1, 1) { MarginTop = 12, MarginBottom = 34 };
        var dv = new DividerBlock { MarginTop = 3, MarginBottom = 6 };
        doc.Blocks.Add(p); doc.Blocks.Add(img); doc.Blocks.Add(tb); doc.Blocks.Add(dv);
        ed.Document = doc;

        var ed2 = new RichEditor();
        ed2.LoadJson(ed.ToJson());

        var p2 = ed2.Document!.Blocks.OfType<Paragraph>().First(b => b.Inlines.Count > 0);
        Assert.Equal(7, p2.MarginTop);
        Assert.Equal(21, p2.MarginBottom);
        Assert.Equal(15, p2.MarginRight);
        var img2 = ed2.Document.Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(4, img2.MarginTop);
        Assert.Equal(0, img2.MarginBottom);
        var tb2 = ed2.Document.Blocks.OfType<TableBlock>().Single();
        Assert.Equal(12, tb2.MarginTop);
        Assert.Equal(34, tb2.MarginBottom);
        var dv2 = ed2.Document.Blocks.OfType<DividerBlock>().Single();
        Assert.Equal(3, dv2.MarginTop);
        Assert.Equal(6, dv2.MarginBottom);
    }

    [AvaloniaFact]
    public void LegacyJson_WithoutMarginFields_GetsHistoricalDefaults()
    {
        // Documents saved before the margin fields existed: images/tables rendered with a fixed
        // 10px bottom gap, dividers with none — loading must reproduce that.
        var ed = new RichEditor();
        ed.LoadJson("""{"Version":2,"Blocks":[{"Type":"Image","Width":100,"Height":80},{"Type":"Divider"}]}""");

        var img = ed.Document!.Blocks.OfType<ImageBlock>().Single();
        Assert.Equal(0, img.MarginTop);
        Assert.Equal(10, img.MarginBottom);
        var dv = ed.Document.Blocks.OfType<DividerBlock>().Single();
        Assert.Equal(0, dv.MarginTop);
        Assert.Equal(0, dv.MarginBottom);
    }

    [AvaloniaFact]
    public void Clone_CopiesMargins_OnImageTableAndDivider()
    {
        var img = new ImageBlock { MarginTop = 4, MarginBottom = 40 };
        var tb = new TableBlock(1, 1) { MarginTop = 5, MarginBottom = 50 };
        var dv = new DividerBlock { MarginTop = 6, MarginBottom = 60 };

        var img2 = (ImageBlock)img.Clone();
        var tb2 = (TableBlock)tb.Clone();
        var dv2 = (DividerBlock)dv.Clone();

        Assert.Equal((4d, 40d), (img2.MarginTop, img2.MarginBottom));
        Assert.Equal((5d, 50d), (tb2.MarginTop, tb2.MarginBottom));
        Assert.Equal((6d, 60d), (dv2.MarginTop, dv2.MarginBottom));
    }
}
