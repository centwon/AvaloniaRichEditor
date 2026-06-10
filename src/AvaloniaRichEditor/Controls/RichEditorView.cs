using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AvaloniaRichEditor.Controls;

/// <summary>
/// One-line drop-in editor view (roadmap N3.6 layer ③): a <see cref="RichEditor"/> with a
/// <see cref="RichEditorToolbar"/> docked on top and a vertical scroller around the document.
/// The toolbar is pre-wired (<see cref="RichEditorToolbar.Target"/> = <see cref="Editor"/>), so
/// feature flags and ReadOnly behave consistently out of the box. Reach <see cref="Editor"/> for
/// documents/commands/flags and <see cref="Toolbar"/> for toolbar tweaks; hosts that want their
/// own layout or scrolling should compose the lower layers (①/②) directly instead.
/// </summary>
public class RichEditorView : UserControl
{
    /// <summary>The editor. Load/save documents and set feature flags here.</summary>
    public RichEditor Editor { get; } = new();

    /// <summary>The formatting toolbar, already targeting <see cref="Editor"/>.</summary>
    public RichEditorToolbar Toolbar { get; }

    /// <summary>Creates the bundled toolbar + scrolling editor view.</summary>
    public RichEditorView()
    {
        Toolbar = new RichEditorToolbar { Target = Editor };

        // The bundle owns the scroller (layers ① and ② deliberately don't scroll themselves).
        var scroller = new ScrollViewer
        {
            Content = Editor,
            Padding = new Thickness(12),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dock = new DockPanel();
        DockPanel.SetDock(Toolbar, Dock.Top);
        dock.Children.Add(Toolbar);
        dock.Children.Add(scroller);
        Content = dock;
    }
}
