using System.Collections.Generic;
using System.Linq;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>What bounds the undo history, and what the bound is measured in.
/// <para>The history used to be bounded by a step count alone, and every step is a full deep clone of
/// the document: a 20000-paragraph document retained 12.1 MB per checkpoint, so fifty steps meant
/// 592 MB of history behind an editor showing one document. That is what the byte budget is for.</para>
/// <para>The budget counts ELEMENTS, not characters, and that is the part worth guarding: Clone shares
/// the strings, so text costs a snapshot nothing — the same document at 2 and at 60 characters per
/// paragraph retained byte-for-byte the same. The WinUI peer had been charging per character for a
/// year; measuring it is what found that (see <c>UndoBudgetProbeTests</c>).</para>
/// <para>Headless: <see cref="UndoManager"/> takes a document and a pointer and nothing else.</para>
/// </summary>
public class UndoBudgetTests
{
    private static Paragraph P(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        return p;
    }

    private static FlowDocument Doc(params string[] paragraphs)
    {
        var d = new FlowDocument();
        foreach (var t in paragraphs) d.Blocks.Add(P(t));
        return d;
    }

    private static TextPointer Caret(FlowDocument d) => new((Paragraph)d.Blocks[0], 0);

    // ---- what the estimate is made of ---------------------------------------------------------

    // The half that a character-counting estimate gets wrong, and the reason this file exists.
    [Fact]
    public void TheEstimateIgnoresTextLength()
    {
        Assert.Equal(UndoManager.EstimateBytes(Doc("x")),
                     UndoManager.EstimateBytes(Doc(new string('x', 100_000))));
    }

    [Fact]
    public void TheEstimateGrowsWithElements()
    {
        int one = UndoManager.EstimateBytes(Doc("a"));
        int ten = UndoManager.EstimateBytes(Doc("a", "b", "c", "d", "e", "f", "g", "h", "i", "j"));

        Assert.True(ten > one * 5, $"ten paragraphs should cost about ten times one, got {one} -> {ten}");
    }

    // Content inside an INLINE table is deep-cloned with the snapshot, so it has to reach the estimate.
    // A flat placeholder for the table makes a document whose content lives in one look tiny — the exact
    // case the budget exists for.
    [Fact]
    public void TheEstimateCountsInlineTableContent()
    {
        static FlowDocument WithCellParagraphs(int count)
        {
            var doc = new FlowDocument();
            var host = new Paragraph();
            var it = new InlineTable { Table = new TableBlock(1, 1) };
            var cell = it.Table.Cells[0][0];
            cell.Blocks.Clear();
            for (int i = 0; i < count; i++) cell.Blocks.Add(P("x"));
            host.Inlines.Add(it);
            doc.Blocks.Add(host);
            return doc;
        }

        Assert.True(UndoManager.EstimateBytes(WithCellParagraphs(50))
                  > UndoManager.EstimateBytes(WithCellParagraphs(1)) * 5,
            "inline-table cell content was not counted");
    }

    // ---- what the bounds do -------------------------------------------------------------------

    [Fact]
    public void TheHistoryKeepsAtMostFiftySteps()
    {
        var doc = Doc("x");
        var undo = new UndoManager();
        for (int i = 0; i < 60; i++) undo.PushState(doc, Caret(doc));

        int depth = 0;
        while (undo.Undo(doc, Caret(doc)) != null) depth++;

        Assert.Equal(50, depth);
    }

    [Fact]
    public void ASmallDocumentKeepsItsFullHistory()
    {
        var doc = Doc("small");
        var undo = new UndoManager();
        for (int i = 0; i < 10; i++) undo.PushState(doc, Caret(doc));

        int depth = 0;
        while (undo.Undo(doc, Caret(doc)) != null) depth++;

        Assert.Equal(10, depth);
    }

    // The budget trims, and the floor keeps undo useful under it: a large document that trimmed to one
    // step would leave the user with an undo key that undoes almost nothing.
    //
    // ⚠ SIX pushes, not four, and the number is load-bearing. Trim returns early while the stack is at
    // or below the floor, so a four-push run lands on three steps whether or not the budget loop honours
    // the floor at all — that version of this test passes with the floor deleted. Six pushes make the
    // loop the only thing that can keep the third step (without the floor it trims to two).
    [Fact]
    public void TheBudgetTrimsTheHistoryButNeverBelowThreeSteps()
    {
        var doc = Doc("a", "b", "c", "d", "e");
        int perSnapshot = UndoManager.EstimateBytes(doc);

        // Room for two snapshots, not three.
        var undo = new UndoManager(maxBytes: perSnapshot * 2 + 1);
        for (int i = 0; i < 6; i++) undo.PushState(doc, Caret(doc));

        int depth = 0;
        while (undo.Undo(doc, Caret(doc)) != null) depth++;

        Assert.Equal(3, depth);
    }

    // Undo pushes the current document onto the redo stack, so that stack needs the same bound — an
    // editor that is undone through fifty steps would otherwise rebuild the whole history on the way
    // back.
    [Fact]
    public void TheRedoStackIsBoundedToo()
    {
        var doc = Doc("a", "b", "c", "d", "e");
        int perSnapshot = UndoManager.EstimateBytes(doc);
        var undo = new UndoManager(maxBytes: perSnapshot * 2 + 1);

        for (int i = 0; i < 8; i++) undo.PushState(doc, Caret(doc));
        int undone = 0;
        while (undo.Undo(doc, Caret(doc)) != null) undone++;

        int redone = 0;
        while (undo.Redo(doc, Caret(doc)) != null) redone++;

        Assert.Equal(3, undone);          // the undo stack was trimmed to the floor
        Assert.Equal(3, redone);          // and so was the redo stack it filled
    }
}
