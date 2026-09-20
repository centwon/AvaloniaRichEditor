using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Page margins reached 1.3.0 as a host API only: the built-in toolbar had paper and orientation, so a
// person using an app built on RichEditorView could not change them at all (spotted by the user right
// after the feature went in). Five steps in millimetres, in a box built like the line-spacing control —
// the icon once, the current step, a chevron that drops the list.
//
// The toolbar is disposed after use: an attached one stays subscribed to the static LanguageChanged and
// breaks other tests' threads (see FindBarTests).
public class ToolbarMarginPickerTests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly List<InteractionHost> _hosts = new();
    public void Dispose() { foreach (var h in _hosts) h.Dispose(); }

    private static T Field<T>(RichEditorToolbar tb, string name)
        => (T)typeof(RichEditorToolbar).GetField(name, NP)!.GetValue(tb)!;

    private static string Label(RichEditorToolbar tb) => Field<TextBlock>(tb, "_marginLabel").Text ?? "";
    private static bool Enabled(RichEditorToolbar tb) => Field<Border>(tb, "_marginBox").IsEnabled;
    private static List<Button> Items(RichEditorToolbar tb) => Field<List<Button>>(tb, "_marginItems");

    private static FlowDocument Doc() => new() { Blocks = { new Paragraph { Inlines = { new Run { Text = "page" } } } } };

    private (RichEditor ed, RichEditorToolbar tb) Paged()
    {
        var (host, tb) = InteractionHost.CreateWithToolbar(new RichEditor { Document = Doc() });
        _hosts.Add(host);
        tb.ToolbarLevel = ToolbarLevel.Maximum; // the page controls live at Maximum (and in the view toolbar)
        host.Editor.PageSize = RichEditorPageSize.A4;
        tb.RefreshPageControls();
        return (host.Editor, tb);
    }

    [AvaloniaFact]
    public void PickingAStep_SetsTheMargins_AndTheDocumentKeepsThem()
    {
        var (ed, tb) = Paged();

        Items(tb)[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // narrow, 10 mm

        Assert.Equal(new PageMargins(10), ed.PageMargin);
        // The pickers edit the OPEN DOCUMENT's setup, so a save carries the change.
        Assert.Equal(new PageMargins(10), ed.Document!.PageSetup!.Margin);
    }

    // The pickers edit the OPEN DOCUMENT, not the host's defaults — which is only observable in the NEXT
    // document: one that carries no page setup of its own starts from the host's. Without this the margins
    // a reader picked for one file would follow every file opened afterwards, and be saved into them (the
    // WinUI port shipped exactly that for paper, see EditDocumentPageSetup).
    [AvaloniaFact]
    public void PickingAStep_DoesNotBecomeTheHostsDefaultForTheNextDocument()
    {
        var (ed, tb) = Paged();
        Items(tb)[4].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // widest, 30 mm
        Assert.Equal(new PageMargins(30), ed.PageMargin);

        ed.Document = Doc(); // a fresh document with no page setup of its own

        Assert.Equal(PageSetup.DefaultMargin, ed.PageMargin);
    }

    // A new document sits on the middle step, so the box names it rather than printing numbers.
    [AvaloniaFact]
    public void TheBoxNamesTheStepTheMarginsAreOn()
    {
        var (ed, tb) = Paged();
        Assert.Equal(RichEditorLocalization.GetString("MarginNormal"), Label(tb));

        ed.PageMargin = new PageMargins(20);
        tb.RefreshPageControls();

        Assert.Equal(RichEditorLocalization.GetString("MarginWide"), Label(tb));
    }

    // A host or a document may carry margins that match no step. Naming one anyway would be a lie about
    // what the page is, and leaving the box blank (the first version did) says nothing — so it states the
    // millimetres, as short as they are regular.
    [AvaloniaFact]
    public void MarginsThatMatchNoStep_AreSpeltOutInMillimetres()
    {
        var (ed, tb) = Paged();

        ed.PageMargin = new PageMargins(42, 6, 8, 31);
        tb.RefreshPageControls();
        Assert.Equal("42 6 8 31mm", Label(tb));

        ed.PageMargin = PageMargins.Symmetric(18, 12);
        tb.RefreshPageControls();
        Assert.Equal("18 / 12mm", Label(tb));
    }

    // Continuous reflows to the control's width: there is no paper, so no margins either.
    [AvaloniaFact]
    public void ThePickerIsDisabledInContinuous()
    {
        var (ed, tb) = Paged();
        Assert.True(Enabled(tb));

        ed.PageSize = RichEditorPageSize.Continuous;
        tb.RefreshPageControls();

        Assert.False(Enabled(tb));
    }
}
