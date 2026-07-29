using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// While the IME is composing, the preedit text is spliced into the caret's paragraph and RENDERED, but
// the measure walk built the same paragraph without it. In a table cell that means the row is sized for
// the text without the composition, so the composed text draws past the cell's bottom border — visible
// on every wrap while typing Korean/Japanese/Chinese into a narrow cell.
public class ImePreeditMeasureTests
{
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    // The IME client calls this on every composition update.
    private static void SetPreedit(RichEditor ed, string? text)
        => typeof(RichEditor).GetMethod("SetPreedit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object?[] { text });

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, off));
    }

    private static double Measure(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        return ed.DesiredSize.Height;
    }

    // A long composition string that must wrap inside a narrow cell.
    private const string LongPreedit = "가나다라마바사아자차카타파하가나다라마바사아자차카타파하";

    [AvaloniaFact]
    public void ComposingInACell_GrowsTheRowHeight()
    {
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 120; // narrow enough that the composition wraps
        var cellPara = tb.Cells[0][0].Para;
        ((Run)cellPara.Inlines[0]).Text = "짧게";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        PlaceCaret(ed, cellPara, 2);

        double before = Measure(ed);
        SetPreedit(ed, LongPreedit);
        double composing = Measure(ed);

        Assert.True(composing > before,
            $"the row must grow to fit the composition text ({composing} vs {before})");
    }

    [AvaloniaFact]
    public void EndingTheComposition_ShrinksTheRowBack()
    {
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 120;
        var cellPara = tb.Cells[0][0].Para;
        ((Run)cellPara.Inlines[0]).Text = "짧게";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        PlaceCaret(ed, cellPara, 2);

        double before = Measure(ed);
        SetPreedit(ed, LongPreedit);
        Measure(ed);
        SetPreedit(ed, null); // the IME committed or cancelled
        double after = Measure(ed);

        Assert.Equal(before, after, 1);
    }

    // The same measure gap at the top level. The symptom differs — the render walk does advance by the
    // preedit height, so nothing overlaps — but the reported extent stayed a line short of what was
    // drawn, so a composition at the end of a document could not be scrolled to.
    [AvaloniaFact]
    public void ComposingInATopLevelParagraph_GrowsTheScrollExtent()
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "짧게" });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed, 200); // narrow, so the composition has to wrap
        PlaceCaret(ed, p, 2);

        double before = Measure(ed, 200);
        SetPreedit(ed, LongPreedit);
        double composing = Measure(ed, 200);

        Assert.True(composing > before,
            $"the scroll extent must cover the composition ({composing} vs {before})");
    }

    // Only the HEIGHT is taken from the composition layout. BlockExtent still hands the hit-tests the
    // plain layout, whose indices are logical offsets — a preedit layout's indices are display positions
    // that include the composition, so a click would land shifted by its length. Asserting invariance
    // rather than an absolute offset: what matters is that composing changes nothing here.
    [AvaloniaFact]
    public void ComposingDoesNotShiftWhereAClickLands()
    {
        int Hit(bool composing)
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = "짧게" });
            var doc = new FlowDocument();
            doc.Blocks.Add(p);
            var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
            Realize(ed, 200);
            PlaceCaret(ed, p, 2);
            if (composing) SetPreedit(ed, LongPreedit);
            Measure(ed, 200);
            return ((TextPointer)typeof(RichEditor)
                .GetMethod("GetPositionFromPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(ed, new object[] { new Point(150, 8) })!).Offset;
        }

        Assert.Equal(Hit(composing: false), Hit(composing: true));
    }

    // A cell nested one level deeper must push its host row too.
    [AvaloniaFact]
    public void ComposingInANestedCell_GrowsTheOuterRowToo()
    {
        var outer = new TableBlock(1, 1);
        outer.ColumnWidths[0] = 200;
        var inner = new TableBlock(1, 1);
        inner.ColumnWidths[0] = 120;
        var innerPara = inner.Cells[0][0].Para;
        ((Run)innerPara.Inlines[0]).Text = "짧게";
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(inner);

        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        PlaceCaret(ed, innerPara, 2);

        double before = Measure(ed);
        SetPreedit(ed, LongPreedit);
        double composing = Measure(ed);

        Assert.True(composing > before,
            $"the outer row must grow with the nested cell ({composing} vs {before})");
    }
}
