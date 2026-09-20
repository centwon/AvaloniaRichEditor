using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Page margins reached 1.3.0 as a host API only: the built-in toolbar had paper and orientation, so a
// person using an app built on RichEditorView could not change them at all (spotted by the user right
// after the feature went in). Presets on the toolbar, as Word and HWP lead with.
//
// The toolbar is disposed after use: an attached one stays subscribed to the static LanguageChanged and
// breaks other tests' threads (see FindBarTests).
public class ToolbarMarginPickerTests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly List<InteractionHost> _hosts = new();
    public void Dispose() { foreach (var h in _hosts) h.Dispose(); }

    private static ComboBox Picker(RichEditorToolbar tb)
        => (ComboBox)typeof(RichEditorToolbar).GetField("_marginCombo", NP)!.GetValue(tb)!;

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
    public void PickingAPreset_SetsTheMargins_AndTheDocumentKeepsThem()
    {
        var (ed, tb) = Paged();
        var picker = Picker(tb);

        picker.SelectedIndex = 1; // narrow

        Assert.Equal(new Thickness(24, 20, 24, 20), ed.PageMargin);
        // The pickers edit the OPEN DOCUMENT's setup, so a save carries the change.
        Assert.Equal(new Thickness(24, 20, 24, 20), ed.Document!.PageSetup!.Margin);
    }

    // The pickers edit the OPEN DOCUMENT, not the host's defaults — which is only observable in the NEXT
    // document: one that carries no page setup of its own starts from the host's. Without this the
    // margins a reader picked for one file would follow every file opened afterwards, and be saved into
    // them (the WinUI port shipped exactly that for paper, see EditDocumentPageSetup).
    [AvaloniaFact]
    public void PickingAPreset_DoesNotBecomeTheHostsDefaultForTheNextDocument()
    {
        var (ed, tb) = Paged();
        Picker(tb).SelectedIndex = 2; // wide
        Assert.Equal(new Thickness(96, 80, 96, 80), ed.PageMargin);

        ed.Document = Doc(); // a fresh document with no page setup of its own

        Assert.Equal(PageSetup.DefaultMargin, ed.PageMargin);
    }

    [AvaloniaFact]
    public void ThePickerShowsTheEditorsCurrentMargins()
    {
        var (ed, tb) = Paged();

        var wide = new Thickness(96, 80, 96, 80);
        ed.PageMargin = wide; // set from code, not from the picker
        tb.RefreshPageControls();

        Assert.Equal(wide, ((ComboBoxItem)Picker(tb).SelectedItem!).Tag);
    }

    // A host or a document may carry margins that match no preset. Showing one anyway would be a lie about
    // what the page is — the zoom combo has the same rule for an off-grid zoom.
    [AvaloniaFact]
    public void MarginsThatMatchNoPreset_SelectNothing()
    {
        var (ed, tb) = Paged();

        ed.PageMargin = new Thickness(160, 24, 32, 120);
        tb.RefreshPageControls();

        Assert.Null(Picker(tb).SelectedItem);
    }

    // Continuous reflows to the control's width: there is no paper, so no margins either.
    [AvaloniaFact]
    public void ThePickerIsDisabledInContinuous()
    {
        var (ed, tb) = Paged();
        Assert.True(Picker(tb).IsEnabled);

        ed.PageSize = RichEditorPageSize.Continuous;
        tb.RefreshPageControls();

        Assert.False(Picker(tb).IsEnabled);
    }
}
