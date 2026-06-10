using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorView (roadmap N3.6 layer ③): the bundled drop-in must come pre-wired — toolbar
// targeting the editor, the editor inside the view-owned scroller, and ReadOnly flowing through.
public class RichEditorViewTests
{
    [AvaloniaFact]
    public void Toolbar_IsWiredToEditor()
    {
        var view = new RichEditorView();
        Assert.Same(view.Editor, view.Toolbar.Target);
        Assert.True(view.Toolbar.IsVisible);
    }

    [AvaloniaFact]
    public void Editor_LivesInsideTheViewOwnedScroller()
    {
        var view = new RichEditorView();
        var dock = Assert.IsType<DockPanel>(view.Content);
        var scroller = Assert.Single(dock.Children.OfType<ScrollViewer>());
        Assert.Same(view.Editor, scroller.Content);
    }

    [AvaloniaFact]
    public void ReadOnlyEditor_HidesBundledToolbar()
    {
        var view = new RichEditorView();
        view.Editor.IsReadOnly = true;
        Assert.False(view.Toolbar.IsVisible);
        view.Editor.IsReadOnly = false;
        Assert.True(view.Toolbar.IsVisible);
    }

    [AvaloniaFact]
    public void Editor_AcceptsContentThroughTheBundle()
    {
        var view = new RichEditorView();
        view.Editor.LoadHtml("<p>hello</p>");
        Assert.NotNull(view.Editor.Document);
        Assert.Contains("hello", view.Editor.ToHtml());
    }
}
