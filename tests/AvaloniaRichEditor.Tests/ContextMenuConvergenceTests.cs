using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Context-menu items converged with the WinUI port (user decision, 2026-09-13):
// - the character toggles, Clear Formatting and Insert Link are enabled without a selection — they act on the
//   caret's word or arm the format for the next typed text, as the shortcuts do. Insert Link could not: without a
//   selection SetHyperlink did nothing but push an empty undo step, which is why it had been gated;
// - "Open Link" is disabled for a link it will not open (only http/https are launched);
// - "Remove List" is a labelled way out of any list, and Ctrl+Shift+7 toggles numbering (both from the port).
public class ContextMenuConvergenceTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    private static (RichEditor ed, Paragraph p) Editor(string text, int caret)
    {
        var p = new Paragraph { Inlines = { new Run { Text = text } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc };
        p = ed.Document!.Blocks.OfType<Paragraph>().First();
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(f, NP)!.SetValue(ed, new TextPointer(p, caret));
        return (ed, p);
    }

    private static List<MenuItem> TextMenu(RichEditor ed, Run? link = null)
    {
        var items = new List<Control>();
        typeof(RichEditor).GetMethod("BuildTextMenu", NP)!.Invoke(ed, new object?[] { items, false, link, null });
        return Flatten(items).ToList();
    }

    private static IEnumerable<MenuItem> Flatten(IEnumerable<object> items)
    {
        foreach (var mi in items.OfType<MenuItem>())
        {
            yield return mi;
            foreach (var inner in Flatten((mi.ItemsSource ?? mi.Items).Cast<object>())) yield return inner;
        }
    }

    private static MenuItem Item(IEnumerable<MenuItem> items, string key) => items.First(i => (i.Header as string) == Loc(key));

    private static void SetHyperlink(RichEditor ed, string? url) => typeof(RichEditor).GetMethod("SetHyperlink", NP)!.Invoke(ed, new object?[] { url, null });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithoutASelection_TheTogglesAndInsertLink_AreEnabled(bool fullMenu)
    {
        var (ed, _) = Editor("hello world", 2);
        ed.ShowFormattingMenu = fullMenu;
        var items = TextMenu(ed);

        foreach (var key in fullMenu ? new[] { "Bold", "Italic", "Underline", "Strikethrough", "ClearFormatting", "InsertLink" }
                                     : new[] { "Bold", "Italic", "Underline", "InsertLink" })
            Assert.True(Item(items, key).IsEnabled, $"{key} is greyed out without a selection");
    }

    // What makes enabling Insert Link honest: without a selection the link goes on the caret's word, as one undo step.
    [AvaloniaFact]
    public void InsertLink_WithoutASelection_LinksTheCaretsWord_AsOneUndoStep()
    {
        var (ed, _) = Editor("hello world", 2);

        SetHyperlink(ed, "https://example.com/");

        var runs = ed.Document!.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().ToList();
        Assert.Equal("https://example.com/", runs.Single(r => r.Text == "hello").NavigateUri);
        Assert.Null(runs.Single(r => r.Text!.Contains("world")).NavigateUri);

        ed.Undo();
        Assert.All(ed.Document!.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>(), r => Assert.Null(r.NavigateUri));
    }

    // With no word at the caret (an empty line) nothing changes yet — the link waits for the next typed text — so
    // no undo step either (it used to push an empty one). (Right after a word counts as that word: CaretWord.)
    [AvaloniaFact]
    public void InsertLink_WithNoWordAtTheCaret_PushesNoEmptyUndoStep()
    {
        var (ed, _) = Editor("", 0);
        Assert.False(ed.CanUndo);

        SetHyperlink(ed, "https://example.com/");

        Assert.False(ed.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("https://example.com/", true)]
    [InlineData("http://example.com/", true)]
    [InlineData("mailto:someone@example.com", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    public void OpenLink_IsEnabledOnlyForALinkItWillOpen(string url, bool enabled)
    {
        var (ed, p) = Editor("hello", 2);
        var link = p.Inlines.OfType<Run>().First();
        link.NavigateUri = url;

        Assert.Equal(enabled, Item(TextMenu(ed, link), "OpenLink").IsEnabled);

        var linkMenu = new List<Control>();
        typeof(RichEditor).GetMethod("BuildLinkMenu", NP)!.Invoke(ed, new object?[] { linkMenu, false, link });
        Assert.Equal(enabled, Item(Flatten(linkMenu), "OpenLink").IsEnabled);
    }

    // "목록 제거" went (user decision, 2026-09-14): a list is turned off by its own toggle; the item was a second door.
    [AvaloniaFact]
    public void TheListMenu_HasNoRemoveListItem()
    {
        var (ed, _) = Editor("item", 2);
        ed.ShowFormattingMenu = true;
        ed.ToggleBullet();
        Assert.DoesNotContain(TextMenu(ed), i => (i.Header as string) is "목록 제거" or "Remove List");
    }

    // ---- the table menu — the same items in the same order as the WinUI port's (user decision, 2026-09-14) ----

    // The expected sequence is written out identically in the port's ControlContextMenuTests; "—" is a separator.
    private static string[] TableMenuLabels(bool inlineToggle)
    {
        var l = new List<string> { Loc("Cut"), Loc("Copy"), Loc("Paste"), Loc("Delete"), "—",
            Loc("SelectCell"), "—",
            Loc("InsertRowAbove"), Loc("InsertRowBelow"), Loc("DeleteRow"), "—",
            Loc("InsertColumnLeft"), Loc("InsertColumnRight"), Loc("DeleteColumn"), "—",
            Loc("MergeCells"), Loc("UnmergeCells"), "—",
            Loc("CellVerticalAlign"), Loc("CellBackground"), Loc("Margin") };
        if (inlineToggle) l.Add(Loc("InlineWithText"));
        l.Add("—");
        l.Add(Loc("DeleteTable"));
        return l.ToArray();
    }

    private static (RichEditor ed, TableBlock tb) TableEditor(bool nested)
    {
        var tb = new TableBlock(2, 2);
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "above" } } });
        if (nested)
        {
            var outer = new TableBlock(1, 1);
            outer.Cells[0][0].Blocks.Insert(0, tb);
            doc.Blocks.Add(outer);
        }
        else doc.Blocks.Add(tb);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "below" } } });
        return (new RichEditor { Document = doc }, tb);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTableMenu_IsTheSameItemsInTheSameOrder_AsThePorts(bool nested)
    {
        var (ed, tb) = TableEditor(nested);
        var items = new List<Control>();
        typeof(RichEditor).GetMethod("BuildTableMenu", NP)!.Invoke(ed, new object?[] { items, tb, tb.Cells[0][0].Para, false });

        var labels = items.Select(i => i is Separator ? "—" : (i as MenuItem)?.Header as string ?? i.GetType().Name).ToArray();
        Assert.Equal(TableMenuLabels(inlineToggle: !nested), labels); // a table in a cell has no 글자처럼 취급
    }

    // Editing inside a cell, 셀 선택 is right in the text menu — just above the "Table" submenu, not only inside it
    // (user decision, 2026-09-14). The submenu then starts with the rows. Written the same way in the port.
    [AvaloniaFact]
    public void InACell_TheTextMenuOffersSelectCell_RightAboveTheTableSubmenu()
    {
        var (ed, tb) = TableEditor(nested: false);
        foreach (var f in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(f, NP)!.SetValue(ed, new TextPointer(tb.Cells[1][1].Para, 0));
        var items = new List<Control>();
        typeof(RichEditor).GetMethod("BuildTextMenu", NP)!.Invoke(ed, new object?[] { items, false, null, tb });

        var labels = items.Select(i => i is Separator ? "—" : (i as MenuItem)?.Header as string ?? "").ToArray();
        Assert.Equal(new[] { "—", Loc("SelectCell"), Loc("TableOps") }, labels[^3..]);
        Assert.True(((MenuItem)items[^2]).IsEnabled);
        var sub = (MenuItem)items[^1];
        Assert.Equal(Loc("InsertRowAbove"), ((MenuItem)(sub.ItemsSource ?? sub.Items)!.Cast<object>().First()).Header as string);
    }

    // 셀 배경 goes on the cell block when there is one — a one-cell block included — else on the clicked cell.
    [AvaloniaFact]
    public void CellBackground_TakesTheCellBlock_ElseTheClickedCell()
    {
        var (ed, tb) = TableEditor(nested: false);
        foreach (var (_, _, cell) in tb.LogicalCells()) cell.Background = Avalonia.Media.Brushes.Red;
        typeof(RichEditor).GetMethod("SelectCellAsBlock", NP)!.Invoke(ed, new object[] { tb, tb.Cells[0][1], false });

        void ClickNone(int r, int c)
        {
            var sub = (MenuItem)typeof(RichEditor).GetMethod("BuildCellBackgroundSub", NP)!.Invoke(ed, new object[] { tb, r, c })!;
            var panel = (StackPanel)((sub.ItemsSource ?? sub.Items)!.Cast<object>().Single());
            var none = panel.Children.OfType<Button>().Single();
            none.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        }

        ClickNone(1, 1); // the right-click was on (1,1), but the block is (0,1)
        Assert.True(tb.Cells[0][1].Background == null, "the block's cell kept its background");
        Assert.True(tb.Cells[1][1].Background != null, "the clicked cell lost its background though a block was on");

        ed.Undo(); // one step — it restores a snapshot, so read the table back from the document
        var restored = ed.Document!.Blocks.OfType<TableBlock>().Single();
        Assert.True(restored.Cells[0][1].Background != null, "one undo did not bring the background back");
    }

    private static void ApplyFromDialog(RichEditor ed, string url)
        => typeof(RichEditor).GetMethod("ApplyHyperlinkFromDialog", NP)!.Invoke(ed, new object?[] { url, null });

    // The dialog's OK on a blank spot: the address goes in as the link's text (Word) — arming a link for the next
    // typed text showed nothing, so the link looked lost (live check, 2026-09-14). One undo step takes it back.
    [AvaloniaFact]
    public void TheLinkDialog_OnABlankSpot_InsertsTheAddressAsTheLink()
    {
        var (ed, _) = Editor("", 0);

        ApplyFromDialog(ed, " https://example.com/ ");

        var runs = ed.Document!.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().ToList();
        Assert.Contains(runs, r => r.Text == "https://example.com/" && r.NavigateUri == "https://example.com/");
        ed.Undo();
        Assert.Equal("", string.Concat(ed.Document!.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().Select(r => r.Text)));
    }

    [AvaloniaFact]
    public void TheLinkDialog_InAWord_LinksTheWord_AndInsertsNothing()
    {
        var (ed, _) = Editor("hello world", 2);

        ApplyFromDialog(ed, "https://example.com/");

        var runs = ed.Document!.Blocks.OfType<Paragraph>().First().Inlines.OfType<Run>().ToList();
        Assert.Equal("hello world", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal("https://example.com/", runs.Single(r => r.Text == "hello").NavigateUri);
    }

    [AvaloniaFact]
    public void CtrlShift7_TogglesNumbering_AndTheMenuShowsIt()
    {
        var (ed, _) = Editor("item", 2);
        ed.ShowFormattingMenu = true;
        Assert.Equal(new KeyGesture(Key.D7, KeyModifiers.Control | KeyModifiers.Shift), Item(TextMenu(ed), "NumberedList").InputGesture);

        ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D7, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift });

        Assert.Equal(ListKind.Ordered, ed.GetCaretFormat().List);
    }
}
