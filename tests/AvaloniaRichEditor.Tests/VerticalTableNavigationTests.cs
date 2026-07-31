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

// HWP-style ↑/↓ around a table: the arrow steps INTO the nearest row and, from the far row, out to the
// neighbouring paragraph. It used to park on the block caret beside the table, and a second press then
// skipped the whole table — its cells were unreachable by vertical navigation.
public class VerticalTableNavigationTests
{
    private static void Render(RichEditor ed, double width = 400)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static T? Field<T>(RichEditor ed, string n) where T : class
        => typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed) as T;

    private static Paragraph? Caret(RichEditor ed)
        => Field<TextPointer>(ed, "_caretPosition")?.Paragraph;

    private static Block? CaretBlock(RichEditor ed) => Field<Block>(ed, "_caretBlock");

    // Press a key and re-render so the caret geometry (_lastCaretPoint) tracks the new position — the
    // vertical steps are geometric, so every press needs a frame behind it, as it has in real use.
    private static void Press(RichEditor ed, Key key)
    {
        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        Render(ed);
    }

    private static void PlaceCaret(RichEditor ed, Paragraph p, int off)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, off));
    }

    // "above" paragraph, a 2x2 table, "below" paragraph.
    private static (RichEditor ed, Paragraph above, TableBlock tb, Paragraph below) Sandwich()
    {
        var above = new Paragraph();
        above.Inlines.Add(new Run { Text = "above" });
        var tb = new TableBlock(2, 2);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"r{r}c{c}";
        var below = new Paragraph();
        below.Inlines.Add(new Run { Text = "below" });

        var doc = new FlowDocument();
        doc.Blocks.Add(above);
        doc.Blocks.Add(tb);
        doc.Blocks.Add(below);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        return (ed, above, tb, below);
    }

    private static int RowOf(RichEditor ed, TableBlock tb)
    {
        var p = Caret(ed);
        for (int r = 0; r < tb.Rows; r++)
            for (int c = 0; c < tb.Columns; c++)
                if (ReferenceEquals(tb.Cells[r][c].Para, p)) return r;
        return -1;
    }

    [AvaloniaFact]
    public void DownFromAboveTheTable_EntersTheFirstRow()
    {
        var (ed, above, tb, _) = Sandwich();
        PlaceCaret(ed, above, 0);
        Render(ed);

        Press(ed, Key.Down);

        Assert.Null(CaretBlock(ed)); // not parked on the block caret
        Assert.Equal(0, RowOf(ed, tb));
    }

    [AvaloniaFact]
    public void DownThroughTheTable_WalksTheRowsThenLeaves()
    {
        var (ed, above, tb, below) = Sandwich();
        PlaceCaret(ed, above, 0);
        Render(ed);

        Press(ed, Key.Down);
        Assert.Equal(0, RowOf(ed, tb));

        Press(ed, Key.Down);
        Assert.Equal(1, RowOf(ed, tb));

        Press(ed, Key.Down);
        Assert.Same(below, Caret(ed)); // out to the paragraph after the table, not onto a block caret
        Assert.Null(CaretBlock(ed));
    }

    [AvaloniaFact]
    public void UpFromBelowTheTable_EntersTheLastRow()
    {
        var (ed, _, tb, below) = Sandwich();
        PlaceCaret(ed, below, 0);
        Render(ed);

        Press(ed, Key.Up);

        Assert.Null(CaretBlock(ed));
        Assert.Equal(1, RowOf(ed, tb));
    }

    [AvaloniaFact]
    public void UpThroughTheTable_WalksTheRowsThenLeaves()
    {
        var (ed, above, tb, below) = Sandwich();
        PlaceCaret(ed, below, 0);
        Render(ed);

        Press(ed, Key.Up);
        Assert.Equal(1, RowOf(ed, tb));

        Press(ed, Key.Up);
        Assert.Equal(0, RowOf(ed, tb));

        Press(ed, Key.Up);
        Assert.Same(above, Caret(ed));
        Assert.Null(CaretBlock(ed));
    }

    // Down then Up must return where it started — the two directions have to be symmetric.
    [AvaloniaFact]
    public void DownIntoTheTableThenUp_ReturnsAbove()
    {
        var (ed, above, tb, _) = Sandwich();
        PlaceCaret(ed, above, 0);
        Render(ed);

        Press(ed, Key.Down);
        Assert.Equal(0, RowOf(ed, tb));
        Press(ed, Key.Up);

        Assert.Same(above, Caret(ed));
    }

    // An image has no text to enter, so vertical navigation still parks on its block caret — that is
    // what makes Space/Backspace on a bare image reachable from the keyboard.
    [AvaloniaFact]
    public void DownOntoAnImage_StillUsesTheBlockCaret()
    {
        var above = new Paragraph();
        above.Inlines.Add(new Run { Text = "above" });
        var img = new ImageBlock { Width = 100, Height = 80 };
        var below = new Paragraph();
        below.Inlines.Add(new Run { Text = "below" });
        var doc = new FlowDocument();
        doc.Blocks.Add(above);
        doc.Blocks.Add(img);
        doc.Blocks.Add(below);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        Render(ed);
        PlaceCaret(ed, above, 0);
        Render(ed);

        Press(ed, Key.Down);

        Assert.Same(img, CaretBlock(ed));
    }

    // The block caret around a table stays reachable horizontally, which is where indent/delete live.
    [AvaloniaFact]
    public void RightAtTheEndOfTheParagraphAboveStillGivesTheTablesBlockCaret()
    {
        var (ed, above, tb, _) = Sandwich();
        PlaceCaret(ed, above, above.Text().Length);
        Render(ed);

        Press(ed, Key.Right);

        Assert.Same(tb, CaretBlock(ed));
    }
}
