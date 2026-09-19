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

// The find UI (from the WinUI port, 2026-09-19): the library had the search engine but no way to reach it — no
// Ctrl+F, no F3, no bar. Driven through the window.
public class FindBarTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static FlowDocument Doc() => new()
    {
        Blocks = { new Paragraph { Inlines = { new Run { Text = "apple banana apple cherry apple" } } } },
    };

    private static string Selected(RichEditor ed)
    {
        var s = (TextPointer)typeof(RichEditor).GetField("_selectionStart", NP)!.GetValue(ed)!;
        var e = (TextPointer)typeof(RichEditor).GetField("_selectionEnd", NP)!.GetValue(ed)!;
        return new TextRange(s, e).GetText();
    }

    private static int SelectionStart(RichEditor ed) => ((TextPointer)typeof(RichEditor).GetField("_selectionStart", NP)!.GetValue(ed)!).Offset;

    // ---- the editor's entry points --------------------------------------------------------------------

    [AvaloniaFact]
    public void CtrlF_AndCtrlH_AskForTheFindUi()
    {
        var host = InteractionHost.Create(new RichEditor { Document = Doc() });
        var asked = new List<bool>();
        host.Editor.FindRequested += (_, withReplace) => asked.Add(withReplace);

        host.Key(Key.F, RawInputModifiers.Control);
        host.Key(Key.H, RawInputModifiers.Control);

        Assert.Equal(new[] { false, true }, asked);
    }

    // Find only reads, so a viewer has it; Find + Replace edits, so it does not.
    [AvaloniaFact]
    public void AViewer_GetsFind_ButNotReplace()
    {
        var host = InteractionHost.Create(new RichEditor { Document = Doc(), IsReadOnly = true });
        var asked = new List<bool>();
        host.Editor.FindRequested += (_, withReplace) => asked.Add(withReplace);

        host.Key(Key.F, RawInputModifiers.Control);
        host.Key(Key.H, RawInputModifiers.Control);

        Assert.Equal(new[] { false }, asked);
    }

    [AvaloniaFact]
    public void F3_RepeatsTheLastSearch_ShiftF3Backwards()
    {
        var host = InteractionHost.Create(new RichEditor { Document = Doc() });
        Assert.True(host.Editor.FindNext("apple", matchCase: false));
        int first = SelectionStart(host.Editor);
        Assert.Equal("apple", host.Editor.LastFindQuery);

        host.Key(Key.F3);
        Assert.Equal("apple", Selected(host.Editor));
        int second = SelectionStart(host.Editor);
        Assert.True(second > first, $"F3 did not move on ({first} -> {second})");

        host.Key(Key.F3, RawInputModifiers.Shift);
        Assert.Equal(first, SelectionStart(host.Editor));
    }

    // ---- the view's bar ---------------------------------------------------------------------------------

    private static Border BarHost(RichEditorView v) => (Border)typeof(RichEditorView).GetField("_findBarHost", NP)!.GetValue(v)!;
    private static TextBox FindBox(RichEditorView v) => (TextBox)typeof(RichEditorView).GetField("_findBox", NP)!.GetValue(v)!;

    private static void KeyIn(Control c, Key key, KeyModifiers mods = KeyModifiers.None)
        => c.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = c });

    [AvaloniaFact]
    public void TheViewsBar_Opens_Finds_AndCloses()
    {
        var view = new RichEditorView(); view.Editor.Document = Doc();
        var host = InteractionHost.CreateWithView(view);

        host.Key(Key.F, RawInputModifiers.Control);
        Assert.True(BarHost(view).IsVisible, "Ctrl+F did not open the find bar");

        var box = FindBox(view);
        box.Text = "cherry";
        KeyIn(box, Key.Enter);
        Assert.Equal("cherry", Selected(view.Editor));

        KeyIn(box, Key.Escape);
        Assert.False(BarHost(view).IsVisible);
        Assert.Null(view.Editor.FindHighlightQuery); // the highlight-all goes with the bar
    }

    [AvaloniaFact]
    public void ReopeningTheBar_PrefillsTheLastQuery()
    {
        var view = new RichEditorView(); view.Editor.Document = Doc();
        var host = InteractionHost.CreateWithView(view);
        view.Editor.FindNext("banana", matchCase: false);

        host.Key(Key.F, RawInputModifiers.Control);
        Assert.Equal("banana", FindBox(view).Text);
    }

    // A host with its own find UI turns the bar off; the request still reaches it.
    [AvaloniaFact]
    public void WithTheBuiltInBarOff_TheRequestStillReachesTheHost()
    {
        var view = new RichEditorView { ShowBuiltInFindBar = false }; view.Editor.Document = Doc();
        var host = InteractionHost.CreateWithView(view);
        bool asked = false;
        view.Editor.FindRequested += (_, _) => asked = true;

        host.Key(Key.F, RawInputModifiers.Control);

        Assert.True(asked);
        Assert.False(BarHost(view).IsVisible);
    }
}
