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

/// <summary>Caret affinity at a soft wrap, backported from the WinUI peer.
/// <para>At a wrap, "end of line k" and "start of line k+1" are the SAME offset, and a layout answers
/// with the leading one. When a line wraps at a space that is invisible — End trims the space and the
/// caret sits before it, on the earlier line. When a line wraps mid-word, with no whitespace to trim,
/// there is nothing to hide behind: measured before this change, pressing End moved the caret from
/// y=116 to y=131 and back to x=10, the left margin. The caret jumped to the START OF THE NEXT LINE.</para>
/// <para><see cref="TextPointer.AtLineEnd"/> is the affinity, and it is display-only: equality and
/// ordering ignore it, so nothing that compares positions changes behaviour.</para>
/// </summary>
public class CaretLineAffinityTests
{
    private const double W = 600, H = 800;
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string text)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    private static void ForceLayout(RichEditor ed)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        new RenderTargetBitmap(new PixelSize((int)W, (int)H)).Render(ed);
    }

    private static Point Caret(RichEditor ed) => (Point)typeof(RichEditor).GetField("_lastCaretPoint", NP)!.GetValue(ed)!;
    private static TextPointer Pos(RichEditor ed) => (TextPointer)typeof(RichEditor).GetField("_caretPosition", NP)!.GetValue(ed)!;

    // A paragraph that wraps mid-word: no whitespace at the break, so the ambiguity cannot be dodged.
    private static RichEditor WrappedWord()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>" + new string('A', 400) + "</p>");
        ed.FocusDocumentEnd();
        ForceLayout(ed);
        return ed;
    }

    // Puts the caret at the start of a middle visual line and returns that line's y.
    private static double GoToMiddleLineStart(RichEditor ed)
    {
        Press(ed, Key.Home);
        Press(ed, Key.Up);
        Press(ed, Key.Home);
        ForceLayout(ed);
        return Caret(ed).Y;
    }

    [AvaloniaFact]
    public void End_OnAMidWordWrap_KeepsTheCaretOnThatLine()
    {
        var ed = WrappedWord();
        double lineY = GoToMiddleLineStart(ed);
        double leftX = Caret(ed).X;

        Press(ed, Key.End);
        ForceLayout(ed);

        Assert.Equal(lineY, Caret(ed).Y, 1);                       // same visual line, not the next one
        Assert.True(Caret(ed).X > leftX + 100,
            $"the caret did not move to the line's right edge (x {leftX:0} -> {Caret(ed).X:0})");
        Assert.True(Pos(ed).AtLineEnd, "End did not set the affinity");
    }

    // The offset is the boundary either way; what changes is which side of it the caret is drawn on.
    // Typing still inserts at that offset, so the affinity cannot move text around.
    [AvaloniaFact]
    public void TypingAfterEnd_InsertsAtTheBoundary()
    {
        var ed = WrappedWord();
        GoToMiddleLineStart(ed);
        Press(ed, Key.End);
        int at = Pos(ed).Offset;

        Type(ed, "X");

        var text = ((Paragraph)ed.Document!.Blocks.First(b => b is Paragraph)).Text();
        Assert.Equal(at, text.IndexOf('X'));
    }

    // Vertical movement asks which line the caret is on. With the affinity set that is the EARLIER line,
    // so Up has to land one line above it — asking about the raw offset would answer with the next line
    // and Up would only get back to where the caret already looks.
    [AvaloniaFact]
    public void UpFromAnAffinityCaret_LeavesTheLine()
    {
        var ed = WrappedWord();
        GoToMiddleLineStart(ed);
        Press(ed, Key.End);
        ForceLayout(ed);
        double atEndY = Caret(ed).Y;

        Press(ed, Key.Up);
        ForceLayout(ed);

        Assert.True(Caret(ed).Y < atEndY - 5,
            $"Up did not leave the line the caret was displayed on ({atEndY:0.0} -> {Caret(ed).Y:0.0})");
    }

    // A wrap at a space keeps working the way it did: End trims the space, so the caret sits before it
    // on the earlier line and needs no affinity at all.
    [AvaloniaFact]
    public void AWrapAtASpace_StillLandsBeforeTheSpace()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>" + string.Join(" ", Enumerable.Repeat("word", 90)) + "</p>");
        ed.FocusDocumentEnd();
        ForceLayout(ed);
        double lineY = GoToMiddleLineStart(ed);

        Press(ed, Key.End);
        ForceLayout(ed);
        int at = Pos(ed).Offset;
        var text = ((Paragraph)ed.Document!.Blocks.First(b => b is Paragraph)).Text();

        Assert.Equal(lineY, Caret(ed).Y, 1);
        Assert.False(char.IsWhiteSpace(text[at - 1]), "End landed after the wrap's space");
    }

    // Display-only: two pointers at the same place are equal and compare equal whatever their affinity,
    // so selection and ordering are untouched by it.
    [AvaloniaFact]
    public void AffinityDoesNotAffectEqualityOrOrder()
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "hello" });
        var lead = new TextPointer(p, 3);
        var trail = new TextPointer(p, 3) { AtLineEnd = true };

        Assert.Equal(lead, trail);
        Assert.True(lead == trail);
        Assert.Equal(0, lead.CompareTo(trail));
    }

    [AvaloniaFact]
    public void AFreshPointerHasNoAffinity()
    {
        Assert.False(new TextPointer(new Paragraph(), 0).AtLineEnd);
    }
}
