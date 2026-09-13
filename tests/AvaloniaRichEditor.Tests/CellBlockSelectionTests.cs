using System.Collections.Generic;
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

    // Reproduces what a drag across cells leaves behind: the two endpoints at partial offsets inside the
    // corner cells of `tb` (exactly the case that used to leak). That alone is the cell block.
    private static void DragAcrossCells(RichEditor ed, TableBlock tb,
        Paragraph from, int fromOff, Paragraph to, int toOff)
    {
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

    // A block of SOME cells: the two left columns of a 2×3 grid. It was the whole 2×2 grid, which since
    // 2026-09-13 is a table held whole — Delete removes that (user decision; ViewerTableCopyTests).
    [AvaloniaFact]
    public void Delete_OnACellBlock_LeavesTheGridStanding()
    {
        var (ed, tb) = Grid(2, 3);
        DragAcrossCells(ed, tb, tb.Cells[0][0].Para, 0, tb.Cells[1][1].Para, 2);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

        Assert.Single(ed.Document!.Blocks.OfType<TableBlock>());
        Assert.Equal(2, tb.Rows);
        Assert.Equal(3, tb.Columns);
        Assert.Equal("", CellText(tb.Cells[1][1]));   // inside the block: emptied
        Assert.Equal("02", CellText(tb.Cells[0][2])); // outside it: untouched
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
            .Invoke(ed, new object[] { tb, cell, false });

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

    // ---- unified cell block (with the WinUI port, 2026-09-13) — through the real key handler ----------

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Caret(RichEditor ed, Paragraph p, int offset)
    {
        SetField(ed, "_caretPosition", new TextPointer(p, offset));
        SetField(ed, "_selectionStart", new TextPointer(p, offset));
        SetField(ed, "_selectionEnd", new TextPointer(p, offset));
    }

    // The block the renderer fills (SelectedCellRange), asserted together with the one the commands act on
    // (SelectedCellsBlock): the two used to disagree.
    private static void AssertBlock(RichEditor ed, TableBlock tb, (int r0, int c0, int r1, int c1)? expected)
    {
        var painted = ((int, int, int, int)?)typeof(RichEditor)
            .GetMethod("SelectedCellRange", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object[] { tb });
        Assert.Equal(expected, painted);
        var operated = typeof(RichEditor)
            .GetMethod("SelectedCellsBlock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, null);
        Assert.Equal(expected != null, operated != null);
    }

    // The measured defect: Shift+arrow across a cell boundary turned the cell-selection mode OFF while the
    // renderer still filled both cells, so Delete removed characters under a painted block ("a | 1b | a2b").
    // The endpoints now make the block for the renderer and the commands alike, and Delete clears both cells.
    [AvaloniaFact]
    public void ShiftArrowAcrossACellBoundary_MakesABlock_ThatDeleteClearsWhole()
    {
        var (ed, tb) = Grid(1, 3);
        Caret(ed, tb.Cells[0][0].Para, 1);

        Press(ed, Key.Right, KeyModifiers.Shift); // to the cell's end
        Press(ed, Key.Right, KeyModifiers.Shift); // into the next cell
        AssertBlock(ed, tb, (0, 0, 0, 1));

        Press(ed, Key.Delete);
        Assert.Equal(new[] { "", "", "02" }, new[] { CellText(tb.Cells[0][0]), CellText(tb.Cells[0][1]), CellText(tb.Cells[0][2]) });
    }

    [AvaloniaFact]
    public void F5_SelectsTheCaretsCell_AsAOneCellBlock()
    {
        var (ed, tb) = Grid(2, 2);
        Caret(ed, tb.Cells[1][0].Para, 1);

        Press(ed, Key.F5);
        AssertBlock(ed, tb, (1, 0, 1, 0));

        Press(ed, Key.Delete);
        Assert.Equal("", CellText(tb.Cells[1][0]));
        Assert.Equal(new[] { "00", "01", "11" }, new[] { CellText(tb.Cells[0][0]), CellText(tb.Cells[0][1]), CellText(tb.Cells[1][1]) });
    }

    // HWP: the anchor corner stays and the active corner steps a cell; back on the anchor it is one cell again,
    // and at the table's edge nothing moves.
    [AvaloniaFact]
    public void ShiftArrows_GrowAndShrinkTheBlock_ByWholeCells()
    {
        var (ed, tb) = Grid(3, 3);
        Caret(ed, tb.Cells[1][1].Para, 1);
        Press(ed, Key.F5);

        Press(ed, Key.Right, KeyModifiers.Shift);
        AssertBlock(ed, tb, (1, 1, 1, 2));
        Press(ed, Key.Down, KeyModifiers.Shift);
        AssertBlock(ed, tb, (1, 1, 2, 2));
        Press(ed, Key.Left, KeyModifiers.Shift);
        Press(ed, Key.Left, KeyModifiers.Shift);
        AssertBlock(ed, tb, (1, 0, 2, 1));
        Press(ed, Key.Up, KeyModifiers.Shift);
        AssertBlock(ed, tb, (1, 0, 1, 1));
        Press(ed, Key.Right, KeyModifiers.Shift);
        AssertBlock(ed, tb, (1, 1, 1, 1)); // back on the anchor: a one-cell block

        Caret(ed, tb.Cells[1][2].Para, 1);
        Press(ed, Key.F5);
        Press(ed, Key.Right, KeyModifiers.Shift);
        AssertBlock(ed, tb, (1, 2, 1, 2)); // the table's edge
    }

    // Any caret move ends a one-cell block — there is no mode to reset — and the SAME range selected again later
    // (the staged Ctrl+A's cell stage) is a text selection: the marker cannot come back to life.
    [AvaloniaFact]
    public void ACaretMove_EndsAOneCellBlock_AndTheSameRangeLater_IsText()
    {
        var (ed, tb) = Grid(2, 2);
        Caret(ed, tb.Cells[0][0].Para, 1);
        Press(ed, Key.F5);
        Press(ed, Key.Right);
        AssertBlock(ed, tb, null);

        Caret(ed, tb.Cells[0][0].Para, 1);
        Press(ed, Key.A, KeyModifiers.Control); // stage 1: the cell's content — the very range F5 selected
        AssertBlock(ed, tb, null);
    }

    // A merged cell is one cell: F5 takes its whole span, and Shift+arrow steps past the span, not into it.
    [AvaloniaFact]
    public void AMergedCell_IsOneCell_ForF5_AndForShiftArrow()
    {
        var tb = new TableBlock(2, 3);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"{r}{c}";
        tb.MergeCells(0, 0, 0, 1);
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        Realize(ed);
        Caret(ed, tb.Cells[0][0].Blocks.OfType<Paragraph>().First(), 0);

        Press(ed, Key.F5);
        AssertBlock(ed, tb, (0, 0, 0, 1));
        Press(ed, Key.Right, KeyModifiers.Shift);
        AssertBlock(ed, tb, (0, 0, 0, 2));
    }

    private static List<Block>? CopyNow(RichEditor ed)
    {
        var slot = typeof(RichEditor).GetField("_internalClipboardBlocks",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        slot.SetValue(null, null); // static: a previous test's copy must not pass for this one
        typeof(RichEditor).GetMethod("CopySelectionToClipboard",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(ed, null);
        return (List<Block>?)slot.GetValue(null);
    }

    // Copy takes a cell block as the rectangle it paints — a one-cell block as a 1×1 table, which pastes back as
    // a cell. It came out as the cell's text (live check, 2026-09-14).
    [AvaloniaFact]
    public void Copy_OfAOneCellBlock_IsAOneByOneTable()
    {
        var (ed, tb) = Grid(2, 2);
        Caret(ed, tb.Cells[0][1].Para, 1);
        Press(ed, Key.F5);

        var copied = Assert.IsType<TableBlock>(Assert.Single(CopyNow(ed)!));
        Assert.Equal((1, 1), (copied.Rows, copied.Columns));
        Assert.Equal("01", CellText(copied.Cells[0][0]));
    }

    // …and a block of several cells as that rectangle — it copied the WHOLE table around it.
    [AvaloniaFact]
    public void Copy_OfACellBlock_IsTheRectangle_NotTheWholeTable()
    {
        var (ed, tb) = Grid(3, 3);
        DragAcrossCells(ed, tb, tb.Cells[0][1].Para, 1, tb.Cells[1][2].Para, 1);

        var copied = Assert.IsType<TableBlock>(Assert.Single(CopyNow(ed)!));
        Assert.Equal((2, 2), (copied.Rows, copied.Columns));
        Assert.Equal(new[] { "01", "02", "11", "12" },
            new[] { CellText(copied.Cells[0][0]), CellText(copied.Cells[0][1]), CellText(copied.Cells[1][0]), CellText(copied.Cells[1][1]) });
    }

    // An EMPTY cell's one-cell block is a zero-length range — it must still count as selected and copy as a 1×1
    // table. It copied nothing ("is anything selected" compared the two ends).
    [AvaloniaFact]
    public void Copy_OfAnEmptyOneCellBlock_IsAOneByOneTable()
    {
        var (ed, tb) = Grid(2, 2);
        ((Run)tb.Cells[0][0].Para.Inlines[0]).Text = "";
        Caret(ed, tb.Cells[0][0].Para, 0);
        Press(ed, Key.F5);

        var copied = Assert.IsType<TableBlock>(Assert.Single(CopyNow(ed)!));
        Assert.Equal((1, 1), (copied.Rows, copied.Columns));
    }

    // A viewer can select a cell too — Ctrl+C then copies it as a 1×1 table.
    [AvaloniaFact]
    public void F5_WorksInAViewer()
    {
        var (ed, tb) = Grid(2, 2);
        ed.IsReadOnly = true;
        Caret(ed, tb.Cells[0][1].Para, 1);

        Press(ed, Key.F5);
        AssertBlock(ed, tb, (0, 1, 0, 1));
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
