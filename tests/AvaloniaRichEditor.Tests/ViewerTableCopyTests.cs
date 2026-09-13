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

    private static FieldInfo CopiedInlinesField
        => typeof(RichEditor).GetField("_internalClipboard", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static List<Inline>? CopiedInlines => (List<Inline>?)CopiedInlinesField.GetValue(null);

    // The internal clipboard is STATIC — shared by every editor, so by every test. A copy test that does not
    // clear it first can pass on a table an earlier test left there, with its own copy doing nothing.
    private static void ClearClipboard()
    {
        CopiedBlocksField.SetValue(null, null);
        CopiedInlinesField.SetValue(null, null);
    }

    // What a copy of the 2×2 table left on the clipboard: an inline table as an inline table (the inline
    // list — paste puts it at the caret), any other as a block (user decision, 2026-09-13).
    private static void AssertCopiedTheTable(bool inline)
    {
        if (inline)
        {
            Assert.Null(CopiedBlocks);
            var it = Assert.IsType<InlineTable>(Assert.Single(CopiedInlines!));
            Assert.Equal((2, 2), (it.Table.Rows, it.Table.Columns));
        }
        else
        {
            var copied = Assert.IsType<TableBlock>(Assert.Single(CopiedBlocks!));
            Assert.Equal((2, 2), (copied.Rows, copied.Columns));
        }
    }

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

    // A 2×2 table inside the first cell of a 1×2 table — the nested case, beside the inline one above.
    private static InteractionHost HostWithCellTable(TableBlock inner)
    {
        var outer = new TableBlock(1, 2);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(inner);
        outer.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "after" } } });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "above" } } });
        doc.Blocks.Add(outer);
        var h = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        h.Render();
        return h;
    }

    private static InteractionHost HostWithNested(string where, TableBlock inner)
        => where == "inline" ? HostWithInlineTable(inner) : HostWithCellTable(inner);

    // Where the last render drew the nested table's grid.
    private static Rect NestedRect(RichEditor ed, TableBlock tb)
        => ((List<(Rect rect, TableBlock tb)>)typeof(RichEditor).GetField("_nestedTableRects", NP)!.GetValue(ed)!)
            .Last(x => ReferenceEquals(x.tb, tb)).rect;

    // On the left border, inside the FIRST row. Halfway down a 2×2 table is exactly the line between its
    // rows, where the editor's row-resize handle wins (a viewer has none) — the first cut stood there and
    // failed in the editor only.
    private static Point OnInlineLeftBorder(Rect r) => new(r.Left, r.Top + 6);

    private static object? MoveCursor
        => typeof(RichEditor).GetProperty("MoveCursor", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    // An inline table's border shows the move cursor, as a top-level table's does; inside its cells it does
    // not. It showed the I-beam: the hover only looked for top-level tables.
    // (A table in a cell, too — it showed nothing either: only inline tables were recorded.)
    [AvaloniaTheory]
    [InlineData("inline")]
    [InlineData("cell")]
    public void ANestedTablesBorder_ShowsTheMoveCursor(string where)
    {
        var inner = Table(2, 2);
        var host = HostWithNested(where, inner);
        var r = NestedRect(host.Editor, inner);

        host.Move(OnInlineLeftBorder(r));
        Assert.Same(MoveCursor, host.Editor.Cursor);
        host.Move(new Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
        Assert.NotSame(MoveCursor, host.Editor.Cursor);
    }

    // A click on it selects the whole table — in the editor and in a viewer alike, since an inline table has
    // no block caret to place — and Ctrl+C copies that table.
    [AvaloniaTheory]
    [InlineData("inline", false)]
    [InlineData("inline", true)]
    [InlineData("cell", false)]
    [InlineData("cell", true)]
    public void ClickingANestedTablesBorder_SelectsItWhole_AndCtrlCCopiesIt(string where, bool readOnly)
    {
        var inner = Table(2, 2);
        var host = HostWithNested(where, inner);
        var r = NestedRect(host.Editor, inner);
        host.Editor.IsReadOnly = readOnly;

        host.Click(OnInlineLeftBorder(r));
        Assert.True(Field<bool>(host.Editor, "_cellSelMode"));
        Assert.Same(inner, Field<TableBlock?>(host.Editor, "_cellSelTable"));

        ClearClipboard();
        host.Key(Key.C, RawInputModifiers.Control);
        AssertCopiedTheTable(where == "inline");
    }

    // A right-click on it — on the top band just above the grid, where the host line's text menu came up —
    // offers an enabled Copy that takes the table: the viewer's short menu, the editor's table menu.
    [AvaloniaTheory]
    [InlineData("inline", false)]
    [InlineData("inline", true)]
    [InlineData("cell", false)]
    [InlineData("cell", true)]
    public void RightClickingANestedTablesBorder_OffersCopy_AndCopyTakesTheTable(string where, bool readOnly)
    {
        var inner = Table(2, 2);
        var host = HostWithNested(where, inner);
        var r = NestedRect(host.Editor, inner);
        host.Editor.IsReadOnly = readOnly;

        host.Click(new Point(r.Left + 20, r.Top - 2), MouseButton.Right);

        var items = MenuItems(host.Editor);
        var copy = items.First(i => i.Header?.ToString() == RichEditorLocalization.GetString("Copy"));
        Assert.True(copy.IsEnabled, "Copy is greyed out on a right-clicked inline-table border");
        if (!readOnly) Assert.Contains(RichEditorLocalization.GetString("DeleteTable"), items.Select(i => i.Header?.ToString()));
        ClearClipboard();
        Invoke(copy);
        AssertCopiedTheTable(where == "inline");
    }

    // Every table in the document, at any depth — cells and inline tables included.
    private static IEnumerable<TableBlock> AllTables(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
        {
            if (b is TableBlock tb)
            {
                yield return tb;
                foreach (var row in tb.Cells)
                    foreach (var cell in row)
                        foreach (var t in AllTables(cell.Blocks)) yield return t;
            }
            else if (b is Paragraph p)
                foreach (var it in p.Inlines.OfType<InlineTable>())
                    foreach (var t in AllTables(new Block[] { it.Table })) yield return t;
        }
    }

    private static int Tables2x2(RichEditor ed) => AllTables(ed.Document!.Blocks).Count(t => t.Rows == 2 && t.Columns == 2);

    // Cut and Delete take a table held whole — selected whole by its border (a nested table), or held by the
    // block caret (a top-level one) — and REMOVE it, from the keyboard and from the menu. Cut copied the table
    // and then cleared its cells, leaving an empty grid ("cut it, and it is still there", live check
    // 2026-09-13), Delete emptied it the same way, and the block caret's cut removed nothing. Delete removing it
    // too is the user's decision. The caret lands in the document — not inside the removed table, where a
    // whole-table selection had put it — and one undo brings the table back (one checkpoint, not two).
    [AvaloniaTheory]
    [InlineData("cell", "ctrl+x")]
    [InlineData("cell", "menu cut")]
    [InlineData("cell", "delete")]
    [InlineData("cell", "menu delete")]
    [InlineData("inline", "ctrl+x")]
    [InlineData("inline", "menu cut")]
    [InlineData("inline", "delete")]
    [InlineData("inline", "menu delete")]
    [InlineData("top", "ctrl+x")]
    [InlineData("top", "menu cut")]
    [InlineData("top", "delete")]
    [InlineData("top", "menu delete")]
    public void CuttingOrDeletingATableHeldWhole_RemovesIt_AndOneUndoBringsItBack(string where, string how)
    {
        bool viaMenu = how.StartsWith("menu"), cut = how.EndsWith("cut") || how == "ctrl+x";
        var inner = Table(2, 2);
        InteractionHost host;
        Point border;
        if (where == "top") { host = Host(inner); border = OnLeftBorder(host.Editor, inner); }
        else { host = HostWithNested(where, inner); border = OnInlineLeftBorder(NestedRect(host.Editor, inner)); }
        Assert.Equal(1, Tables2x2(host.Editor));

        ClearClipboard();
        if (viaMenu)
        {
            host.Click(border, MouseButton.Right);
            var label = RichEditorLocalization.GetString(cut ? "Cut" : "Delete");
            var item = MenuItems(host.Editor).First(i => i.Header?.ToString() == label);
            Assert.True(item.IsEnabled, $"{label} is greyed out on a table held whole");
            Invoke(item);
            // A click on a real menu item closes the menu; raising Click does not — and while the menu is
            // open the next key (the Ctrl+Z below) goes to it, not to the editor.
            ((ContextMenu?)typeof(RichEditor).GetField("_openContextMenu", NP)!.GetValue(host.Editor))?.Close();
        }
        else
        {
            host.Click(border);
            if (cut) host.Key(Key.X, RawInputModifiers.Control);
            else host.Key(Key.Delete);
        }

        if (cut) AssertCopiedTheTable(where == "inline");
        else { Assert.Null(CopiedBlocks); Assert.Null(CopiedInlines); } // Delete copies nothing
        Assert.Equal(0, Tables2x2(host.Editor)); // gone — not an emptied grid
        var paragraphs = (List<Paragraph>)typeof(RichEditor).GetMethod("GetAllParagraphsInOrder", NP)!.Invoke(host.Editor, null)!;
        Assert.Contains(host.Caret.Paragraph!, paragraphs);

        host.Key(Key.Z, RawInputModifiers.Control);
        Assert.Equal(1, Tables2x2(host.Editor));
    }

    // An inline table copies AS an inline table — the clipboard's inline list, which paste puts at the caret —
    // not a block table splitting the paragraph it lands in (user decision, 2026-09-13). The paste half is
    // InsertInlines, the paste path the inline list takes (a headless test has no system clipboard to go
    // through). A top-level table still copies as a block (the tests above).
    [AvaloniaFact]
    public void AnInlineTable_CopiesAsAnInlineTable_AndPastesInlineAtTheCaret()
    {
        var inner = Table(2, 2);
        var host = HostWithInlineTable(inner);
        host.Click(OnInlineLeftBorder(NestedRect(host.Editor, inner)));
        ClearClipboard();
        var inlinesField = typeof(RichEditor).GetField("_internalClipboard", BindingFlags.NonPublic | BindingFlags.Static)!;
        inlinesField.SetValue(null, null);
        host.Key(Key.C, RawInputModifiers.Control);

        Assert.Null(CopiedBlocks); // no block list: that is what made paste insert a block table
        var copied = Assert.IsType<InlineTable>(Assert.Single((List<Inline>)inlinesField.GetValue(null)!));
        Assert.Equal((2, 2), (copied.Table.Rows, copied.Table.Columns));

        var above = (Paragraph)host.Editor.Document!.Blocks[0]; // "above" — the caret between "ab" and "ove"
        var at = new TextPointer(above, 2);
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(f, NP)!.SetValue(host.Editor, at);
        typeof(RichEditor).GetMethod("InsertInlines", NP)!.Invoke(host.Editor, new object[] { new List<Inline> { (Inline)copied.Clone() } });

        Assert.Contains(above.Inlines, i => i is InlineTable);
        Assert.Equal("ab", ((Run)above.Inlines[0]).Text);         // inside the line: text on both sides
        Assert.IsType<Run>(above.Inlines[^1]);
        Assert.Empty(host.Editor.Document!.Blocks.OfType<TableBlock>()); // no block table appeared
        Assert.Equal(2, Tables2x2(host.Editor));                    // the original and the pasted one
    }

    // Where a nested table's border band overlaps the one around it (a cell's padding apart), the INNER table
    // is taken — by a click and a right-click alike. Two depths: a table in a top-level table's cell (the
    // nested lookup is asked before the top-level one), and a table in a nested table's cell (the rects are
    // walked innermost-first).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhereBordersOverlap_TheInnerTableIsTaken(bool twoDeep)
    {
        var inner = Table(2, 2);
        var target = inner;
        if (twoDeep)
        {
            target = Table(2, 2);
            inner.Cells[0][0].Blocks.Clear();
            inner.Cells[0][0].Blocks.Add(target);
            inner.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "x" } } });
        }
        var host = HostWithCellTable(inner);
        var r = NestedRect(host.Editor, target);
        var both = new Point(r.Left - 2, r.Top + 6); // 2 from the target's left edge, 3 from the one around it

        host.Click(both);
        Assert.Same(target, Field<TableBlock?>(host.Editor, "_cellSelTable"));
        Assert.Null(host.CaretBlock);

        host.Click(both, MouseButton.Right);
        Assert.Same(target, Field<TableBlock?>(host.Editor, "_cellSelTable"));
        Assert.Null(host.CaretBlock);
    }
}
