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
    public void SetHeading_ResetsRunSizesToBodyDefault_ButNotWeights()
    {
        // Like applying a Word style: a size baked into the runs would pin the text, so the heading
        // size (applied at layout to body-default runs) would never show. Weight is left alone.
        var p = TestHelpers.Para(
            new Run { Text = "a", FontSize = 14 },
            new Run { Text = "big", FontSize = 30 });
        var ed = EditorWithCaretIn(p);

        ed.SetHeading(1);

        Assert.Equal(1, p.HeadingLevel);
        Assert.Equal(10, ((Run)p.Inlines[0]).FontSize, 3);
        Assert.Equal(10, ((Run)p.Inlines[1]).FontSize, 3);
        Assert.Equal(FontWeight.Normal, ((Run)p.Inlines[0]).FontWeight);
    }

    [AvaloniaFact]
    public void SetHeading_ToBody_LeavesRunSizes()
    {
        var p = TestHelpers.Para(new Run { Text = "big", FontSize = 30 });
        var ed = EditorWithCaretIn(p);

        ed.SetHeading(0);

        Assert.Equal(30, ((Run)p.Inlines[0]).FontSize, 3);
    }

    [AvaloniaFact]
    public void Heading1ToHeading2_ChangesRenderedSize_EvenWhenSizeWasBaked()
    {
        // The reported bug: an <h1> imported from HTML (or the demo's headings) carried 20pt in its
        // runs, so switching to Heading 2 left the text at Heading 1 size.
        var p = TestHelpers.Para(new Run { Text = "Title", FontSize = 20 });
        p.HeadingLevel = 1;
        var ed = EditorWithCaretIn(p);
        ed.PageSize = RichEditorPageSize.Continuous;

        ed.SetHeading(1);
        ed.Measure(new Size(800, double.PositiveInfinity));
        double h1 = ed.DesiredSize.Height;
        ed.SetHeading(2);
        ed.Measure(new Size(800, double.PositiveInfinity));
        double h2 = ed.DesiredSize.Height;

        Assert.True(h2 < h1, $"Heading 2 should render smaller than Heading 1 ({h2} vs {h1})");
    }

    [AvaloniaFact]
    public void Heading_RendersTallerThanBody()
    {
        // The bigger look must actually take effect at layout time (and ParagraphSig must include the
        // heading level so the cached layout is rebuilt), so a heading paragraph measures taller.
        var p = TestHelpers.Para(new Run { Text = "Title", FontSize = 10 }); // body default (pt)
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

    [AvaloniaFact]
    public void LineSpacing_RendersTallerThanSingle_AndScalesWithFontSize()
    {
        // Proportional LineSpacing (2.0 = double) must measurably increase a paragraph's height, and
        // because it scales with font size, the same multiplier adds more height to bigger text.
        static double MeasuredHeight(double fontPt, double spacing)
        {
            var p = TestHelpers.Para(new Run { Text = "wrap this onto enough lines to matter", FontSize = fontPt });
            p.LineSpacing = spacing;
            var ed = EditorWithCaretIn(p);
            ed.PageSize = RichEditorPageSize.Continuous;
            ed.Measure(new Size(400, double.PositiveInfinity));
            return ed.DesiredSize.Height;
        }

        Assert.True(MeasuredHeight(10, 2.0) > MeasuredHeight(10, double.NaN),
            "double spacing should render taller than single");
        // Same multiplier, larger font -> larger absolute gain (proportional, not fixed px).
        double gainSmall = MeasuredHeight(10, 2.0) - MeasuredHeight(10, double.NaN);
        double gainBig = MeasuredHeight(24, 2.0) - MeasuredHeight(24, double.NaN);
        Assert.True(gainBig > gainSmall, $"spacing gain should scale with font size ({gainBig} vs {gainSmall})");
    }

    [AvaloniaFact]
    public void CaretFormat_ReflectsLineSpacing()
    {
        // The toolbar's line-spacing % label reads CaretFormat.LineSpacing; it must mirror the caret
        // paragraph (NaN when unset, the multiplier when set).
        var unset = TestHelpers.Para(new Run { Text = "x" });
        Assert.True(double.IsNaN(EditorWithCaretIn(unset).GetCaretFormat().LineSpacing));

        var spaced = TestHelpers.Para(new Run { Text = "x" });
        spaced.LineSpacing = 1.6;
        Assert.Equal(1.6, EditorWithCaretIn(spaced).GetCaretFormat().LineSpacing, 3);
    }

    [AvaloniaFact]
    public void SetHeading_Level6_ReflectsInCaretFormat()
    {
        var p = TestHelpers.Para(new Run { Text = "x", FontSize = 14 });
        var ed = EditorWithCaretIn(p);

        ed.SetHeading(6);

        Assert.Equal(6, p.HeadingLevel);
        Assert.Equal(6, ed.GetCaretFormat().Heading);
    }

    [AvaloniaFact]
    public void ToggleQuote_FlipsIsQuote_AndReflectsInCaretFormat()
    {
        var p = TestHelpers.Para(new Run { Text = "quote me", FontSize = 14 });
        var ed = EditorWithCaretIn(p);
        Assert.False(ed.GetCaretFormat().Quote);

        ed.ToggleQuote();
        Assert.True(p.IsQuote);
        Assert.True(ed.GetCaretFormat().Quote);

        ed.ToggleQuote();
        Assert.False(p.IsQuote);
        Assert.False(ed.GetCaretFormat().Quote);
    }
}
