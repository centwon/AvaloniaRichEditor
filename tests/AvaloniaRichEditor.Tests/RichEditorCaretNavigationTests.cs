using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Covers the layout-dependent caret keys (Home/End/Up/Down) that the offset-only key tests avoid:
// they hit-test pixel positions and read _lastCaretPoint, which is only populated by a real Render
// pass. We force that pass with RenderTargetBitmap (no top-level Window — same path the print tests
// use), so the caret's visual line geometry is real. Caret position is asserted indirectly by typing
// a marker and checking where it lands, avoiding any private-state access.
public class RichEditorCaretNavigationTests
{
    private const double W = 600, H = 800;

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string text)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    private static Paragraph Para(RichEditor ed, int index)
        => (Paragraph)ed.Document!.Blocks.Where(b => b is Paragraph).ElementAt(index);

    // Measure/Arrange then render to a bitmap so Render() runs: this builds the layout cache and sets
    // _lastCaretPoint to the caret's current visual position, which Home/End/Up/Down read.
    private static void ForceLayout(RichEditor ed)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        var rtb = new RenderTargetBitmap(new PixelSize((int)W, (int)H));
        rtb.Render(ed);
    }

    private static RichEditor Editor(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        ed.FocusDocumentEnd();
        return ed;
    }

    [AvaloniaFact]
    public void Home_MovesCaretToLineStart()
    {
        var ed = Editor("<p>hello world</p>"); // caret at end of the single line
        ForceLayout(ed);
        Press(ed, Key.Home);
        Type(ed, "X");
        Assert.Equal("Xhello world", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void End_MovesCaretToLineEnd()
    {
        var ed = Editor("<p>hello world</p>");
        Press(ed, Key.Home, KeyModifiers.Control); // caret to document start
        ForceLayout(ed);
        Press(ed, Key.End);
        Type(ed, "X");
        Assert.Equal("hello worldX", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void End_OnWrappedLine_StaysAtVisualLineEnd_DoesNotSplitNextLineWord()
    {
        // A long paragraph wraps into multiple visual lines at W=600. End from the first visual line
        // must land at the END of that line (a word/space boundary), not one char into the next line.
        // The old far-right hit-test returned the next line's leading position (trailing) and overshot,
        // splitting the next line's first word (e.g. "this" -> "tXhis").
        var ed = Editor("<p>Hello Avalonia this is a long paragraph that will certainly wrap onto a " +
                        "second visual line here and keep going well past the right edge of the width</p>");
        Press(ed, Key.Home, KeyModifiers.Control); // caret to start (visual line 0)
        ForceLayout(ed);
        Press(ed, Key.End);
        Type(ed, "X");
        var text = Para(ed, 0).Text();
        int idx = text.IndexOf('X');
        Assert.True(idx > 0 && idx < text.Length - 1, $"X should be mid-paragraph (a wrapped line end), was at {idx}: '{text}'");
        Assert.False(char.IsLetter(text[idx - 1]) && char.IsLetter(text[idx + 1]),
            $"End split a word ('{text[idx - 1]}X{text[idx + 1]}') — caret overshot onto the next visual line: '{text}'");
    }

    [AvaloniaFact]
    public void Down_MovesCaretToParagraphBelow()
    {
        var ed = Editor("<p>aaa</p><p>bbb</p><p>ccc</p>");
        Press(ed, Key.Home, KeyModifiers.Control); // start of "aaa" (top line)
        ForceLayout(ed);
        Press(ed, Key.Down);
        Type(ed, "M");
        Assert.Equal("aaa", Para(ed, 0).Text());   // top line untouched
        Assert.Contains("M", Para(ed, 1).Text());  // caret descended exactly one line
        Assert.Equal("ccc", Para(ed, 2).Text());   // did not overshoot
    }

    [AvaloniaFact]
    public void Up_FromBottomLine_LandsInMiddleLine()
    {
        // Caret at end of "ccc" (bottom). One Up must land in the *middle* line, not clamp to the top:
        // this only holds if Render populated _lastCaretPoint.Y with the bottom line's real position.
        var ed = Editor("<p>aaa</p><p>bbb</p><p>ccc</p>");
        ForceLayout(ed);
        Press(ed, Key.Up);
        Type(ed, "M");
        Assert.Equal("aaa", Para(ed, 0).Text());   // did not jump to the top line
        Assert.Contains("M", Para(ed, 1).Text());  // rose exactly one line
        Assert.Equal("ccc", Para(ed, 2).Text());   // left the starting line
    }

    [AvaloniaFact]
    public void ShiftHome_SelectsToLineStart()
    {
        // Shift+Home extends the selection from the caret to the line start; Backspace then clears it.
        var ed = Editor("<p>hello</p>");
        ForceLayout(ed);
        Press(ed, Key.Home, KeyModifiers.Shift);
        Press(ed, Key.Back); // deletes the selected "hello"
        Assert.Equal("", Para(ed, 0).Text());
    }
}
