using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// C2: editing operations that merge paragraphs (Backspace/Delete at a boundary, selection delete
// across a boundary) used to leave the two boundary runs adjacent even when their formatting was
// identical, fragmenting the run list over an editing session. Merges now coalesce adjacent runs
// that share all formatting — text and offsets are unchanged, only the run count shrinks.
public class RunCoalescingTests
{
    private static int RunCount(Paragraph p) => p.Inlines.OfType<Run>().Count();

    // ---- model level: TextRange paragraph merge ----

    [Fact]
    public void ParagraphMerge_CoalescesAdjacentSameFormatRuns()
    {
        var p1 = TestHelpers.Para(new Run { Text = "ab" });
        var p2 = TestHelpers.Para(new Run { Text = "cd" });
        var doc = TestHelpers.Doc(p1, p2);

        // A zero-width range across the paragraph break merges p2 into p1.
        new TextRange(new TextPointer(p1, 2), new TextPointer(p2, 0)).Delete();

        Assert.Single(doc.Blocks);
        Assert.Equal("abcd", p1.Text());
        Assert.Equal(1, RunCount(p1)); // the two default runs merged into one
    }

    [Fact]
    public void ParagraphMerge_KeepsDifferentlyFormattedRunsSeparate()
    {
        var p1 = TestHelpers.Para(new Run { Text = "ab" });
        var p2 = TestHelpers.Para(new Run { Text = "cd", FontWeight = FontWeight.Bold });
        var doc = TestHelpers.Doc(p1, p2);

        new TextRange(new TextPointer(p1, 2), new TextPointer(p2, 0)).Delete();

        Assert.Single(doc.Blocks);
        Assert.Equal("abcd", p1.Text());
        Assert.Equal(2, RunCount(p1)); // formatting differs -> not merged
    }

    [Fact]
    public void DeleteWithinParagraph_RemovingMiddleRun_CoalescesNeighbours()
    {
        var p = TestHelpers.Para(
            new Run { Text = "a" },
            new Run { Text = "X", FontWeight = FontWeight.Bold },
            new Run { Text = "c" });
        TestHelpers.Doc(p);

        new TextRange(new TextPointer(p, 1), new TextPointer(p, 2)).Delete(); // delete "X"

        Assert.Equal("ac", p.Text());
        Assert.Equal(1, RunCount(p)); // the two default neighbours merged
    }

    [Fact]
    public void StyleToggledOnThenOff_CoalescesBackToOneRun()
    {
        var p = TestHelpers.Para(new Run { Text = "abcdef" });
        TestHelpers.Doc(p);

        new TextRange(new TextPointer(p, 2), new TextPointer(p, 4))
            .ApplyPropertyValue(r => r.FontWeight = FontWeight.Bold);   // splits into 3 runs
        Assert.Equal(3, RunCount(p));

        new TextRange(new TextPointer(p, 2), new TextPointer(p, 4))
            .ApplyPropertyValue(r => r.FontWeight = FontWeight.Normal); // all uniform again

        Assert.Equal("abcdef", p.Text());
        Assert.Equal(1, RunCount(p));
    }

    // ---- editor level: Backspace / Delete boundary merges ----

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static Paragraph Para(RichEditor ed, int i)
        => (Paragraph)ed.Document!.Blocks.Where(b => b is Paragraph).ElementAt(i);

    [AvaloniaFact]
    public void Backspace_MergeAtParagraphStart_CoalescesRuns()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>ab</p><p>cd</p>");
        ed.FocusDocumentEnd();        // caret at end of "cd"
        Press(ed, Key.Left);
        Press(ed, Key.Left);          // caret at start of "cd"
        Press(ed, Key.Back);          // merge "cd" into "ab"

        Assert.Single(ed.Document!.Blocks);
        Assert.Equal("abcd", Para(ed, 0).Text());
        Assert.Equal(1, RunCount(Para(ed, 0)));
    }

    [AvaloniaFact]
    public void Delete_MergeAtParagraphEnd_CoalescesRuns()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>ab</p><p>cd</p>");
        ed.FocusDocumentEnd();
        Press(ed, Key.Home, KeyModifiers.Control); // caret at start of "ab"
        Press(ed, Key.Right);
        Press(ed, Key.Right);                      // caret at end of "ab"
        Press(ed, Key.Delete);                     // merge "cd" into "ab"

        Assert.Single(ed.Document!.Blocks);
        Assert.Equal("abcd", Para(ed, 0).Text());
        Assert.Equal(1, RunCount(Para(ed, 0)));
    }

    [AvaloniaFact]
    public void Delete_MiddleStyledRun_CoalescesViaEditPipeline()
    {
        var p = TestHelpers.Para(
            new Run { Text = "a" },
            new Run { Text = "X", FontWeight = FontWeight.Bold },
            new Run { Text = "c" });
        var ed = new RichEditor();
        ed.Document = TestHelpers.Doc(p);
        ed.FocusDocumentEnd();
        Press(ed, Key.Home, KeyModifiers.Control); // caret at paragraph start
        Press(ed, Key.Right);                      // caret after "a" (offset 1)
        Press(ed, Key.Delete);                     // delete the bold "X"

        Assert.Equal("ac", Para(ed, 0).Text());
        Assert.Equal(1, RunCount(Para(ed, 0)));
    }
}
