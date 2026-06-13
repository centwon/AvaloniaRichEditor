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

    // Regression: shrinking the hosting window down to tiny widths (which overflows the bundled
    // toolbar and reflows the editor) must not crash. Driven through a real window + layout passes
    // with dispatcher pumping so the toolbar's deferred overflow reparent actually runs.
    [AvaloniaFact]
    public void ShrinkingWindow_DoesNotCrash()
    {
        var view = new RichEditorView();
        view.Editor.Document = null;
        view.Editor.LoadHtml("<p>The quick brown fox jumps over the lazy dog, several times in a row.</p>");
        var win = new Window { Width = 1000, Height = 400, Content = view };
        win.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var w in new double[] { 800, 600, 400, 300, 200, 150, 120, 90, 60, 40, 20, 10, 200, 1000 })
            {
                win.Width = w;
                win.Measure(new Avalonia.Size(w, 400));
                win.Arrange(new Avalonia.Rect(0, 0, w, 400));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }
        finally { win.Close(); }
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
