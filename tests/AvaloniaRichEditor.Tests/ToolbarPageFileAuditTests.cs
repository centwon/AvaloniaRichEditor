using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditorToolbar.PageFile.cs (2026-09-23). The toolbar mirrors the editor's page state onto its
// combos; a paper or zoom change that did NOT come from the combo must not be answered as if the user had picked it.
public class ToolbarPageFileAuditTests
{
    private static RichEditorToolbar Toolbar(RichEditor ed) => new() { Target = ed, ToolbarLevel = ToolbarLevel.Maximum };

    private static FlowDocument DocWith(PageSetup setup)
    {
        var doc = new FlowDocument { PageSetup = setup };
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "text" } } });
        return doc;
    }

    // Control: a document that asks for A4 WITHOUT boundaries keeps them off (this path was already right — the
    // document's setup is applied after the paper — and guards the fix below from breaking it).
    [AvaloniaFact]
    public void OpeningADocument_KeepsItsPageBoundariesOff()
    {
        var ed = new RichEditor();
        _ = Toolbar(ed);

        ed.Document = DocWith(new PageSetup { PageSize = RichEditorPageSize.A4, ShowPageBoundaries = false });

        Assert.Equal(RichEditorPageSize.A4, ed.PageSize); // precondition
        Assert.False(ed.ShowPageBoundaries);
        Assert.False(ed.Document!.PageSetup!.ShowPageBoundaries);
    }

    // A host setting PageSize in code with boundaries off: mirroring the paper onto the combo fired the combo's
    // own handler, which switched boundaries on as a picked paper does.
    [AvaloniaFact]
    public void AHostSettingThePaper_KeepsItsPageBoundariesOff()
    {
        var ed = new RichEditor { ShowPageBoundaries = false };
        _ = Toolbar(ed);

        ed.PageSize = RichEditorPageSize.A4;

        Assert.False(ed.ShowPageBoundaries);
    }

    // An off-grid zoom (Ctrl+wheel) empties the zoom combo; clearing it outside the sync guard fired its handler
    // with index -1, which it reads as "Fit" — so a paper change snapped the host's zoom to fit-width.
    [AvaloniaFact]
    public void APaperChange_DoesNotSnapAnOffGridZoomToFit()
    {
        var ed = new RichEditor();
        var tb = Toolbar(ed);
        int fits = 0;
        tb.ZoomGetter = () => 1.1;
        tb.IsFitWidthGetter = () => false;
        tb.FitWidthAction = () => fits++;

        ed.PageSize = RichEditorPageSize.A4;

        Assert.Equal(0, fits);
    }
}
