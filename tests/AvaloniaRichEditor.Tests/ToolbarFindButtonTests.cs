using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The toolbar's Find button (from the WinUI port, 2026-09-19). It shows only while something answers
// RichEditor.FindRequested — the WinUI port first shipped it unconditionally, and on a bare editor + toolbar it opened
// nothing. Hosts are disposed: an attached toolbar stays subscribed to the static LanguageChanged (FindBarTests).
public class ToolbarFindButtonTests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly List<InteractionHost> _hosts = new();
    public void Dispose() { foreach (var h in _hosts) h.Dispose(); }

    private static Control? FindButton(RichEditorToolbar tb) => (Control?)typeof(RichEditorToolbar).GetField("_findBtn", NP)!.GetValue(tb);

    private static FlowDocument Doc() => new() { Blocks = { new Paragraph { Inlines = { new Run { Text = "apple banana" } } } } };

    [AvaloniaFact]
    public void OnABareEditor_TheButtonShowsOnlyWhileSomethingAnswersFind()
    {
        var (host, tb) = InteractionHost.CreateWithToolbar(new RichEditor { Document = Doc() });
        _hosts.Add(host);
        Assert.NotNull(FindButton(tb));
        Assert.False(FindButton(tb)!.IsVisible); // nothing answers Ctrl+F: the button would open nothing

        EventHandler<bool> h = (_, _) => { };
        host.Editor.FindRequested += h;
        Assert.True(FindButton(tb)!.IsVisible);
        host.Editor.FindRequested -= h;
        Assert.False(FindButton(tb)!.IsVisible);
    }

    [AvaloniaFact]
    public void InAView_ClickingTheButton_OpensTheFindBar()
    {
        var view = new RichEditorView(); view.Editor.Document = Doc();
        var host = InteractionHost.CreateWithView(view);
        _hosts.Add(host);
        var btn = FindButton(view.Toolbar)!;
        Assert.True(btn.IsVisible);

        host.ClickControl(btn);

        var bar = (Border)typeof(RichEditorView).GetField("_findBarHost", NP)!.GetValue(view)!;
        Assert.True(bar.IsVisible, "the toolbar's Find button did not open the find bar");
    }

    // Find only reads: a viewer's toolbar keeps it. Turned off by the capability flag.
    [AvaloniaFact]
    public void AViewerKeepsIt_AndTheFlagTurnsItOff()
    {
        var view = new RichEditorView(); view.Editor.Document = Doc();
        view.Editor.IsReadOnly = true;
        var host = InteractionHost.CreateWithView(view);
        _hosts.Add(host);
        Assert.True(FindButton(view.Toolbar)!.IsVisible);

        view.Editor.AllowFindReplace = false;
        Assert.False(FindButton(view.Toolbar)!.IsVisible);
    }
}
