using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A host that edits the model it assigned — Document.Blocks / Paragraph.Inlines / Run.Text — instead of going
// through the editor's commands. The model raises no change notifications (Inlines and a cell's Blocks are plain
// lists), so the host tells the editor with NotifyDocumentChanged. Measured without it (2026-10-17): the caret kept
// a removed paragraph and the next typed text went into it, an added paragraph's Enter became a line break (its
// Parent was never wired, so the split found no container), and a table added at the end had no paragraph after it,
// so typing at the end went into its last cell. A re-measure after a Run.Text change was already right.
public class HostModelMutationTests
{
    private static string TextOf(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    private static Paragraph Para(string text) => new Paragraph { Inlines = { new Run { Text = text } } };

    private static double Measure(RichEditor ed, double width = 400)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        return ed.DesiredSize.Height;
    }

    [AvaloniaFact]
    public void RunTextChanged_ThenHostInvalidatesMeasure_HeightGrows()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var run = new Run { Text = "short" };
        doc.Blocks.Add(new Paragraph { Inlines = { run } });
        ed.Document = doc;
        double before = Measure(ed);

        run.Text = string.Join(" ", Enumerable.Repeat("long text that wraps", 40));
        ed.InvalidateMeasure();
        double after = Measure(ed);

        Assert.True(after > before * 3, $"before {before}, after {after}");
    }

    [AvaloniaFact]
    public void CaretParagraphRemoved_ThenTyping_TextIsInTheDocument()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        doc.Blocks.Add(Para("one"));
        doc.Blocks.Add(Para("two"));
        ed.Document = doc;
        ed.FocusDocumentEnd();

        doc.Blocks.RemoveAt(1);
        ed.NotifyDocumentChanged();
        ed.InsertText("X");

        Assert.Equal("oneX", ed.GetPlainText());
    }

    [AvaloniaFact]
    public void ParagraphAppended_ThenEnterAtItsEnd_SplitsIt()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        doc.Blocks.Add(Para("one"));
        ed.Document = doc;
        using var host = InteractionHost.Create(ed);

        doc.Blocks.Add(Para("two"));
        ed.NotifyDocumentChanged();
        ed.FocusDocumentEnd();
        host.Key(Key.Enter);
        ed.InsertText("three");

        Assert.Equal(new[] { "one", "two", "three" }, doc.Blocks.OfType<Paragraph>().Select(TextOf).ToArray());
    }

    [AvaloniaFact]
    public void TableAppended_ThenTypingAtTheEnd_TextGoesAfterTheTable()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        doc.Blocks.Add(Para("one"));
        ed.Document = doc;

        doc.Blocks.Add(new TableBlock(1, 1));
        ed.NotifyDocumentChanged();
        ed.FocusDocumentEnd();
        ed.InsertText("after");

        Assert.IsType<Paragraph>(doc.Blocks.Last());
        Assert.Equal("after", TextOf((Paragraph)doc.Blocks.Last()));
    }

    // A paragraph that is still there keeps the caret, its offset clamped to the paragraph's new length.
    [AvaloniaFact]
    public void CaretParagraphShortened_CaretStaysInItAtItsNewEnd()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var run = new Run { Text = "hello world" };
        doc.Blocks.Add(Para("first"));
        doc.Blocks.Add(new Paragraph { Inlines = { run } });
        ed.Document = doc;
        ed.FocusDocumentEnd(); // offset 11

        run.Text = "hi";
        ed.NotifyDocumentChanged();
        ed.InsertText("!");

        Assert.Equal("first", TextOf((Paragraph)doc.Blocks[0]));
        Assert.Equal("hi!", TextOf((Paragraph)doc.Blocks[1]));
    }

    // The user types, the host edits, the user types again: undo takes back only the second typing, so the host's
    // edit survives one Ctrl+Z (the notification ends the coalescing run that would have swallowed it).
    [AvaloniaFact]
    public void TypingRunAcrossAHostEdit_UndoKeepsTheHostEdit()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        doc.Blocks.Add(Para("one"));
        ed.Document = doc;
        using var host = InteractionHost.Create(ed);
        ed.FocusDocumentEnd();
        host.Type("a");

        doc.Blocks.Add(Para("host"));
        ed.NotifyDocumentChanged(); // the caret stays after "a": no caret move to end the run
        host.Type("b");
        ed.Undo();

        // Undo swaps a snapshot in, so read the editor's document, not `doc`.
        Assert.Equal(new[] { "onea", "host" }, ed.Document!.Blocks.OfType<Paragraph>().Select(TextOf).ToArray());
    }

    // InsertText pushed no undo checkpoint: Ctrl+Z after a host's insert undid the edit BEFORE it, taking the
    // inserted text along — or, with nothing before it, did nothing at all.
    [AvaloniaFact]
    public void InsertText_IsItsOwnUndoStep()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        doc.Blocks.Add(Para("one"));
        ed.Document = doc;
        ed.FocusDocumentEnd();

        ed.InsertText(" two");
        ed.InsertText(" three");
        ed.Undo();

        Assert.Equal("one two", ed.GetPlainText());
        ed.Undo();
        Assert.Equal("one", ed.GetPlainText());
    }
}
