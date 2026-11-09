using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The view turned ShowPageBoundaries off in its constructor. Set by code, that counted as the HOST's page setup, so once
// the host also set a paper size by code every new document captured {A4, no boundaries} and saved it — while the WinUI
// view saved boundaries on: the two controls wrote different bytes for the same host code (2026-11-08).
public class ViewPageBoundaryTests
{
    [AvaloniaFact]
    public void HostPaperSetByCode_KeepsPageBoundaries()
    {
        var view = new RichEditorView();
        view.Editor.PageSize = RichEditorPageSize.A4; // the host's choice, by code
        view.Editor.Document = new FlowDocument();

        Assert.True(view.Editor.ShowPageBoundaries);
        Assert.DoesNotContain("\"ShowPageBoundaries\":false", view.Editor.ToJson());
    }
}
