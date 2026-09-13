using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A viewer (read-only editor) could not copy a table: right-clicking one offered Copy greyed out — it acts on
// the text selection, empty after a right-click — and nothing else took a table out. Now a right-click on a
// table selects it whole (the staged Ctrl+A's cell fill, so the viewer sees what Copy takes), the left/top
// border selects it on a click, and copying a whole-table selection takes THAT table (a nested one used to
// come out as the table around it, a one-cell one as bare text). Ported from the WinUI peer (PR #25 there).
public class ViewerTableCopyTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static T Field<T>(RichEditor ed, string name) => (T)typeof(RichEditor).GetField(name, NP)!.GetValue(ed)!;

    private static FieldInfo CopiedBlocksField
        => typeof(RichEditor).GetField("_internalClipboardBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<Block>? CopiedBlocks => (List<Block>?)CopiedBlocksField.GetValue(null);

    // The internal clipboard is STATIC — shared by every editor, so by every test. A copy test that does not
    // clear it first can pass on a table an earlier test left there, with its own copy doing nothing.
    private static void ClearClipboard() => CopiedBlocksField.SetValue(null, null);

    private static List<MenuItem> MenuItems(RichEditor ed)
    {
        var menu = (ContextMenu)typeof(RichEditor).GetField("_openContextMenu", NP)!.GetValue(ed)!;
        return (menu.ItemsSource ?? menu.Items)!.OfType<MenuItem>().ToList();
    }

    private static void Invoke(MenuItem mi) => mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static TableBlock Table(int rows, int cols)
    {
        var tb = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"c{r}{c}";
        return tb;
    }

    private static InteractionHost Host(TableBlock tb)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "above the table" } } });
        doc.Blocks.Add(tb);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        return host;
    }

    // Inside the table's first cell, from the geometry the renderer produced (never a hardcoded y — see
    // ContextMenuInteractionTests for why).
    private static Point InsideFirstCell(InteractionHost host, TableBlock tb)
    {
        var row0 = host.RowHandles.First(r => ReferenceEquals(r.tb, tb) && r.rowIndex == 0);
        return new Point(row0.rect.Left + 20, row0.rect.Center.Y - row0.height / 2);
    }

    // On the table's left border, a third of the way down its first row.
    private static Point OnLeftBorder(RichEditor ed, TableBlock tb)
    {
        var rect = typeof(RichEditor).GetMethod("GetTableRect", NP)!.Invoke(ed, new object[] { tb })!;
        double top = (double)rect.GetType().GetField("Item1")!.GetValue(rect)!;
        return new Point(10 + tb.Indent, top + 6);
    }

    // On the table's TOP border band, just ABOVE the grid — where GetBlockAtPoint finds the paragraph above,
    // so only a border-first lookup finds the table there.
    private static Point OnTopBorderAboveTheGrid(RichEditor ed, TableBlock tb)
    {
        var rect = typeof(RichEditor).GetMethod("GetTableRect", NP)!.Invoke(ed, new object[] { tb })!;
        double top = (double)rect.GetType().GetField("Item1")!.GetValue(rect)!;
        return new Point(10 + tb.Indent + 30, top - 2);
    }

    [AvaloniaTheory]
    [InlineData(2, 2)]
    [InlineData(1, 1)] // a one-cell table has no separate whole-table stage — its cell block IS the table
    public void AViewerRightClickingATable_SelectsItWhole_AndCopyTakesTheTable(int rows, int cols)
    {
        var tb = Table(rows, cols);
        var host = Host(tb);
        var inside = InsideFirstCell(host, tb);
        host.Editor.IsReadOnly = true;

        host.Click(inside, MouseButton.Right);

        var items = MenuItems(host.Editor);
        Assert.Equal(new[] { RichEditorLocalization.GetString("Copy"), RichEditorLocalization.GetString("SelectAll") },
                     items.Select(i => i.Header?.ToString()).ToArray());
        Assert.True(items[0].IsEnabled, "Copy is greyed out on a table with nothing selected");
        Assert.True(Field<bool>(host.Editor, "_cellSelMode"));            // shown selected: the cell fill
        Assert.Same(tb, Field<TableBlock?>(host.Editor, "_cellSelTable"));

        ClearClipboard();
        Invoke(items[0]);
        var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
        Assert.Equal(rows, copied.Rows);
        Assert.Equal(cols, copied.Columns);
    }

    // The contrast: outside a table the viewer's Copy is still greyed out with nothing selected, and nothing
    // gets selected — so the case above is the table's doing, not "Copy is always enabled now".
    [AvaloniaFact]
    public void AViewerRightClickingText_SelectsNothing_AndCopyStaysGreyedOut()
    {
        var host = Host(Table(2, 2));
        host.Editor.IsReadOnly = true;

        host.Click(new Point(10, 8), MouseButton.Right);

        Assert.False(MenuItems(host.Editor)[0].IsEnabled);
        Assert.False(Field<bool>(host.Editor, "_cellSelMode"));
    }

    // Copying a whole-table selection takes that table — here a nested one. The block capture copies the
    // OUTERMOST top-level block, so this came out as the 1×2 table around it; the editable editor did the
    // same after a staged Ctrl+A.
    [AvaloniaFact]
    public void CopyingAWholeNestedTable_TakesThatTable_NotTheOneAroundIt()
    {
        var inner = Table(2, 2);
        var outer = new TableBlock(1, 2);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(inner);
        outer.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "after" } } });
        var doc = new FlowDocument();
        doc.Blocks.Add(outer);
        var ed = new RichEditor { Document = doc };

        Assert.True((bool)typeof(RichEditor).GetMethod("SelectWholeTable", NP)!.Invoke(ed, new object[] { inner })!);
        ClearClipboard();
        typeof(RichEditor).GetMethod("CopySelectionToClipboard", NP)!.Invoke(ed, null);

        var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
        Assert.Equal((2, 2), (copied.Rows, copied.Columns));
    }

    // In a viewer the left/top border selects the whole table (Ctrl+C then copies it); in the editor the same
    // click still places the block caret in front of the table — the edit-mode behaviour is unchanged.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClickingATablesBorder_InAViewerSelectsTheTable_InTheEditorPlacesTheBlockCaret(bool readOnly)
    {
        var tb = Table(2, 2);
        var host = Host(tb);
        host.Editor.IsReadOnly = readOnly;

        host.Click(OnLeftBorder(host.Editor, tb));

        if (readOnly)
        {
            Assert.True(Field<bool>(host.Editor, "_cellSelMode"));
            Assert.Same(tb, Field<TableBlock?>(host.Editor, "_cellSelTable"));
            Assert.Null(host.CaretBlock);
        }
        else
        {
            Assert.Same(tb, host.CaretBlock);
            Assert.False(Field<bool>(host.Editor, "_cellSelMode"));
        }
    }

    // In the EDITOR the border holds the table as a unit — the block caret, with which Del deletes it and
    // Space indents it. Copy takes it too. A right-click on the border now does what a click there does, so
    // the menu is the table's own with Copy enabled; it opened the text menu, or the table menu with Copy
    // greyed out (live check, 2026-09-13).
    [AvaloniaFact]
    public void InTheEditor_RightClickingATablesBorder_OffersCopy_AndCopyTakesTheTable()
    {
        var tb = Table(2, 2);
        var host = Host(tb);

        host.Click(OnTopBorderAboveTheGrid(host.Editor, tb), MouseButton.Right);

        Assert.Same(tb, host.CaretBlock);
        var copy = MenuItems(host.Editor).First(i => i.Header?.ToString() == RichEditorLocalization.GetString("Copy"));
        Assert.True(copy.IsEnabled, "Copy is greyed out on a right-clicked table border");
        ClearClipboard();
        Invoke(copy);
        var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
        Assert.Equal((2, 2), (copied.Rows, copied.Columns));
    }

    // ...and Ctrl+C after a border click, which copied nothing: there was no text selection to copy.
    [AvaloniaFact]
    public void InTheEditor_ClickingATablesBorder_ThenCtrlC_CopiesTheTable()
    {
        var tb = Table(2, 2);
        var host = Host(tb);

        host.Click(OnLeftBorder(host.Editor, tb));
        Assert.Same(tb, host.CaretBlock); // the border click's block caret — the premise
        ClearClipboard();
        host.Key(Key.C, RawInputModifiers.Control);

        var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
        Assert.Equal((2, 2), (copied.Rows, copied.Columns));
    }

    // ---- inline tables: the same border, the same unit -------------------------------------------

    private static InteractionHost HostWithInlineTable(TableBlock inner)
    {
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        host.Inlines.Add(new InlineTable { Table = inner });
        host.Inlines.Add(new Run { Text = " after" });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "above" } } });
        doc.Blocks.Add(host);
        var h = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        h.Render();
        return h;
    }

    // Where the last render drew the inline table's grid.
    private static Rect InlineRect(RichEditor ed, TableBlock tb)
        => ((List<(Rect rect, TableBlock tb)>)typeof(RichEditor).GetField("_inlineTableRects", NP)!.GetValue(ed)!)
            .First(x => ReferenceEquals(x.tb, tb)).rect;

    // On the left border, inside the FIRST row. Halfway down a 2×2 table is exactly the line between its
    // rows, where the editor's row-resize handle wins (a viewer has none) — the first cut stood there and
    // failed in the editor only.
    private static Point OnInlineLeftBorder(Rect r) => new(r.Left, r.Top + 6);

    private static object? MoveCursor
        => typeof(RichEditor).GetProperty("MoveCursor", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    // An inline table's border shows the move cursor, as a top-level table's does; inside its cells it does
    // not. It showed the I-beam: the hover only looked for top-level tables.
    [AvaloniaFact]
    public void AnInlineTablesBorder_ShowsTheMoveCursor()
    {
        var inner = Table(2, 2);
        var host = HostWithInlineTable(inner);
        var r = InlineRect(host.Editor, inner);

        host.Move(OnInlineLeftBorder(r));
        Assert.Same(MoveCursor, host.Editor.Cursor);
        host.Move(new Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
        Assert.NotSame(MoveCursor, host.Editor.Cursor);
    }

    // A click on it selects the whole table — in the editor and in a viewer alike, since an inline table has
    // no block caret to place — and Ctrl+C copies that table.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickingAnInlineTablesBorder_SelectsItWhole_AndCtrlCCopiesIt(bool readOnly)
    {
        var inner = Table(2, 2);
        var host = HostWithInlineTable(inner);
        var r = InlineRect(host.Editor, inner);
        host.Editor.IsReadOnly = readOnly;

        host.Click(OnInlineLeftBorder(r));
        Assert.True(Field<bool>(host.Editor, "_cellSelMode"));
        Assert.Same(inner, Field<TableBlock?>(host.Editor, "_cellSelTable"));

        ClearClipboard();
        host.Key(Key.C, RawInputModifiers.Control);
        var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
        Assert.Equal((2, 2), (copied.Rows, copied.Columns));
    }

    // A right-click on it — on the top band just above the grid, where the host line's text menu came up —
    // offers an enabled Copy that takes the table: the viewer's short menu, the editor's table menu.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightClickingAnInlineTablesBorder_OffersCopy_AndCopyTakesTheTable(bool readOnly)
    {
        var inner = Table(2, 2);
        var host = HostWithInlineTable(inner);
        var r = InlineRect(host.Editor, inner);
        host.Editor.IsReadOnly = readOnly;

        host.Click(new Point(r.Left + 20, r.Top - 2), MouseButton.Right);

        var items = MenuItems(host.Editor);
        var copy = items.First(i => i.Header?.ToString() == RichEditorLocalization.GetString("Copy"));
        Assert.True(copy.IsEnabled, "Copy is greyed out on a right-clicked inline-table border");
        if (!readOnly) Assert.Contains(RichEditorLocalization.GetString("DeleteTable"), items.Select(i => i.Header?.ToString()));
        ClearClipboard();
        Invoke(copy);
        var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
        Assert.Equal((2, 2), (copied.Rows, copied.Columns));
    }
}
