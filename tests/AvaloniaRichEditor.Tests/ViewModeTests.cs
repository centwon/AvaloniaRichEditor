using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A bare editor shows any document as reflowed text; pages are drawn only in page view, which RichEditorView turns
// on. The page setup stays the document's either way — kept, saved and printed. Before ViewMode, an A4 file opened in
// a small editing box drew an A4 page on a grey desk.
public class ViewModeTests
{
    private static FlowDocument A4Doc()
    {
        var doc = new FlowDocument { PageSetup = new PageSetup { PageSize = RichEditorPageSize.A4 } };
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "text" } } });
        return doc;
    }

    [AvaloniaFact]
    public void ABareEditor_ReflowsAnA4Document_AndKeepsItsPaper()
    {
        var ed = new RichEditor { Document = A4Doc() };
        Assert.Equal(RichEditorViewMode.Continuous, ed.ViewMode);
        Assert.Equal(RichEditorPageSize.A4, ed.PageSize); // the document's paper is kept…
        Assert.False(ed.IsPaged);                         // …but not drawn
        ed.Measure(new Size(300, double.PositiveInfinity));
        Assert.True(ed.DesiredSize.Height < 200);         // one reflowed line, not an A4 page tall
        Assert.Contains("\"PageSize\":\"A4\"", ed.ToJson()); // and saved as it came

        ed.ViewMode = RichEditorViewMode.Page;
        Assert.True(ed.IsPaged);
        ed.Measure(new Size(300, double.PositiveInfinity));
        Assert.True(ed.DesiredSize.Height >= 1123); // a page on the desk (the width is clipped to the 300 offered)
    }

    [AvaloniaFact]
    public void TheView_OpensInPageView()
        => Assert.Equal(RichEditorViewMode.Page, new RichEditorView().Editor.ViewMode);

    // Paper, orientation and margins change nothing a continuous editor shows, so the toolbar offers them only in page
    // view, and brings them when the host turns it on.
    [AvaloniaFact]
    public void TheToolbarsPaperFields_ComeWithPageView()
    {
        var ed = new RichEditor { Document = A4Doc() };
        var tb = new RichEditorToolbar { Target = ed, ToolbarLevel = ToolbarLevel.Maximum };
        object? Paper() => typeof(RichEditorToolbar).GetField("_paperCombo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tb);

        Assert.Null(Paper());
        ed.ViewMode = RichEditorViewMode.Page;
        Assert.NotNull(Paper());
        ed.ViewMode = RichEditorViewMode.Continuous;
        Assert.Null(Paper());
    }
}
