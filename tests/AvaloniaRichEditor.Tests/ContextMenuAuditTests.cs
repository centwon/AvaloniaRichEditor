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

// Audit of RichEditor.ContextMenu.cs (2026-09-23): the file had no test naming its own members. Each case was
// measured red before its fix.
public class ContextMenuAuditTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string L(string key) => RichEditorLocalization.GetString(key);

    private static List<MenuItem> AllItems(RichEditor ed)
    {
        var menu = (ContextMenu)typeof(RichEditor).GetField("_openContextMenu", NP)!.GetValue(ed)!;
        var result = new List<MenuItem>();
        void Walk(IEnumerable<object?> items)
        {
            foreach (var mi in items.OfType<MenuItem>())
            {
                result.Add(mi);
                Walk((mi.ItemsSource ?? mi.Items)!.Cast<object?>());
            }
        }
        Walk((menu.ItemsSource ?? menu.Items)!.Cast<object?>());
        return result;
    }

    private static MenuItem Sub(MenuItem parent, string header)
        => ((parent.ItemsSource ?? parent.Items)!).OfType<MenuItem>().Single(m => (m.Header as string) == header);

    private static void Invoke(MenuItem mi) => mi.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static TableBlock Table()
    {
        var tb = new TableBlock(2, 2);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                ((Run)tb.Cells[r][c].Para.Inlines[0]).Text = $"c{r}{c}";
        return tb;
    }

    private static InteractionHost Host(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        return host;
    }

    private static Point OnLeftBorder(RichEditor ed, TableBlock tb)
    {
        var rect = typeof(RichEditor).GetMethod("GetTableRect", NP)!.Invoke(ed, new object[] { tb })!;
        double top = (double)rect.GetType().GetField("Item1")!.GetValue(rect)!;
        return new Point(10 + tb.Indent, top + 6);
    }

    private static Point InsideFirstCell(InteractionHost host, TableBlock tb)
    {
        var row0 = host.RowHandles.First(r => ReferenceEquals(r.tb, tb) && r.rowIndex == 0);
        return new Point(row0.rect.Left + 20, row0.rect.Center.Y - row0.height / 2);
    }

    // A left click clears the block caret on every press; the right-click returned before that line, so a table
    // held by its border stayed held after a right-click somewhere else — the caret moved to the clicked text
    // while the block caret still claimed the table.
    [AvaloniaFact]
    public void RightClickingText_DropsABlockCaretLeftOnATable()
    {
        var tb = Table();
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above the table" } } }, tb);
        host.Click(OnLeftBorder(host.Editor, tb));
        Assert.Same(tb, host.CaretBlock); // precondition

        host.Click(new Point(20, 8), MouseButton.Right);

        Assert.Null(host.CaretBlock);
    }

    // …and inside the same table's cell: the stale block caret put the menu in "table held whole" mode, greying
    // 셀 선택 and the row/column items for a cell the user had just clicked.
    [AvaloniaFact]
    public void RightClickingACell_AfterTheBorderWasClicked_OffersTheCellItems()
    {
        var tb = Table();
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above the table" } } }, tb);
        host.Click(OnLeftBorder(host.Editor, tb));
        Assert.Same(tb, host.CaretBlock); // precondition

        host.Click(InsideFirstCell(host, tb), MouseButton.Right);

        var items = AllItems(host.Editor);
        Assert.True(items.First(i => (i.Header as string) == L("SelectCell")).IsEnabled);
        Assert.True(items.First(i => (i.Header as string) == L("InsertRowAbove")).IsEnabled);
    }

    // Control: a right-click ON the border still holds the table (the 2026-09-13 behaviour).
    [AvaloniaFact]
    public void RightClickingTheBorder_StillHoldsTheTable()
    {
        var tb = Table();
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above the table" } } }, tb);

        host.Click(OnLeftBorder(host.Editor, tb), MouseButton.Right);

        Assert.Same(tb, host.CaretBlock);
    }

    // Every other public insert (InsertTable, InsertImage) refuses in a read-only editor; InsertDivider did not.
    [AvaloniaFact]
    public void InsertDivider_DoesNothingWhenReadOnly()
    {
        var host = Host(new Paragraph { Inlines = { new Run { Text = "text" } } });
        host.Editor.IsReadOnly = true;

        host.Editor.InsertDivider();

        Assert.DoesNotContain(host.Editor.Document!.Blocks, b => b is DividerBlock);
        Assert.False(host.Editor.CanUndo);
    }

    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static (InteractionHost host, ImageBlock img) HostWithImage()
    {
        var img = new ImageBlock { Width = 300, Height = 300 };
        img.SetImageData(Png, "image/png");
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above" } } }, img,
                        new Paragraph { Inlines = { new Run { Text = "below" } } });
        host.Click(new Point(80, 120), MouseButton.Right);
        return (host, img);
    }

    private static MenuItem BottomPreset(RichEditor ed, string px)
    {
        var margin = AllItems(ed).Single(i => (i.Header as string) == L("Margin"));
        return Sub(Sub(margin, L("MarginBottom")), px);
    }

    // Picking the margin already in force changed nothing but left an undo step that undid nothing — the cell
    // vertical-alignment radio beside it already skips its own current value.
    [AvaloniaFact]
    public void MarginPreset_AlreadyInForce_LeavesNoUndoStep()
    {
        var (host, img) = HostWithImage();
        Assert.Equal(10, img.MarginBottom); // precondition: the default bottom margin
        Assert.False(host.Editor.CanUndo);  // precondition

        Invoke(BottomPreset(host.Editor, "10 px"));

        Assert.False(host.Editor.CanUndo);
    }

    private static void CloseMenu(RichEditor ed)
        => ((ContextMenu)typeof(RichEditor).GetField("_openContextMenu", NP)!.GetValue(ed)!).Close();

    private static MenuItem Item(RichEditor ed, string key) => AllItems(ed).First(i => (i.Header as string) == L(key));

    // After a table goes away through the menu, typing has to land in the document. The caret stayed in a cell of
    // the removed table (and a border's block caret on the table itself), so the keys typed into a paragraph that
    // is no longer in the document — the text vanished.
    [AvaloniaTheory]
    [InlineData(false)] // right-click inside a cell: the text menu's "Table" submenu
    [InlineData(true)]  // right-click on the border: the table menu
    public void DeleteTableFromTheMenu_ThenTyping_LandsInTheDocument(bool onBorder)
    {
        var tb = Table();
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above the table" } } }, tb);
        host.Click(onBorder ? OnLeftBorder(host.Editor, tb) : InsideFirstCell(host, tb), MouseButton.Right);

        Invoke(Item(host.Editor, "DeleteTable"));
        CloseMenu(host.Editor);
        host.Type("Z");

        Assert.DoesNotContain(host.Editor.Document!.Blocks, b => b is TableBlock); // precondition: it went
        Assert.Contains("Z", host.Editor.GetPlainText());
    }

    private static (InteractionHost host, TableBlock inner) HostWithInlineTable()
    {
        var inner = Table();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "before " });
        p.Inlines.Add(new InlineTable { Table = inner });
        p.Inlines.Add(new Run { Text = " after" });
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above" } } }, p);
        return (host, inner);
    }

    private static Rect NestedRect(RichEditor ed, TableBlock tb)
        => ((List<(Rect rect, TableBlock tb)>)typeof(RichEditor).GetField("_nestedTableRects", NP)!.GetValue(ed)!)
            .Last(x => ReferenceEquals(x.tb, tb)).rect;

    // The same for "treat as character" unchecked on an inline table: the table is re-created as a block (a clone),
    // and the caret was left in a cell of the inline original, which is gone.
    [AvaloniaFact]
    public void InlineTableToBlockFromTheMenu_ThenTyping_LandsInTheDocument()
    {
        var (host, inner) = HostWithInlineTable();
        var r = NestedRect(host.Editor, inner);
        host.Click(new Point(r.Left + 20, r.Top + 8), MouseButton.Right); // inside its first cell

        Invoke(Item(host.Editor, "InlineWithText"));
        CloseMenu(host.Editor);
        host.Type("Z");

        Assert.Contains(host.Editor.Document!.Blocks, b => b is TableBlock); // precondition: it became a block
        Assert.Contains("Z", host.Editor.GetPlainText());
    }

    // …and the other way: a block table held by its border and made a character. The block caret still named the
    // removed table.
    [AvaloniaFact]
    public void TableBlockToInlineFromTheBorderMenu_LeavesNoBlockCaretOnTheRemovedTable()
    {
        var tb = Table();
        var host = Host(new Paragraph { Inlines = { new Run { Text = "above the table" } } }, tb);
        host.Click(OnLeftBorder(host.Editor, tb), MouseButton.Right);
        Assert.Same(tb, host.CaretBlock); // precondition

        Invoke(Item(host.Editor, "InlineWithText"));

        Assert.Null(host.CaretBlock);
        CloseMenu(host.Editor);
        host.Type("Z");
        Assert.Contains("Z", host.Editor.GetPlainText());
    }

    private static MenuItem TopPreset(RichEditor ed, string header)
    {
        var margin = AllItems(ed).Single(i => (i.Header as string) == L("Margin"));
        return Sub(Sub(margin, L("MarginTop")), header);
    }

    // A picture's top is "auto" (one line gap) by default since PR #52. No px preset showed it, and once one was
    // picked nothing could bring it back (user decision, 2026-09-23: an Auto item).
    [AvaloniaFact]
    public void TopMargin_OffersAuto_CheckedByDefault_AndRestoresIt()
    {
        var (host, img) = HostWithImage();
        Assert.True(double.IsNaN(img.MarginTop)); // precondition: the default
        Assert.True(TopPreset(host.Editor, L("MarginAuto")).IsChecked);

        Invoke(TopPreset(host.Editor, "10 px"));
        Assert.Equal(10, img.MarginTop);
        CloseMenu(host.Editor);
        host.Click(new Point(80, 120), MouseButton.Right);
        Invoke(TopPreset(host.Editor, L("MarginAuto")));

        Assert.True(double.IsNaN(img.MarginTop));
    }

    // A paragraph has no "auto" top: its gap is the line spacing's.
    [AvaloniaFact]
    public void ParagraphTopMargin_HasNoAuto()
    {
        var host = Host(new Paragraph { Inlines = { new Run { Text = "text" } } });
        host.Editor.ShowFormattingMenu = true;
        host.Click(new Point(10, 8), MouseButton.Right);

        Assert.DoesNotContain(AllItems(host.Editor), i => (i.Header as string) == L("MarginAuto"));
    }

    // Tab in the last cell of a table that is NOT the document's last jumped into the next table below; it adds a
    // row, as it always did in the last table (user decision, 2026-09-23).
    [AvaloniaFact]
    public void TabInTheLastCell_OfAnEarlierTable_AddsARow()
    {
        var first = Table();
        var second = Table();
        var host = Host(new Paragraph { Inlines = { new Run { Text = "a" } } }, first,
                        new Paragraph { Inlines = { new Run { Text = "between" } } }, second);
        host.Click(InsideFirstCell(host, first));
        host.Key(Key.Tab); host.Key(Key.Tab); host.Key(Key.Tab); // to the last cell, c11

        host.Key(Key.Tab);

        Assert.Equal(3, first.Rows);
        Assert.Equal(2, second.Rows);
    }

    // Control: inside a cell's nested table, Tab past its last cell still steps out to the host's next cell.
    [AvaloniaFact]
    public void TabInTheLastCell_OfANestedTable_StillStepsOut()
    {
        var inner = Table();
        var outer = new TableBlock(1, 2);
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(inner);
        outer.Cells[0][0].Blocks.Add(new Paragraph { Inlines = { new Run { Text = "after" } } });
        var host = Host(new Paragraph { Inlines = { new Run { Text = "a" } } }, outer);
        typeof(RichEditor).GetMethod("FocusCell", NP)!.Invoke(host.Editor, new object[] { inner.Cells[1][1].Para });

        host.Key(Key.Tab);

        Assert.Equal(2, inner.Rows);
        Assert.Equal(1, outer.Rows);
        Assert.Same(outer.Cells[0][1].Para, host.Caret.Paragraph);
    }

    // Control: a different preset still applies and can be undone.
    [AvaloniaFact]
    public void MarginPreset_NewValue_AppliesWithAnUndoStep()
    {
        var (host, img) = HostWithImage();

        Invoke(BottomPreset(host.Editor, "20 px"));

        Assert.Equal(20, img.MarginBottom);
        Assert.True(host.Editor.CanUndo);
    }
}
