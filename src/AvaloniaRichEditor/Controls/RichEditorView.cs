using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

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

    /// <inheritdoc cref="ZoomFactor"/>
    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<RichEditorView, double>(nameof(ZoomFactor), 1.0, coerce: CoerceZoom);

    /// <summary>Visual zoom for the document area (1.0 = 100%). The toolbar is never scaled. Scaling
    /// is applied around the editor, which reflows to the zoomed width — text stays crisp and no
    /// horizontal scrollbar appears in the continuous layout. Clamped to 0.2–5.0.</summary>
    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    private static double CoerceZoom(AvaloniaObject _, double v)
        => double.IsFinite(v) ? Math.Clamp(v, 0.2, 5.0) : 1.0;

    // The editor lives inside this; its LayoutTransform carries the zoom. LayoutTransform (not
    // RenderTransform) so the scroller's extent and the editor's reflow width both follow the zoom.
    // Top-aligned so a short document anchors at the top of the scroller instead of centering
    // vertically (LayoutTransformControl centers its child in any slack it's given).
    private readonly LayoutTransformControl _zoomHost;
    private readonly ScrollViewer _scroller;

    /// <summary>Creates the bundled toolbar + scrolling editor view.</summary>
    public RichEditorView()
    {
        Toolbar = new RichEditorToolbar { Target = Editor };

        _zoomHost = new LayoutTransformControl
        {
            Child = Editor,
            LayoutTransform = new ScaleTransform(1, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // The bundle owns the scroller (layers ① and ② deliberately don't scroll themselves).
        _scroller = new ScrollViewer
        {
            Content = _zoomHost,
            Padding = new Thickness(12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        UpdateHorizontalScroll();
        // Page view paginates to a fixed page width, so a zoomed-in page can exceed the viewport →
        // allow horizontal scrolling there. The continuous layout reflows to the viewport, so it must
        // stay disabled (a finite width is what makes the editor reflow instead of growing unbounded).
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichEditor.PageViewProperty) UpdateHorizontalScroll();
        };

        var dock = new DockPanel();
        DockPanel.SetDock(Toolbar, Dock.Top);
        dock.Children.Add(Toolbar);
        dock.Children.Add(_scroller);
        Content = dock;
    }

    private void UpdateHorizontalScroll()
        => _scroller.HorizontalScrollBarVisibility = Editor.PageView
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoomFactorProperty)
            _zoomHost.LayoutTransform = new ScaleTransform(ZoomFactor, ZoomFactor);
    }
}
