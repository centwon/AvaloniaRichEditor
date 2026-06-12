using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Drives the real OnKeyDown/OnTextInput pipeline via routed events to cover the private editing
// paths (DeleteLocalText, paragraph merge, SplitParagraphAtCaret, typing undo coalescing) that
// public commands don't reach — the remaining N4 gap ahead of N6-2.
// Layout-dependent keys (Home/End/Up/Down — they hit-test pixel positions) are avoided; caret is
// positioned with Ctrl+Home (document edge) and Left/Right (pure offset moves).
public class RichEditorKeyInputTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string text)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    private static RichEditor Editor(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        ed.FocusDocumentEnd();
        return ed;
    }

    private static Paragraph Para(RichEditor ed, int index)
        => (Paragraph)ed.Document!.Blocks.Where(b => b is Paragraph).ElementAt(index);

    [AvaloniaFact]
    public void Backspace_DeletesCharBeforeCaret()
    {
        var ed = Editor("<p>abc</p>");
        Press(ed, Key.Back);
        Assert.Equal("ab", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Backspace_AtParagraphStart_MergesIntoPrevious()
    {
        var ed = Editor("<p>ab</p><p>cd</p>");
        Press(ed, Key.Back); // d
        Press(ed, Key.Back); // c -> second paragraph now empty, caret at offset 0
        Press(ed, Key.Back); // merge into "ab"
        Assert.Single(ed.Document!.Blocks);
        Assert.Equal("ab", Para(ed, 0).Text());

        Press(ed, Key.Back); // keeps deleting in the merged paragraph
        Assert.Equal("a", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Delete_AtParagraphEnd_MergesNextParagraph()
    {
        var ed = Editor("<p>ab</p><p>cd</p>");
        Press(ed, Key.Home, KeyModifiers.Control); // start of document
        Press(ed, Key.Right);
        Press(ed, Key.Right);                      // end of "ab"
        Press(ed, Key.Delete);                     // forward-merge "cd"

        Assert.Single(ed.Document!.Blocks);
        Assert.Equal("abcd", Para(ed, 0).Text());

        ed.Undo(); // discrete deletes push a checkpoint each — merge must be undoable
        Assert.Equal(2, ed.Document!.Blocks.Count);
    }

    [AvaloniaFact]
    public void Delete_InsideParagraph_DeletesCharAfterCaret()
    {
        var ed = Editor("<p>abc</p>");
        Press(ed, Key.Home, KeyModifiers.Control);
        Press(ed, Key.Delete);
        Assert.Equal("bc", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Enter_SplitsParagraphAtCaret()
    {
        var ed = Editor("<p>abcd</p>");
        Press(ed, Key.Home, KeyModifiers.Control);
        Press(ed, Key.Right);
        Press(ed, Key.Right); // between b and c
        Press(ed, Key.Enter);

        Assert.Equal(2, ed.Document!.Blocks.Count);
        Assert.Equal("ab", Para(ed, 0).Text());
        Assert.Equal("cd", Para(ed, 1).Text());
    }

    [AvaloniaFact]
    public void Enter_OnHeading_NewParagraphIsBodyText()
    {
        var ed = Editor("<h1>Title</h1>");
        Press(ed, Key.Enter); // at end of the heading

        Assert.Equal(2, ed.Document!.Blocks.Count);
        Assert.Equal(1, Para(ed, 0).HeadingLevel);
        Assert.Equal(0, Para(ed, 1).HeadingLevel); // heading level resets to body
    }

    [AvaloniaFact]
    public void Enter_OnEmptyListItem_ExitsListInsteadOfSplitting()
    {
        var ed = Editor("<ul><li>item</li></ul>");
        Press(ed, Key.Enter); // new list paragraph
        Assert.Equal(ListKind.Bullet, Para(ed, 1).ListType); // list style is inherited

        Press(ed, Key.Enter); // Enter on the empty item leaves the list, no new paragraph
        Assert.Equal(2, ed.Document!.Blocks.Count);
        Assert.Equal(ListKind.None, Para(ed, 1).ListType);
    }

    [AvaloniaFact]
    public void Typing_CoalescesKeystrokesIntoSingleUndo()
    {
        var ed = Editor("<p>x</p>");
        Type(ed, "a");
        Type(ed, "b");
        Type(ed, "c");
        Assert.Equal("xabc", Para(ed, 0).Text());

        ed.Undo(); // one checkpoint for the whole typing run, not one per keystroke
        Assert.Equal("x", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Typing_AfterCaretMove_StartsNewUndoRun()
    {
        var ed = Editor("<p>x</p>");
        Type(ed, "a");
        Press(ed, Key.Left); // caret move ends the typing run
        Press(ed, Key.Right);
        Type(ed, "b");
        Assert.Equal("xab", Para(ed, 0).Text());

        ed.Undo();
        Assert.Equal("xa", Para(ed, 0).Text()); // only the second run is undone
        ed.Undo();
        Assert.Equal("x", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Backspace_CoalescesConsecutiveDeletesIntoSingleUndo()
    {
        var ed = Editor("<p>abcd</p>");
        Press(ed, Key.Back);
        Press(ed, Key.Back);
        Press(ed, Key.Back);
        Assert.Equal("a", Para(ed, 0).Text());

        ed.Undo(); // one checkpoint for the whole backspace run, not one per keypress
        Assert.Equal("abcd", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Backspace_AfterCaretMove_StartsNewUndoRun()
    {
        var ed = Editor("<p>abcd</p>");
        Press(ed, Key.Back);        // "abc"
        Press(ed, Key.Left);        // caret move ends the run
        Press(ed, Key.Right);
        Press(ed, Key.Back);        // "ab" — new run
        Assert.Equal("ab", Para(ed, 0).Text());

        ed.Undo();
        Assert.Equal("abc", Para(ed, 0).Text()); // only the second run is undone
        ed.Undo();
        Assert.Equal("abcd", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void Backspace_RemovesWholeSurrogatePair()
    {
        // A 1-unit delete used to leave a lone surrogate half behind (broken glyph).
        var ed = Editor("<p>a</p>");
        Type(ed, "\U0001F600"); // 😀 = surrogate pair (2 UTF-16 units)
        Press(ed, Key.Back);
        Assert.Equal("a", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void ArrowsAndDelete_TreatSurrogatePairAsOneCharacter()
    {
        var ed = Editor("<p>a</p>");
        Type(ed, "\U0001F600");
        Type(ed, "b");        // "a😀b", caret at offset 4
        Press(ed, Key.Left);  // before 'b' (3)
        Press(ed, Key.Left);  // one step crosses the whole pair (1)
        Press(ed, Key.Delete);
        Assert.Equal("ab", Para(ed, 0).Text());
    }

    [AvaloniaFact]
    public void ToggleBold_CaretInsideWord_BoldsOnlyThatWord()
    {
        var ed = Editor("<p>hello world</p>");
        Press(ed, Key.Home, KeyModifiers.Control);
        Press(ed, Key.Right);
        Press(ed, Key.Right); // caret inside "hello"
        ed.ToggleBold();

        var runs = Para(ed, 0).Inlines.OfType<Run>().ToList();
        Assert.Equal("hello", string.Concat(runs.Where(r => r.FontWeight == FontWeight.Bold).Select(r => r.Text)));
        Assert.Contains(runs, r => r.FontWeight == FontWeight.Normal && r.Text!.Contains("world"));
    }

    [AvaloniaFact]
    public void ToggleBold_AtEmptyPosition_AppliesToNextTypedText()
    {
        var ed = Editor("<p>ab </p>"); // caret after the space — not inside a word
        ed.ToggleBold();               // pending: nothing in the document changes yet
        Assert.All(Para(ed, 0).Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Normal, r.FontWeight));

        Type(ed, "c");
        var runs = Para(ed, 0).Inlines.OfType<Run>().ToList();
        Assert.Equal("c", string.Concat(runs.Where(r => r.FontWeight == FontWeight.Bold).Select(r => r.Text)));
        Assert.Contains(runs, r => r.FontWeight == FontWeight.Normal && r.Text!.StartsWith("ab"));
    }

    [AvaloniaFact]
    public void PendingCaretFormat_ClearsOnCaretMove()
    {
        var ed = Editor("<p>ab </p>");
        ed.ToggleBold();      // pending
        Press(ed, Key.Left);  // caret move cancels it
        Press(ed, Key.Right);
        Type(ed, "c");
        Assert.All(Para(ed, 0).Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Normal, r.FontWeight));
    }

    [AvaloniaFact]
    public void ShiftEnter_InsertsSoftLineBreakInsteadOfSplitting()
    {
        var ed = Editor("<p>ab</p>");
        Press(ed, Key.Enter, KeyModifiers.Shift);
        Type(ed, "c");

        Assert.Single(ed.Document!.Blocks);          // no paragraph split
        Assert.Equal("ab\nc", Para(ed, 0).Text());   // hard line break inside the run
    }

    [AvaloniaFact]
    public void AutoLink_SpaceAfterUrl_LinksUrlButNotTheSpace()
    {
        var ed = Editor("<p></p>");
        Type(ed, "see https://a.com");
        Type(ed, " ");
        Type(ed, "x");

        var runs = Para(ed, 0).Inlines.OfType<Run>().ToList();
        var linked = runs.Where(r => !string.IsNullOrEmpty(r.NavigateUri)).ToList();
        Assert.Equal("https://a.com", string.Concat(linked.Select(r => r.Text)));
        Assert.All(linked, r => Assert.Equal("https://a.com", r.NavigateUri));
        // The typed space and following text stay outside the link.
        Assert.Contains(runs, r => string.IsNullOrEmpty(r.NavigateUri) && r.Text!.Contains(" x"));
    }

    [AvaloniaFact]
    public void AutoList_PrefixSplitAcrossRuns_LeavesNoResidue()
    {
        // "1" and "." in separate runs (e.g. different formatting): the prefix delete must
        // span run boundaries — a single-run delete used to leave the "." behind.
        var ed = Editor("<p></p>");
        var p = Para(ed, 0);
        p.Inlines.Clear();
        p.Inlines.Add(new Run { Text = "1", Parent = p });
        p.Inlines.Add(new Run { Text = ".", Parent = p });
        ed.FocusDocumentEnd();
        Type(ed, " "); // triggers the auto-list conversion

        Assert.Equal(ListKind.Ordered, p.ListType);
        Assert.Equal("", p.Text());
    }

    [AvaloniaFact]
    public void ReadOnly_IgnoresTypingAndDeletion()
    {
        var ed = Editor("<p>abc</p>");
        ed.IsReadOnly = true;
        Type(ed, "z");
        Press(ed, Key.Back);
        Assert.Equal("abc", Para(ed, 0).Text());
    }
}
