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
    public void Up_FromFirstCell_EntersBeforeTableCaret()
    {
        var (ed, tb) = EditorWithTable();
        PlaceCaret(ed, tb.Cells[0][0], 0);

        Press(ed, Key.Up);

        Assert.Same(tb, CaretBlockField.GetValue(ed));
        Assert.False((bool)CaretBlockAfterField.GetValue(ed)!);
    }
}
