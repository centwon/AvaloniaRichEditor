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
        // The editor sits inside a zoom host (LayoutTransformControl) inside the scroller.
        var zoomHost = Assert.IsType<LayoutTransformControl>(scroller.Content);
        Assert.Same(view.Editor, zoomHost.Child);
    }

    [AvaloniaFact]
    public void ZoomFactor_DefaultsToOne_AndClamps()
    {
        var view = new RichEditorView();
        Assert.Equal(1.0, view.ZoomFactor);

        view.ZoomFactor = 2.0;
        Assert.Equal(2.0, view.ZoomFactor);

        view.ZoomFactor = 100;          // above the 5.0 ceiling
        Assert.Equal(5.0, view.ZoomFactor);

        view.ZoomFactor = -1;           // below the 0.2 floor
        Assert.Equal(0.2, view.ZoomFactor);

        view.ZoomFactor = double.NaN;   // non-finite falls back to 1.0
        Assert.Equal(1.0, view.ZoomFactor);
    }

    [AvaloniaFact]
    public void Zoomed_LaysOutWithoutThrowing()
    {
        var view = new RichEditorView();
        view.Editor.LoadHtml("<p>hello world</p>");
        foreach (var z in new[] { 1.0, 1.5, 2.0, 0.5 })
        {
            view.ZoomFactor = z;
            view.Measure(new Avalonia.Size(800, 600));
            view.Arrange(new Avalonia.Rect(0, 0, 800, 600));
        }
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
