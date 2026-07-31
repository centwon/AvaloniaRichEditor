using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Undo/redo caret restoration at depth. UndoManager identifies the caret by its index in document
// paragraph order; that walk stopped at each cell's FIRST paragraph, so a caret in a cell's 2nd+
// paragraph (P3), in a nested table (P4-2b) or in an inline table (milestone B) was never numbered
// and undo dropped the caret at the start of the document instead of where the edit happened.
public class UndoCaretRestoreTests
{
    // Populates the layout caches the way a real frame does (no top-level Window).
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string text)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    // The caret paragraph is internal state; the public surface reports position, not identity.
    private static Paragraph? CaretPara(RichEditor ed)
        => ((TextPointer)typeof(RichEditor)
            .GetField("_caretPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ed)!).Paragraph;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        var t = typeof(RichEditor);
        foreach (var name in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            t.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
             .SetValue(ed, new TextPointer(p, off));
    }

    private static Paragraph NewPara(string text)
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = text });
        return p;
    }

    // Undo swaps in a cloned document, so the restored caret paragraph is a different instance than
    // the one edited. Identify it structurally: its position in document paragraph order.
    private static int ParaIndexOf(RichEditor ed, Paragraph? target)
    {
        if (target == null) return -1;
        var all = (System.Collections.IEnumerable)typeof(RichEditor)
            .GetMethod("GetAllParagraphsInOrder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, null)!;
        int i = 0;
        foreach (Paragraph p in all) { if (ReferenceEquals(p, target)) return i; i++; }
        return -1;
    }

    // Types into `caret`, undoes, and returns (expected index, restored index) in paragraph order.
    private static (int expected, int actual) TypeThenUndo(RichEditor ed, Paragraph caret)
    {
        Realize(ed);
        PlaceCaret(ed, caret, 0);
        int expected = ParaIndexOf(ed, caret);
        Type(ed, "X");                     // OnTextInput pushes the typing checkpoint
        Realize(ed);
        ed.Undo();
        return (expected, ParaIndexOf(ed, CaretPara(ed)));
    }

    [AvaloniaFact]
    public void Undo_RestoresCaret_InACellsSecondParagraph()
    {
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(NewPara("first"));
        var second = NewPara("second");
        cell.Blocks.Add(second);

        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };

        var (expected, actual) = TypeThenUndo(ed, second);
        Assert.True(expected >= 0, "the second cell paragraph must be in document order");
        Assert.Equal(expected, actual);
    }

    [AvaloniaFact]
    public void Undo_RestoresCaret_InsideANestedTable()
    {
        var outer = new TableBlock(1, 1);
        var inner = new TableBlock(1, 1);
        var innerPara = inner.Cells[0][0].Para;
        ((Run)innerPara.Inlines[0]).Text = "nested";
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(inner);

        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc };

        var (expected, actual) = TypeThenUndo(ed, innerPara);
        Assert.True(expected >= 0, "a nested-table cell paragraph must be in document order");
        Assert.Equal(expected, actual);
    }

    [AvaloniaFact]
    public void Undo_RestoresCaret_InsideAnInlineTable()
    {
        var host = NewPara("host");
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        var cellPara = it.Table.Cells[0][0].Para;
        ((Run)cellPara.Inlines[0]).Text = "inline";
        host.Inlines.Add(it);

        var doc = new FlowDocument();
        doc.Blocks.Add(host);
        var ed = new RichEditor { Document = doc };

        var (expected, actual) = TypeThenUndo(ed, cellPara);
        Assert.True(expected >= 0, "an inline-table cell paragraph must be in document order");
        Assert.Equal(expected, actual);
    }

    // The two walks are inverses, so a caret deep in the tree must survive undo AND the following redo.
    [AvaloniaFact]
    public void Redo_RestoresCaret_InACellsSecondParagraph()
    {
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(NewPara("first"));
        var second = NewPara("second");
        cell.Blocks.Add(second);

        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        PlaceCaret(ed, second, 0);
        int expected = ParaIndexOf(ed, second);

        Type(ed, "X");
        Realize(ed);
        ed.Undo();
        Realize(ed);
        ed.Redo();

        Assert.Equal(expected, ParaIndexOf(ed, CaretPara(ed)));
    }

    // The plain top-level case must keep working (the walk's numbering changed shape).
    [AvaloniaFact]
    public void Undo_StillRestoresCaret_InATopLevelParagraph()
    {
        var first = NewPara("one");
        var target = NewPara("two");
        var doc = TestHelpers.Doc(first, target);
        var ed = new RichEditor { Document = doc };

        var (expected, actual) = TypeThenUndo(ed, target);
        Assert.Equal(expected, actual);
    }
}
