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

    [AvaloniaFact]
    public void RemoveList_IsOfferedInAList_AndTakesItAway()
    {
        var (ed, p) = Editor("item", 2);
        ed.ShowFormattingMenu = true;
        Assert.False(Item(TextMenu(ed), "RemoveList").IsEnabled);

        ed.ToggleBullet();
        var remove = Item(TextMenu(ed), "RemoveList");
        Assert.True(remove.IsEnabled);
        remove.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(ListKind.None, ed.GetCaretFormat().List);
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
