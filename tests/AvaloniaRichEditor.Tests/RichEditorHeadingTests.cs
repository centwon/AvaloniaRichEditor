using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// C1: heading level is a paragraph property whose bigger/bold look is applied at layout time, not
// baked into run FontSize/FontWeight. Toggling a heading on and back off must therefore never
// overwrite or lose a run's manually-set size — the old destructive SetHeading flattened every run.
public class RichEditorHeadingTests
{
    private static RichEditor EditorWithCaretIn(Paragraph p)
    {
        var ed = new RichEditor();
        ed.Document = TestHelpers.Doc(p);
        ed.FocusDocumentEnd(); // caret lands in the (last) paragraph
        return ed;
    }

    [AvaloniaFact]
    public void SetHeading_DoesNotBakeRunSizesOrWeights()
    {
        var p = TestHelpers.Para(
            new Run { Text = "a", FontSize = 14 },
            new Run { Text = "big", FontSize = 30 });
        var ed = EditorWithCaretIn(p);

        ed.SetHeading(1);

        Assert.Equal(1, p.HeadingLevel);
        // Runs are untouched — the heading look comes from layout, not from rewriting the model.
        Assert.Equal(14, ((Run)p.Inlines[0]).FontSize, 3);
        Assert.Equal(30, ((Run)p.Inlines[1]).FontSize, 3);
        Assert.Equal(FontWeight.Normal, ((Run)p.Inlines[0]).FontWeight);
    }

    [AvaloniaFact]
    public void SetHeading_ThenRevertToBody_PreservesManualRunSize()
    {
        var p = TestHelpers.Para(
            new Run { Text = "a", FontSize = 14 },
            new Run { Text = "big", FontSize = 30 });
        var ed = EditorWithCaretIn(p);

        ed.SetHeading(2);
        ed.SetHeading(0); // back to body — the old code reset every run to 14/Normal here

        Assert.Equal(0, p.HeadingLevel);
        Assert.Equal(30, ((Run)p.Inlines[1]).FontSize, 3); // user's size survived the round trip
    }

    [AvaloniaFact]
    public void Heading_RendersTallerThanBody()
    {
        // The bigger look must actually take effect at layout time (and ParagraphSig must include the
        // heading level so the cached layout is rebuilt), so a heading paragraph measures taller.
        var p = TestHelpers.Para(new Run { Text = "Title", FontSize = 14 });
        var ed = EditorWithCaretIn(p);
        ed.PageSize = RichEditorPageSize.Continuous;

        ed.Measure(new Size(800, double.PositiveInfinity));
        double bodyHeight = ed.DesiredSize.Height;

        ed.SetHeading(1);
        ed.Measure(new Size(800, double.PositiveInfinity));
        double headingHeight = ed.DesiredSize.Height;

        Assert.True(headingHeight > bodyHeight,
            $"H1 should render taller than body ({headingHeight} vs {bodyHeight})");
    }
}
