using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Block-caret navigation around tables (roadmap bug: ↓ from the last cell must land on the
// table's "after" caret). The block caret is private state, so these tests read it by reflection.
public class BlockCaretTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static readonly FieldInfo CaretBlockField =
        typeof(RichEditor).GetField("_caretBlock", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo CaretBlockAfterField =
        typeof(RichEditor).GetField("_caretBlockAfter", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo CaretPositionField =
        typeof(RichEditor).GetField("_caretPosition", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static (RichEditor ed, TableBlock tb) EditorWithTable()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>before</p><table><tr><td>x</td><td>y</td></tr><tr><td>z</td><td>w</td></tr></table><p>after</p>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        return (ed, tb);
    }

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        CaretPositionField.SetValue(ed, new TextPointer(p, offset));
        typeof(RichEditor).GetField("_selectionStart", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ed, new TextPointer(p, offset));
        typeof(RichEditor).GetField("_selectionEnd", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ed, new TextPointer(p, offset));
    }

    [AvaloniaFact]
    public void Down_FromLastCell_EntersAfterTableCaret()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[1][1], 1); // inside "w" (last row, last column)

        Press(ed, Key.Down);

        Assert.Same(tb, CaretBlockField.GetValue(ed));
        Assert.True((bool)CaretBlockAfterField.GetValue(ed)!);
    }

    [AvaloniaFact]
    public void Down_FromAfterTableCaret_MovesToNextParagraph()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[1][1], 1);

        Press(ed, Key.Down); // -> after-table block caret
        Press(ed, Key.Down); // -> paragraph after the table

        Assert.Null(CaretBlockField.GetValue(ed));
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Equal("after", caret.Paragraph!.Text());
        Assert.Equal(0, caret.Offset);
    }

    [AvaloniaFact]
    public void Down_FromMergedCellReachingLastRow_EntersAfterTableCaret()
    {
        var (ed, tb) = EditorWithTable();
        tb.MergeCells(0, 1, 1, 1); // vertical merge in the last column: anchor (0,1) spans both rows
        PlaceCaret(ed, tb.Cells[0][1], 0);

        Press(ed, Key.Down);

        Assert.Same(tb, CaretBlockField.GetValue(ed));
        Assert.True((bool)CaretBlockAfterField.GetValue(ed)!);
    }

    [AvaloniaFact]
    public void Right_FromLastCellEnd_EntersAfterTableCaret()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[1][1], 1); // end of "w" (last logical cell)

        Press(ed, Key.Right);

        Assert.Same(tb, CaretBlockField.GetValue(ed));
        Assert.True((bool)CaretBlockAfterField.GetValue(ed)!);

        Press(ed, Key.Right); // and out to the following paragraph
        Assert.Null(CaretBlockField.GetValue(ed));
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Equal("after", caret.Paragraph!.Text());
    }

    [AvaloniaFact]
    public void Left_FromFirstCellStart_EntersBeforeTableCaret()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[0][0], 0); // start of "x" (first logical cell)

        Press(ed, Key.Left);

        Assert.Same(tb, CaretBlockField.GetValue(ed));
        Assert.False((bool)CaretBlockAfterField.GetValue(ed)!);

        Press(ed, Key.Left); // and out to the preceding paragraph's end
        Assert.Null(CaretBlockField.GetValue(ed));
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Equal("before", caret.Paragraph!.Text());
    }

    [AvaloniaFact]
    public void Right_FromNonLastCellEnd_MovesToNextCell()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[0][0], 1); // end of "x" — not the last cell

        Press(ed, Key.Right);

        Assert.Null(CaretBlockField.GetValue(ed)); // no block caret: plain move into the next cell
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Same(tb.Cells[0][1], caret.Paragraph);
    }

    [AvaloniaFact]
    public void Down_FromBeforeTableCaret_SkipsCells_ToFollowingParagraph()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[0][0], 0);
        Press(ed, Key.Up);   // -> before-table caret

        Press(ed, Key.Down); // vertical: skip the table as one unit

        Assert.Null(CaretBlockField.GetValue(ed));
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Equal("after", caret.Paragraph!.Text());
    }

    [AvaloniaFact]
    public void Up_FromAfterTableCaret_SkipsCells_ToPrecedingParagraph()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[1][1], 1);
        Press(ed, Key.Down); // -> after-table caret

        Press(ed, Key.Up);   // vertical: skip the table as one unit

        Assert.Null(CaretBlockField.GetValue(ed));
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Equal("before", caret.Paragraph!.Text());
    }

    [AvaloniaFact]
    public void Right_FromBeforeTableCaret_EntersFirstCell()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[0][0], 0);
        Press(ed, Key.Up);    // -> before-table caret

        Press(ed, Key.Right); // horizontal: walk into the cells

        Assert.Null(CaretBlockField.GetValue(ed));
        var caret = (TextPointer)CaretPositionField.GetValue(ed)!;
        Assert.Same(tb.Cells[0][0], caret.Paragraph);
        Assert.Equal(0, caret.Offset);
    }

    [AvaloniaFact]
    public void Up_FromFirstCell_EntersBeforeTableCaret()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[0][0], 0);

        Press(ed, Key.Up);

        Assert.Same(tb, CaretBlockField.GetValue(ed));
        Assert.False((bool)CaretBlockAfterField.GetValue(ed)!);
    }
}
