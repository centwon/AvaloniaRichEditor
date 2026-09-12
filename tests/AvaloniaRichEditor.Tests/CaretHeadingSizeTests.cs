using System;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>The caret's size inside a heading.
/// <para>The caret is sized from the font size at the caret, and that size ignored headings: a heading's
/// unstyled runs are stored at the 10 pt body default and DRAWN at the heading size, so every heading got a
/// 10 pt caret. Measured before the fix: 19.0 in an H1, where the same 20 pt text in body text gets 29.1;
/// 19.0 in an H2 too, against 23.3. Body text never showed it — there the caret is clamped to the line
/// box, which is shorter than the 1.4 em the caret asks for. The caret now uses the renderer's rule,
/// <c>DrawnRunSize</c>. Found by measuring after the WinUI port fixed the mirror-image defect (its caret
/// took the heading size even for explicitly sized runs).</para>
/// <para>Judged against a REFERENCE — body text drawn at the same size — not a formula, so the test says
/// "sized like the text it sits in" and survives any change to how caret height is computed.</para>
/// </summary>
public class CaretHeadingSizeTests
{
    private const double W = 600, H = 800;
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    // The rendered caret's height at offset 2 of a one-paragraph document.
    private static double CaretHeight(int heading, double size)
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var p = new Paragraph { HeadingLevel = heading };
        p.Inlines.Add(new Run { Text = "Mxg Mxg", FontSize = size });
        doc.Blocks.Add(p);
        ed.Document = doc;
        typeof(RichEditor).GetField("_caretPosition", NP)!.SetValue(ed, new TextPointer(p, 2));
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        new RenderTargetBitmap(new PixelSize((int)W, (int)H)).Render(ed);
        return (double)typeof(RichEditor).GetField("_lastCaretHeight", NP)!.GetValue(ed)!;
    }

    // An unstyled run (unset, or at the 10 pt body default) in a heading is drawn at the heading's size.
    [AvaloniaTheory]
    [InlineData(1, 10.0, 20.0)]
    [InlineData(1, 0.0, 20.0)]
    [InlineData(2, 10.0, 16.0)]
    [InlineData(3, 10.0, 14.0)]
    public void TheCaretOnAnUnstyledRunInAHeading_IsSizedLikeTheHeading(int level, double runSize, double drawnAt)
    {
        double heading = CaretHeight(level, runSize), body = CaretHeight(0, drawnAt);
        Assert.True(Math.Abs(heading - body) < 0.5,
            $"H{level} unstyled: caret {heading:0.0}, {drawnAt}pt body text {body:0.0}");
    }

    // Pinned so the fix cannot trade it away: an explicitly sized run keeps its own size in a heading (the
    // renderer draws it so), and so does its caret — this was already right here, and was the WinUI bug.
    // Only sizes LARGER than the heading's can tell: the caret is clamped to its line box, so a smaller
    // run given the heading's size is clamped back to the right height (a 12 pt case passed a falsification
    // that made every heading caret heading-sized — it guarded nothing).
    [AvaloniaTheory]
    [InlineData(36.0)]
    [InlineData(28.0)]
    public void TheCaretOnAnExplicitlySizedRunInAHeading_IsSizedLikeThatRun(double size)
    {
        double heading = CaretHeight(1, size), body = CaretHeight(0, size);
        Assert.True(Math.Abs(heading - body) < 0.5,
            $"{size}pt in an H1: caret {heading:0.0}, the same run in body text {body:0.0}");
    }
}
