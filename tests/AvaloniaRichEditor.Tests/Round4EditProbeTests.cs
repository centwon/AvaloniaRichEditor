using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Editing invariants exercised on a document that mixes milestone A (blocks in cells, nested
// tables) with milestone B (inline tables) — the combination the single-axis suites don't build.
public class Round4EditProbeTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string s)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s });

    // "head" / inline-table cell "i0" / "tail", plus a block table whose cell holds a nested table.
    private static (RichEditor ed, Paragraph host, Paragraph inlineCellPara, Paragraph nestedCellPara) Mixed()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();

        var it = new InlineTable { Table = new TableBlock(1, 1) };
        var inlineCellPara = it.Table.Cells[0][0].Para;
        inlineCellPara.Inlines.Add(new Run { Text = "i0" });
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "head" });
        host.Inlines.Add(it);
        host.Inlines.Add(new Run { Text = "tail" });
        doc.Blocks.Add(host);

        var outer = new TableBlock(1, 1);
        var nested = new TableBlock(1, 1);
        var nestedCellPara = nested.Cells[0][0].Para;
        nestedCellPara.Inlines.Add(new Run { Text = "n0" });
        outer.Cells[0][0].Blocks.Add(nested);
        doc.Blocks.Add(outer);

        ed.Document = doc;
        ed.MarkSaved();
        return (ed, host, inlineCellPara, nestedCellPara);
    }

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        var f = typeof(RichEditor);
        const System.Reflection.BindingFlags NP =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        foreach (var name in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            f.GetField(name, NP)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static Paragraph InlineCellParaOf(RichEditor ed)
        => ed.Document!.Blocks.OfType<Paragraph>()
             .SelectMany(p => p.Inlines.OfType<InlineTable>())
             .Single().Table.Cells[0][0].Para;

    // Undo identifies the caret by its index in recursive paragraph order. An edit made in an
    // inline table's cell must come back with the caret in that same cell, not at the document top.
    [AvaloniaFact]
    public void Undo_AfterEditingAnInlineTableCell_RestoresTheCaretThere()
    {
        var (ed, _, cellPara, _) = Mixed();
        PlaceCaret(ed, cellPara, 2);
        Type(ed, "X");
        Assert.Equal("i0X", cellPara.Text());

        ed.Undo();

        // Undo swaps in a cloned document, so compare by position, not by reference.
        Assert.Equal("i0", InlineCellParaOf(ed).Text());
        var caret = (TextPointer)typeof(RichEditor)
            .GetField("_caretPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ed)!;
        Assert.Same(InlineCellParaOf(ed), caret.Paragraph);
    }

    // Same for a paragraph inside a nested block table.
    [AvaloniaFact]
    public void Undo_AfterEditingANestedTableCell_RestoresTheCaretThere()
    {
        var (ed, _, _, nestedPara) = Mixed();
        PlaceCaret(ed, nestedPara, 2);
        Type(ed, "Y");

        ed.Undo();

        var caret = (TextPointer)typeof(RichEditor)
            .GetField("_caretPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ed)!;
        var outer = ed.Document!.Blocks.OfType<TableBlock>().Single();
        var nested = outer.Cells[0][0].Blocks.OfType<TableBlock>().Single();
        Assert.Same(nested.Cells[0][0].Para, caret.Paragraph);
    }

    // GetPlainText backs the accessibility peer, so text at any depth must appear in it.
    [AvaloniaFact]
    public void GetPlainText_ReachesInlineAndNestedTableText()
    {
        var (ed, _, _, _) = Mixed();
        string text = ed.GetPlainText();
        Assert.Contains("head", text);
        Assert.Contains("i0", text);
        Assert.Contains("tail", text);
        Assert.Contains("n0", text);
    }

    // Loading a document clears the dirty flag; a single edit at depth must set it, and MarkSaved
    // must clear it again. (A resize-handle click regression made this misreport before.)
    [AvaloniaFact]
    public void IsModified_TracksAnEditInsideAnInlineTableCell()
    {
        var (ed, _, cellPara, _) = Mixed();
        Assert.False(ed.IsModified);

        PlaceCaret(ed, cellPara, 2);
        Type(ed, "Z");
        Assert.True(ed.IsModified);

        ed.MarkSaved();
        Assert.False(ed.IsModified);
    }

    // Ctrl+A outside a table selects the whole document; deleting it must leave a valid document
    // (core rule #5: a paragraph at the start and end) rather than an empty block list.
    [AvaloniaFact]
    public void SelectAllThenDelete_LeavesAValidDocument()
    {
        var (ed, host, _, _) = Mixed();
        PlaceCaret(ed, host, 0);
        Press(ed, Key.A, KeyModifiers.Control);
        Press(ed, Key.Delete);

        Assert.NotEmpty(ed.Document!.Blocks);
        Assert.IsType<Paragraph>(ed.Document.Blocks[0]);
        Assert.IsType<Paragraph>(ed.Document.Blocks[^1]);
    }

    // Find must reach text at any depth and select it in the owning paragraph.
    [AvaloniaFact]
    public void FindNext_ReachesTextInsideAnInlineTableCell()
    {
        var (ed, host, cellPara, _) = Mixed();
        PlaceCaret(ed, host, 0);

        Assert.True(ed.FindNext("i0", matchCase: true));

        var sel = (TextPointer)typeof(RichEditor)
            .GetField("_selectionStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ed)!;
        Assert.Same(cellPara, sel.Paragraph);
    }

    // ReplaceAll must reach every depth and terminate.
    [AvaloniaFact]
    public void ReplaceAll_ReachesEveryDepth()
    {
        var (ed, _, cellPara, nestedPara) = Mixed();
        Assert.Equal(2, ed.ReplaceAll("0", "9", matchCase: true));
        Assert.Equal("i9", cellPara.Text());
        Assert.Equal("n9", nestedPara.Text());
    }
}
