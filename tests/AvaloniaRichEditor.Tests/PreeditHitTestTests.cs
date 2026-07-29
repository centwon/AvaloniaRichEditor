using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Clicking while the IME is composing. The glyphs on screen include the preedit, but the hit-test read
// the plain layout, so a click resolved to the offset it would have had if the composition weren't
// there — drifting further the longer the composition grew. Also covers Shift+Tab, which used to type
// four spaces outside a table.
public class PreeditHitTestTests
{
    private static void Realize(RichEditor ed, double width = 400)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static void SetPreedit(RichEditor ed, string? text)
        => typeof(RichEditor).GetMethod("SetPreedit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object?[] { text });

    private static TextPointer Hit(RichEditor ed, double x, double y)
        => (TextPointer)typeof(RichEditor)
            .GetMethod("GetPositionFromPoint", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object[] { new Point(x, y) })!;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, off));
    }

    private static (RichEditor ed, Paragraph p) OneLine(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        return (ed, p);
    }

    // The smallest x whose click lands at the paragraph's end — i.e. just past the drawn text.
    private static double FirstXAtEnd(RichEditor ed, int length)
    {
        for (double x = 2; x < 400; x += 1)
            if (Hit(ed, x, 8).Offset >= length) return x;
        return 400;
    }

    // "abcd" with a composition spliced in at offset 2 draws as "ab[XYZXYZ]cd". A click over the
    // composition's glyphs sits well past where plain "abcd" ends, so the old code clamped it to the
    // paragraph's end; it must resolve to the composition's start instead.
    [AvaloniaFact]
    public void ClickingOverTheComposition_ResolvesToItsStart_NotTheParagraphEnd()
    {
        var (ed, p) = OneLine("abcd");
        PlaceCaret(ed, p, 2);
        double xPastPlainText = FirstXAtEnd(ed, 4) + 12; // beyond "abcd", inside the composition

        SetPreedit(ed, "XYZXYZ");
        Realize(ed);

        Assert.Equal(2, Hit(ed, xPastPlainText, 8).Offset);
    }

    // Positions after the composition shift back by its length rather than counting its characters.
    [AvaloniaFact]
    public void ClickingPastTheComposition_MapsBackToALogicalOffset()
    {
        var (ed, p) = OneLine("abcd");
        PlaceCaret(ed, p, 2);
        SetPreedit(ed, "XYZXYZ");
        Realize(ed);

        // Far right of the composed line: the paragraph's real end, never the composed length.
        Assert.Equal(4, Hit(ed, 380, 8).Offset);
    }

    // Text before the composition is unaffected.
    [AvaloniaFact]
    public void ClickingBeforeTheComposition_IsUnchanged()
    {
        var (ed, p) = OneLine("abcd");
        PlaceCaret(ed, p, 2);
        int plain = Hit(ed, 12, 8).Offset;

        SetPreedit(ed, "XYZXYZ");
        Realize(ed);

        Assert.Equal(plain, Hit(ed, 12, 8).Offset);
    }

    // Inside a cell, the walk advanced by the plain height while the cell's rect grew with the
    // composition, so blocks below the composing paragraph were hit-tested at the wrong y.
    [AvaloniaFact]
    public void ComposingInACell_StillHitsTheParagraphBelowIt()
    {
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 150;
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        var first = new Paragraph(); first.Inlines.Add(new Run { Text = "짧게" });
        var second = new Paragraph(); second.Inlines.Add(new Run { Text = "second" });
        cell.Blocks.Add(first); cell.Blocks.Add(second);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Realize(ed);
        PlaceCaret(ed, first, 2);

        // The y at which clicks start landing on the second paragraph, before and during composition.
        double Boundary()
        {
            for (double y = 2; y < ed.DesiredSize.Height; y += 1)
                if (ReferenceEquals(Hit(ed, 30, y).Paragraph, second)) return y;
            return double.MaxValue;
        }

        double before = Boundary();
        SetPreedit(ed, "가나다라마바사아자차카타파하가나다라마바사아자차카타파하");
        Realize(ed);
        double during = Boundary();

        // The composition makes the first paragraph taller, so the boundary has to move DOWN with it.
        // The walk used to advance by the plain height, leaving the boundary where it was — clicks on
        // the composition's own wrapped lines were attributed to the paragraph below.
        Assert.True(during > before, $"boundary must follow the composition ({during} vs {before})");
    }

    // ---- Shift+Tab outside a table ------------------------------------------

    [AvaloniaFact]
    public void ShiftTab_OutsideATable_Outdents_DoesNotTypeSpaces()
    {
        var (ed, p) = OneLine("abc");
        p.Indent = 40;
        PlaceCaret(ed, p, 3);

        ed.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Tab,
            KeyModifiers = KeyModifiers.Shift,
        });

        Assert.Equal("abc", p.Text()); // no four spaces appended
        Assert.Equal(20, p.Indent);
    }

    [AvaloniaFact]
    public void Tab_OutsideATable_StillTypesSpaces()
    {
        var (ed, p) = OneLine("abc");
        PlaceCaret(ed, p, 3);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab });

        Assert.Equal("abc    ", p.Text());
    }
}
