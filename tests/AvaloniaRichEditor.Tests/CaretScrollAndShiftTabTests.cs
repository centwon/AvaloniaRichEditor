using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Two things reported from the demo: typing never scrolled the caret back into view (worst inside a
// table cell, which grows downward as its content does), and Shift+Tab did nothing after a Tab.
public class CaretScrollAndShiftTabTests
{
    private static void Render(RichEditor ed, double w = 400)
    {
        ed.Measure(new Size(w, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, w, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)w, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
        Dispatcher.UIThread.RunJobs(); // BringIntoView is posted, not called inline
    }

    private static void Press(RichEditor ed, Key k, KeyModifiers m = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = k, KeyModifiers = m });

    private static void Type(RichEditor ed, string t)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = t });

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, off));
    }

    // BringIntoView raises a routed request; catching it is how we observe "the editor asked to scroll"
    // without a templated ScrollViewer, which headless has no theme for.
    private static List<Rect> WatchScrollRequests(RichEditor ed)
    {
        var rects = new List<Rect>();
        ed.AddHandler(Control.RequestBringIntoViewEvent,
            (object? _, RequestBringIntoViewEventArgs e) => rects.Add(e.TargetRect));
        return rects;
    }

    // ---- caret scrolling ----------------------------------------------------

    [AvaloniaFact]
    public void TypingAsksToScrollTheCaretIntoView()
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "x" });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        PlaceCaret(ed, p, 1);
        ed.Focus();
        Render(ed);

        var requests = WatchScrollRequests(ed);
        Type(ed, "a");
        Render(ed);

        Assert.NotEmpty(requests);
    }

    [AvaloniaFact]
    public void TypingInsideACell_AsksToScrollAndTracksTheCaretDown()
    {
        var tb = new TableBlock(1, 1);
        tb.ColumnWidths[0] = 120; // narrow, so typed text wraps and the row keeps growing
        var cellPara = tb.Cells[0][0].Para;
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        PlaceCaret(ed, cellPara, 0);
        ed.Focus();
        Render(ed);

        var requests = WatchScrollRequests(ed);
        for (int i = 0; i < 20; i++) { Type(ed, "가나다라마 "); Render(ed); }

        Assert.NotEmpty(requests);
        // The last request must follow the caret down, not sit at the top of the cell.
        Assert.True(requests[^1].Y > requests[0].Y,
            $"the scroll target must track the caret ({requests[^1].Y} vs {requests[0].Y})");
    }

    // Enter already worked; keep it pinned so the typing fix can't regress it.
    [AvaloniaFact]
    public void EnterStillAsksToScrollTheCaretIntoView()
    {
        var tb = new TableBlock(1, 1);
        var cellPara = tb.Cells[0][0].Para;
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        PlaceCaret(ed, cellPara, 0);
        ed.Focus();
        Render(ed);

        var requests = WatchScrollRequests(ed);
        Press(ed, Key.Enter);
        Render(ed);

        Assert.NotEmpty(requests);
    }

    // Typing must still coalesce into ONE undo step — the scroll flag had to be set without going
    // through ResetCaretBlink, which ends the run.
    [AvaloniaFact]
    public void TypingStillCoalescesIntoOneUndoStep()
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "" });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        PlaceCaret(ed, p, 0);
        ed.Focus();

        Type(ed, "a"); Type(ed, "b"); Type(ed, "c");
        Render(ed);
        Assert.Equal("abc", p.Text());

        ed.Undo();
        var after = ed.Document!.Blocks.OfType<Paragraph>().First();
        Assert.Equal("", after.Text());   // one step took all three characters
        Assert.False(ed.CanUndo);
    }

    // ---- Shift+Tab undoes Tab ----------------------------------------------

    private static (RichEditor ed, Paragraph p) Line(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        return (ed, p);
    }

    [AvaloniaFact]
    public void ShiftTab_RemovesTheSpacesTabTyped()
    {
        var (ed, p) = Line("abc");
        PlaceCaret(ed, p, 3);

        Press(ed, Key.Tab);                          // "abc    "
        Assert.Equal("abc    ", p.Text());
        Press(ed, Key.Tab, KeyModifiers.Shift);      // ...and back

        Assert.Equal("abc", p.Text());
    }

    [AvaloniaFact]
    public void ShiftTab_RemovesAtMostFourSpaces()
    {
        var (ed, p) = Line("abc      "); // six trailing spaces
        PlaceCaret(ed, p, 9);

        Press(ed, Key.Tab, KeyModifiers.Shift);

        Assert.Equal("abc  ", p.Text()); // four removed, two left
    }

    // With no spaces to give back it still outdents the paragraph, so an indent set from the toolbar
    // is reachable from the keyboard.
    [AvaloniaFact]
    public void ShiftTab_WithNoSpaces_OutdentsTheParagraph()
    {
        var (ed, p) = Line("abc");
        p.Indent = 40;
        PlaceCaret(ed, p, 3);

        Press(ed, Key.Tab, KeyModifiers.Shift);

        Assert.Equal("abc", p.Text());
        Assert.Equal(20, p.Indent);
    }

    [AvaloniaFact]
    public void Tab_OutsideATable_StillTypesSpaces()
    {
        var (ed, p) = Line("abc");
        PlaceCaret(ed, p, 3);

        Press(ed, Key.Tab);

        Assert.Equal("abc    ", p.Text());
    }
}
