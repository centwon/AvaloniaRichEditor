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

// The cell block is the operated-on selection, not just painted chrome. It used to feed only the
// renderer and the context menu while every edit/format command walked the linear text run between the
// drag's two endpoints — which both misses the part of the first/last cell outside the drag offsets and
// sweeps in cells that lie OUTSIDE the painted rectangle.
public class CellBlockSelectionTests
{
    private static void Realize(RichEditor ed, double width = 800)
    {
        ed.Measure(new Size(width, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, width, ed.DesiredSize.Height));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)width, (int)System.Math.Max(1, ed.DesiredSize.Height)));
        rtb.Render(ed);
    }

    private static void SetField(RichEditor ed, string name, object? v)
        => typeof(RichEditor).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(ed, v);

    // Reproduces what a drag across cells leaves behind: cell-selection mode on `tb`, with the two
    // endpoints at partial offsets inside the corner cells (exactly the case that used to leak).
    private static void DragAcrossCells(RichEditor ed, TableBlock tb,
        Paragraph from, int fromOff, Paragraph to, int toOff)
    {
        SetField(ed, "_cellSelMode", true);
        SetField(ed, "_cellSelTable", tb);
        SetField(ed, "_selectionStart", new TextPointer(from, fromOff));
        SetField(ed, "_selectionEnd", new TextPointer(to, toOff));
        SetField(ed, "_caretPosition", new TextPointer(to, toOff));
    }

    private static string CellText(TableCell c)
        => string.Concat(c.Blocks.OfType<Paragraph>().Select(p => p.Text()));

    // A grid whose every cell carries its own "r,c" text.
    private static (RichEditor ed, TableBlock tb) Grid(int rows, int cols)
    {
        var tb = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"{r}{c}";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        return (ed, tb);
    }

    // ---- Delete clears whole cells and keeps the grid (semantic A) ----------

    [AvaloniaFact]
    public void Delete_OnACellBlock_ClearsWholeCells_NotJustFromTheDragOffset()
    {
        var (ed, tb) = Grid(1, 2);
        // Drag started mid-text in the first cell and ended mid-text in the second.
        DragAcrossCells(ed, tb, tb.Cells[0][0].Para, 1, tb.Cells[0][1].Para, 1);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        Assert.Equal("", CellText(tb.Cells[0][0]));
        Assert.Equal("", CellText(tb.Cells[0][1]));
    }

    [AvaloniaFact]
    public void Delete_OnACellBlock_LeavesTheGridStanding()
    {
        var (ed, tb) = Grid(2, 2);
        DragAcrossCells(ed, tb, tb.Cells[0][0].Para, 0, tb.Cells[1][1].Para, 2);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        Assert.Single(ed.Document!.Blocks.OfType<TableBlock>());
        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
    }

    // ---- formatting covers whole cells, and only the rectangle --------------

    [AvaloniaFact]
    public void CharacterFormat_OnACellBlock_CoversTheWholeCell()
    {
        var (ed, tb) = Grid(1, 2);
        DragAcrossCells(ed, tb, tb.Cells[0][0].Para, 1, tb.Cells[0][1].Para, 1);

        ed.ToggleBold();

        // Every run of both cells is bold — not just from offset 1 of the first to offset 1 of the last.
        foreach (var cell in new[] { tb.Cells[0][0], tb.Cells[0][1] })
            foreach (var p in cell.Blocks.OfType<Paragraph>())
                Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
    }

    // A vertical block in a 3-column table: document order between the two corners runs through the
    // cells to the right, which are NOT in the painted rectangle and must stay untouched.
    [AvaloniaFact]
    public void ParagraphFormat_OnAVerticalCellBlock_SkipsCellsOutsideTheRectangle()
    {
        var (ed, tb) = Grid(2, 3);
        DragAcrossCells(ed, tb, tb.Cells[0][0].Para, 0, tb.Cells[1][0].Para, 2);

        ed.SetTextAlignment(TextAlignment.Center);

        Assert.Equal(TextAlignment.Center, tb.Cells[0][0].Para.TextAlignment);
        Assert.Equal(TextAlignment.Center, tb.Cells[1][0].Para.TextAlignment);
        // Column 1 and 2 lie outside the rectangle.
        Assert.Equal(TextAlignment.Left, tb.Cells[0][1].Para.TextAlignment);
        Assert.Equal(TextAlignment.Left, tb.Cells[0][2].Para.TextAlignment);
        Assert.Equal(TextAlignment.Left, tb.Cells[1][1].Para.TextAlignment);
    }

    [AvaloniaFact]
    public void CharacterFormat_OnAVerticalCellBlock_SkipsCellsOutsideTheRectangle()
    {
        var (ed, tb) = Grid(2, 3);
        DragAcrossCells(ed, tb, tb.Cells[0][0].Para, 0, tb.Cells[1][0].Para, 2);

        ed.ToggleBold();

        Assert.All(tb.Cells[0][0].Para.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
        Assert.All(tb.Cells[0][1].Para.Inlines.OfType<Run>(), r => Assert.NotEqual(FontWeight.Bold, r.FontWeight));
        Assert.All(tb.Cells[0][2].Para.Inlines.OfType<Run>(), r => Assert.NotEqual(FontWeight.Bold, r.FontWeight));
    }

    // A multi-paragraph cell must be covered in full, not just its first paragraph.
    [AvaloniaFact]
    public void CellBlock_CoversEveryParagraphOfAMultiParagraphCell()
    {
        var tb = new TableBlock(1, 2);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        var p1 = new Paragraph(); p1.Inlines.Add(new Run { Text = "one" });
        var p2 = new Paragraph(); p2.Inlines.Add(new Run { Text = "two" });
        cell.Blocks.Add(p1); cell.Blocks.Add(p2);
        ((Run)tb.Cells[0][1].Para.Inlines[0]).Text = "right";
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);

        DragAcrossCells(ed, tb, p1, 0, tb.Cells[0][1].Para, 2);
        ed.SetTextAlignment(TextAlignment.Right);

        Assert.Equal(TextAlignment.Right, p1.TextAlignment);
        Assert.Equal(TextAlignment.Right, p2.TextAlignment);
    }

    // ---- one cell as a block ------------------------------------------------

    // The "Select Cell" entry point (context menu, or a click while in cell mode).
    private static void SelectCellAsBlock(RichEditor ed, TableBlock tb, TableCell cell)
        => typeof(RichEditor).GetMethod("SelectCellAsBlock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object[] { tb, cell });

    // Selecting a single cell is a real one-cell block: the commands treat it as a unit.
    // SelectedCellRange used to return null for a single cell outright, so nothing could act on one.
    [AvaloniaFact]
    public void ASingleSelectedCell_IsAnOperableBlock()
    {
        var (ed, tb) = Grid(1, 2);
        var cell = tb.Cells[0][0];
        SelectCellAsBlock(ed, tb, cell);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        Assert.Equal("", CellText(cell));
        Assert.Equal("01", CellText(tb.Cells[0][1])); // the neighbour untouched
    }

    [AvaloniaFact]
    public void ASingleSelectedCell_FormatsAsAUnit()
    {
        var (ed, tb) = Grid(1, 2);
        SelectCellAsBlock(ed, tb, tb.Cells[0][0]);

        ed.ToggleBold();

        Assert.All(tb.Cells[0][0].Para.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
        Assert.All(tb.Cells[0][1].Para.Inlines.OfType<Run>(), r => Assert.NotEqual(FontWeight.Bold, r.FontWeight));
    }

    // Moving the caret ends the block, so the painted fill and the operated-on range never diverge.
    [AvaloniaFact]
    public void AnArrowKey_EndsTheCellBlock()
    {
        var (ed, tb) = Grid(1, 2);
        SelectCellAsBlock(ed, tb, tb.Cells[0][0]);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        // Back to ordinary text editing: one character, not the whole cell.
        Assert.NotEqual("", CellText(tb.Cells[0][0]));
    }

    // Guard: a plain text selection INSIDE one cell (not in cell mode) must stay a text edit.
    [AvaloniaFact]
    public void ATextSelectionInsideOneCell_IsNotTreatedAsACellBlock()
    {
        var (ed, tb) = Grid(1, 2);
        var cell = tb.Cells[0][0];
        ((Run)cell.Para.Inlines[0]).Text = "abcd";
        SetField(ed, "_selectionStart", new TextPointer(cell.Para, 1));
        SetField(ed, "_selectionEnd", new TextPointer(cell.Para, 3));
        SetField(ed, "_caretPosition", new TextPointer(cell.Para, 3));

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        Assert.Equal("ad", CellText(cell)); // only the selected characters went
    }
}
