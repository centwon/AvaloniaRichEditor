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

// (5) Paragraph-level commands apply to the whole selection, like the list commands already did, and
// (6) a Backspace/Delete that cannot change anything leaves no undo step behind.
public class ParagraphCommandScopeTests
{
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static Paragraph NewPara(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        return p;
    }

    private static void SetField(RichEditor ed, string name, TextPointer tp)
        => typeof(RichEditor).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(ed, tp);

    private static void Select(RichEditor ed, Paragraph from, int fromOff, Paragraph to, int toOff)
    {
        SetField(ed, "_selectionStart", new TextPointer(from, fromOff));
        SetField(ed, "_selectionEnd", new TextPointer(to, toOff));
        SetField(ed, "_caretPosition", new TextPointer(to, toOff));
    }

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            SetField(ed, n, new TextPointer(p, off));
    }

    // ---- (5) selection scope ------------------------------------------------

    private static (RichEditor ed, Paragraph a, Paragraph b, Paragraph c) ThreeParagraphs()
    {
        var a = NewPara("one");
        var b = NewPara("two");
        var c = NewPara("three");
        var ed = new RichEditor { Document = TestHelpers.Doc(a, b, c) };
        Realize(ed);
        Select(ed, a, 0, c, 5);
        return (ed, a, b, c);
    }

    [AvaloniaFact]
    public void SetTextAlignment_AppliesToEverySelectedParagraph()
    {
        var (ed, a, b, c) = ThreeParagraphs();
        ed.SetTextAlignment(TextAlignment.Center);
        Assert.All(new[] { a, b, c }, p => Assert.Equal(TextAlignment.Center, p.TextAlignment));
    }

    [AvaloniaFact]
    public void SetHeading_AppliesToEverySelectedParagraph()
    {
        var (ed, a, b, c) = ThreeParagraphs();
        ed.SetHeading(2);
        Assert.All(new[] { a, b, c }, p => Assert.Equal(2, p.HeadingLevel));
    }

    [AvaloniaFact]
    public void SetLineSpacing_AppliesToEverySelectedParagraph()
    {
        var (ed, a, b, c) = ThreeParagraphs();
        ed.SetLineSpacing(1.5);
        Assert.All(new[] { a, b, c }, p => Assert.Equal(1.5, p.LineSpacing));
    }

    // Indent is a delta, so each paragraph moves from its own starting value.
    [AvaloniaFact]
    public void Indent_ShiftsEverySelectedParagraphByTheDelta()
    {
        var (ed, a, b, c) = ThreeParagraphs();
        b.Indent = 40;
        ed.Indent(20);
        Assert.Equal(20, a.Indent);
        Assert.Equal(60, b.Indent);
        Assert.Equal(20, c.Indent);
    }

    // A mixed selection ends up uniform: the caret paragraph decides the direction for all of them.
    [AvaloniaFact]
    public void ToggleQuote_AppliesTheCaretsDirectionToTheWholeSelection()
    {
        var (ed, a, b, c) = ThreeParagraphs();
        b.IsQuote = true; // mixed to start with; the caret sits in `c` (IsQuote false) -> turn all on
        ed.ToggleQuote();
        Assert.All(new[] { a, b, c }, p => Assert.True(p.IsQuote));
    }

    // Cell paragraphs are reachable too — the commands must not be top-level only.
    [AvaloniaFact]
    public void SetTextAlignment_ReachesParagraphsInsideACell()
    {
        var tb = new TableBlock(1, 1);
        var p1 = NewPara("one");
        var p2 = NewPara("two");
        tb.Cells[0][0].Blocks.Clear();
        tb.Cells[0][0].Blocks.Add(p1);
        tb.Cells[0][0].Blocks.Add(p2);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        Select(ed, p1, 0, p2, 3);

        ed.SetTextAlignment(TextAlignment.Right);

        Assert.Equal(TextAlignment.Right, p1.TextAlignment);
        Assert.Equal(TextAlignment.Right, p2.TextAlignment);
    }

    // With no selection the command still applies to exactly the caret paragraph.
    [AvaloniaFact]
    public void SetTextAlignment_WithNoSelection_TouchesOnlyTheCaretParagraph()
    {
        var (ed, a, b, c) = ThreeParagraphs();
        PlaceCaret(ed, b, 1);
        ed.SetTextAlignment(TextAlignment.Center);
        Assert.Equal(TextAlignment.Left, a.TextAlignment);
        Assert.Equal(TextAlignment.Center, b.TextAlignment);
        Assert.Equal(TextAlignment.Left, c.TextAlignment);
    }

    // ---- (6) no undo step for a delete that does nothing --------------------

    [AvaloniaFact]
    public void BackspaceAtDocumentStart_LeavesNoUndoStep()
    {
        var p = NewPara("hello");
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        Realize(ed);
        ed.MarkSaved();
        PlaceCaret(ed, p, 0);

        Press(ed, Key.Back);

        Assert.False(ed.CanUndo);
        Assert.False(ed.IsModified);
        Assert.Equal("hello", p.Text());
    }

    [AvaloniaFact]
    public void DeleteAtDocumentEnd_LeavesNoUndoStep()
    {
        var p = NewPara("hello");
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        Realize(ed);
        ed.MarkSaved();
        PlaceCaret(ed, p, 5);

        Press(ed, Key.Delete);

        Assert.False(ed.CanUndo);
        Assert.False(ed.IsModified);
        Assert.Equal("hello", p.Text());
    }

    // Guard: a Backspace that DOES merge two paragraphs must still checkpoint.
    [AvaloniaFact]
    public void BackspaceAtAParagraphStart_StillCheckpoints()
    {
        var a = NewPara("one");
        var b = NewPara("two");
        var ed = new RichEditor { Document = TestHelpers.Doc(a, b) };
        Realize(ed);
        ed.MarkSaved();
        PlaceCaret(ed, b, 0);

        Press(ed, Key.Back);

        Assert.True(ed.CanUndo);
        Assert.True(ed.IsModified);
        Assert.Equal("onetwo", a.Text());
    }

    // One Enter used to push two identical checkpoints, so the first Ctrl+Z appeared to do nothing.
    [AvaloniaFact]
    public void Enter_PushesExactlyOneUndoStep()
    {
        var p = NewPara("abcd");
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        Realize(ed);
        PlaceCaret(ed, p, 2);

        Press(ed, Key.Enter);
        Assert.Equal(2, ed.Document!.Blocks.OfType<Paragraph>().Count());

        ed.Undo();

        // A single undo must fully restore the one-paragraph document.
        var paras = ed.Document!.Blocks.OfType<Paragraph>().ToList();
        Assert.Single(paras);
        Assert.Equal("abcd", paras[0].Text());
        Assert.False(ed.CanUndo);
    }
}
