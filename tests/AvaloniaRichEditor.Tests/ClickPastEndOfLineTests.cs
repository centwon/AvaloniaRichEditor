using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Clicking the empty space to the right of a line — an everyday action. Avalonia reports the last
// position with IsTrailing set once the point is past the end of a line, so HitTestIndex's
// TextPosition + IsTrailing came back ONE PAST the paragraph's length. The caret then sat at an offset
// that does not exist, and the two things you do next both broke.
public class ClickPastEndOfLineTests
{
    private static RichEditor Editor(FlowDocument doc, double width = 300)
    {
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
        return ed;
    }

    private static TextPointer Hit(RichEditor ed, double x, double y)
        => (TextPointer)typeof(RichEditor)
            .GetMethod("GetPositionFromPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object[] { new Point(x, y) })!;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, off));
    }

    private static (RichEditor ed, Paragraph p) OneLine(string text, FontWeight weight = FontWeight.Normal)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text, FontWeight = weight });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        return (Editor(doc), p);
    }

    [AvaloniaTheory]
    [InlineData("ab")]
    [InlineData("hello world")]
    [InlineData("짧게")]
    public void ClickingRightOfALine_LandsAtTheParagraphsEnd_NotPastIt(string text)
    {
        var (ed, _) = OneLine(text);
        var hit = Hit(ed, 280, 8); // far right of the text, still inside the control
        Assert.Equal(text.Length, hit.Offset);
    }

    // Consequence 1: the delete range fell outside every run, so nothing was removed.
    [AvaloniaFact]
    public void BackspaceAfterClickingRightOfALine_DeletesTheLastCharacter()
    {
        var (ed, p) = OneLine("ab");
        PlaceCaret(ed, p, Hit(ed, 280, 8).Offset);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });

        Assert.Equal("a", p.Text());
    }

    // Consequence 2: the insert point wasn't found, so a fresh unformatted run was appended and text
    // typed after a bold line came out unbolded.
    [AvaloniaFact]
    public void TypingAfterClickingRightOfALine_ContinuesTheRunsFormatting()
    {
        var (ed, p) = OneLine("ab", FontWeight.Bold);
        PlaceCaret(ed, p, Hit(ed, 280, 8).Offset);

        ed.InsertText("X");

        Assert.Equal("abX", p.Text());
        Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
    }

    // The same click inside a table cell goes through the other walk (HitTestBlockList).
    [AvaloniaFact]
    public void ClickingRightOfALineInsideACell_LandsAtTheParagraphsEnd()
    {
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 250;
        var cellPara = tb.Cells[0][0].Para;
        ((Run)cellPara.Inlines[0]).Text = "ab";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = Editor(doc);

        // The overshoot only appears at/past the layout box's right edge, so sweep the whole cell rather
        // than guessing one point: NO click inside it may resolve past the text's length. (Sweeping also
        // survives the empty paragraph NormalizeBlocks puts before the table.)
        int worst = 0, hits = 0;
        for (double y = 2; y < ed.DesiredSize.Height; y += 2)
            for (double x = 12; x < 265; x += 2)
                if (Hit(ed, x, y) is { } h && ReferenceEquals(h.Paragraph, cellPara))
                {
                    hits++;
                    worst = System.Math.Max(worst, h.Offset);
                }

        Assert.True(hits > 0, "the cell paragraph must be reachable by clicking");
        Assert.Equal(2, worst); // the cell's text is "ab" — never 3
    }

    // Guards: clicking ON the text must be unaffected by the clamp.
    [AvaloniaFact]
    public void ClickingAtTheStartOfALine_StillLandsAtZero()
    {
        var (ed, _) = OneLine("hello world");
        Assert.Equal(0, Hit(ed, 11, 8).Offset);
    }

    [AvaloniaFact]
    public void ClickingInsideTheText_StillLandsInsideTheParagraph()
    {
        var (ed, p) = OneLine("hello world");
        var hit = Hit(ed, 40, 8);
        Assert.InRange(hit.Offset, 1, p.Text().Length - 1);
    }

    // An empty paragraph has no positions to clamp to but must still resolve to 0.
    [AvaloniaFact]
    public void ClickingRightOfAnEmptyParagraph_LandsAtZero()
    {
        var (ed, _) = OneLine("");
        Assert.Equal(0, Hit(ed, 280, 8).Offset);
    }
}
