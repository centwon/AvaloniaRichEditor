using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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

    /// <inheritdoc cref="FitToWidth"/>
    public static readonly StyledProperty<bool> FitToWidthProperty =
        AvaloniaProperty.Register<RichEditorView, bool>(nameof(FitToWidth), true);

    /// <summary>When <see langword="true"/> (the default), the view auto-scales the document so the
    /// page (or fixed content column) exactly fills the viewport width, recomputing on resize and on
    /// paper/orientation/outline changes; no horizontal scrollbar appears. Setting <see cref="ZoomFactor"/>
    /// explicitly (e.g. a zoom control) turns this off. The continuous layout always fits at 1.0.</summary>
    public bool FitToWidth
    {
        get => GetValue(FitToWidthProperty);
        set => SetValue(FitToWidthProperty, value);
    }

    // Guards the self-driven ZoomFactor write in ApplyFitWidth so it isn't mistaken for an explicit
    // (fit-cancelling) zoom from a host.
    private bool _settingZoomInternally;

    private static string Loc(string key) => AvaloniaRichEditor.RichEditorLocalization.GetString(key);

    /// <inheritdoc cref="ShowStatusBar"/>
    public static readonly StyledProperty<bool> ShowStatusBarProperty =
        AvaloniaProperty.Register<RichEditorView, bool>(nameof(ShowStatusBar), true);

    /// <summary>Whether the built-in bottom status bar (character/word counts, caret line/column,
    /// page count and the soft image-limit warning) is shown. Default <see langword="true"/>.</summary>
    public bool ShowStatusBar
    {
        get => GetValue(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }

    /// <inheritdoc cref="ShowFileActions"/>
    public static readonly StyledProperty<bool> ShowFileActionsProperty =
        AvaloniaProperty.Register<RichEditorView, bool>(nameof(ShowFileActions), true);

    /// <summary>Whether the built-in Export/Import (and Print, when <see cref="PrintRequested"/> is
    /// handled) buttons are shown at the end of the toolbar. Export/Import use the platform file picker
    /// for JSON/.flow/HTML. Default <see langword="true"/>.</summary>
    public bool ShowFileActions
    {
        get => GetValue(ShowFileActionsProperty);
        set => SetValue(ShowFileActionsProperty, value);
    }

    // Status-bar widgets (built once in the ctor). The page/zoom chrome and Export/Import/Print file
    // actions now live natively in the toolbar (RichEditorToolbar.PageFile); this view just wires them.
    private TextBlock _statusInfo = null!, _pageInfo = null!, _limitInfo = null!;
    private Border _statusBar = null!;

    private EventHandler? _printRequested;

    /// <summary>Raised when the user clicks the toolbar's built-in Print button. Printing is platform-specific
    /// (and intentionally not implemented in this cross-platform library), so a host handles this to
    /// drive its own print/preview. The Print button is hidden until at least one handler is attached.</summary>
    public event EventHandler? PrintRequested
    {
        add
        {
            bool had = _printRequested != null;
            _printRequested += value;
            if (!had && _printRequested != null) Toolbar.PrintRequested += ForwardPrint; // reveal the toolbar's Print button
        }
        remove
        {
            _printRequested -= value;
            if (_printRequested == null) Toolbar.PrintRequested -= ForwardPrint;
        }
    }

    private void ForwardPrint(object? sender, EventArgs e) => _printRequested?.Invoke(this, e);

    // The editor lives inside this; its LayoutTransform carries the zoom. LayoutTransform (not
    // RenderTransform) so the scroller's extent and the editor's reflow width both follow the zoom.
    // Top-aligned so a short document anchors at the top of the scroller instead of centering
    // vertically (LayoutTransformControl centers its child in any slack it's given).
    private readonly LayoutTransformControl _zoomHost;
    private readonly ScrollViewer _scroller;

    /// <summary>Creates the bundled toolbar + scrolling editor view.</summary>
    public RichEditorView()
    {
        // The View is the full host, so its toolbar carries everything: page/zoom + file actions.
        Toolbar = new RichEditorToolbar { Target = Editor, ToolbarLevel = ToolbarLevel.Maximum };
        // Zoom is view-level here (a LayoutTransform around the editor), so the toolbar's zoom combo is
        // driven through these hooks rather than the editor directly.
        Toolbar.ZoomGetter = () => ZoomFactor;
        Toolbar.IsFitWidthGetter = () => FitToWidth;
        Toolbar.ZoomSetter = ZoomToPercent;
        Toolbar.FitWidthAction = () => SetCurrentValue(FitToWidthProperty, true);

        // View defaults: the editor's own default (Continuous, reflow to width) with no page outline/desk
        // chrome. A host can still switch to a concrete paper size on Editor / via the toolbar.
        Editor.ShowPageBoundaries = false;

        // Margin (not ScrollViewer padding) gives the editor its breathing room: the content sits
        // inside the LayoutTransformControl's bounds, so it's neither clipped at the edge nor bled
        // over the padding. The right gutter = 12 + the idle scrollbar's ~6px, so content/resize
        // handles clear the resting scrollbar (its hover-expanded state just overlays the gutter).
        Editor.Margin = new Thickness(12, 12, 18, 12);

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
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        UpdateHorizontalScroll();
        // Make the editor at least as tall as the viewport, so the empty area below short content is part
        // of the editing surface (click-to-end) and the "draw table" rubber-band can extend into it without
        // being clipped to the content height. MinHeight is in editor (pre-zoom) px, so divide by the zoom.
        _scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty) UpdateEditorFillHeight();
        };
        // A concrete paper size fixes the column width, so a narrow viewport (or a zoomed page) can
        // exceed it → allow horizontal scrolling there. The continuous (Free) layout reflows to the
        // viewport, so it must stay disabled (a finite width is what makes the editor reflow instead
        // of growing unbounded).
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichEditor.PageSizeProperty
                || e.Property == RichEditor.ShowPageBoundariesProperty
                || e.Property == RichEditor.PageOrientationProperty)
            {
                UpdateHorizontalScroll();
                ApplyFitWidth(); // paper/orientation/outline change the fit target
                // The toolbar syncs its own paper/orientation combos off the editor's property change.
            }
        };
        // Re-fit whenever the viewport width changes.
        SizeChanged += (_, _) => ApplyFitWidth();

        BuildStatusBar();

        var dock = new DockPanel();
        DockPanel.SetDock(Toolbar, Dock.Top);
        dock.Children.Add(Toolbar);
        DockPanel.SetDock(_statusBar, Dock.Bottom);
        dock.Children.Add(_statusBar);
        dock.Children.Add(_scroller); // fills the remaining space between toolbar and status bar
        Content = dock;

        Toolbar.ShowFileActions = ShowFileActions;

        // Keep the status bar live. Counts follow any caret move; the page count and image-limit
        // warning ride the content-only signal (they need O(blocks) walks).
        Editor.SelectionChanged += (_, _) => UpdateCounts();
        Editor.TextChanged += (_, _) => UpdateStatus();
        Editor.RecommendedImageLimitExceeded += (_, _) => UpdateImageWarning();
        UpdateStatus();
    }

    // A horizontal scrollbar only makes sense for a fixed-width paged column that overflows the
    // viewport. In fit-to-width the column is scaled to the viewport, so it never overflows — and the
    // continuous layout reflows — so both disable it.
    private void UpdateHorizontalScroll()
        => _scroller.HorizontalScrollBarVisibility = (Editor.IsPaged && !FitToWidth)
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;

    // Floor the editor's height at the viewport (in pre-zoom px) so short documents still fill the visible
    // area. Capped at the viewport so this can never push content past the viewport and spawn a scrollbar
    // (which would shrink the viewport and loop): when content is taller it already exceeds this floor.
    private void UpdateEditorFillHeight()
    {
        double vh = _scroller.Viewport.Height;
        double zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;
        if (vh > 0) Editor.MinHeight = vh / zoom;
    }

    // Scales the document so the page (chrome) or fixed content column (no chrome) fills the viewport
    // width. Mirrors the print/desk geometry: a chromed page adds a desk gap each side; a bare column
    // is the paper minus its 2×48 margins. Continuous reflows on its own, so fit is just 1.0.
    private void ApplyFitWidth()
    {
        if (!FitToWidth) return;
        double vw = Bounds.Width;
        if (vw < 50) return; // not laid out yet
        const double pad = 40;
        // Reference the actual desk gap so the fit target leaves the same thin grey margin each side as
        // the top/inter-page gap (was hardcoded 24, leaving a wide grey band even after PageGap shrank).
        const double deskGap = RichEditor.PageGap;
        double target;
        if (Editor.PageSize == RichEditorPageSize.Continuous)
            target = 0;
        else
        {
            double paperW = Editor.GetPaperPixelSize().Width; // accounts for size + orientation
            target = Editor.ShowPageBoundaries ? paperW + 2 * deskGap : paperW - 96;
        }
        double z = target > 0 ? Math.Clamp((vw - pad) / target, 0.2, 5.0) : 1.0;
        _settingZoomInternally = true;
        try { SetCurrentValue(ZoomFactorProperty, z); }
        finally { _settingZoomInternally = false; }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ZoomFactorProperty)
        {
            _zoomHost.LayoutTransform = new ScaleTransform(ZoomFactor, ZoomFactor);
            // An explicit zoom (not our own fit write) cancels fit-to-width.
            if (!_settingZoomInternally) SetCurrentValue(FitToWidthProperty, false);
            Toolbar.RefreshPageControls(); // zoom is view-level, so push it onto the toolbar's zoom combo
        }
        else if (change.Property == FitToWidthProperty)
        {
            UpdateHorizontalScroll();
            if (FitToWidth) ApplyFitWidth();
            Toolbar.RefreshPageControls();
        }
        else if (change.Property == ShowStatusBarProperty)
        {
            if (_statusBar != null) _statusBar.IsVisible = ShowStatusBar;
        }
        else if (change.Property == ShowFileActionsProperty)
        {
            Toolbar.ShowFileActions = ShowFileActions;
        }
    }

    private void ZoomToPercent(double factor)
    {
        SetCurrentValue(FitToWidthProperty, false);
        SetCurrentValue(ZoomFactorProperty, Math.Clamp(factor, 0.2, 5.0));
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomToPercent(ZoomFactor + (e.Delta.Y > 0 ? 0.1 : -0.1));
            e.Handled = true;
            return;
        }
        base.OnPointerWheelChanged(e);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key is Key.D0 or Key.NumPad0) { SetCurrentValue(FitToWidthProperty, true); e.Handled = true; return; }
            if (e.Key is Key.OemPlus or Key.Add) { ZoomToPercent(ZoomFactor + 0.1); e.Handled = true; return; }
            if (e.Key is Key.OemMinus or Key.Subtract) { ZoomToPercent(ZoomFactor - 0.1); e.Handled = true; return; }
        }
        base.OnKeyDown(e);
    }

    // ---------------- Built-in status bar ----------------

    private void BuildStatusBar()
    {
        TextBlock Tb(string color) => new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse(color)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _statusInfo = Tb("#444444");
        _pageInfo = Tb("#444444");
        _pageInfo.Margin = new Thickness(0, 0, 12, 0);
        _limitInfo = Tb("#CC6600");
        _limitInfo.Margin = new Thickness(0, 0, 12, 0);

        var panel = new DockPanel();
        DockPanel.SetDock(_limitInfo, Dock.Right);
        DockPanel.SetDock(_pageInfo, Dock.Right);
        panel.Children.Add(_limitInfo);
        panel.Children.Add(_pageInfo);
        panel.Children.Add(_statusInfo);

        _statusBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EEEEEE")),
            BorderBrush = new SolidColorBrush(Color.Parse("#CCCCCC")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 3),
            Child = panel,
            IsVisible = ShowStatusBar,
        };
    }

    private void UpdateStatus()
    {
        UpdateCounts();
        _pageInfo.Text = string.Format(Loc("PageCountFormat"), Editor.GetPrintPageCount());
        if (!string.IsNullOrEmpty(_limitInfo.Text) && Editor.GetImageCount() <= Editor.MaxRecommendedImages)
            _limitInfo.Text = ""; // cleared once back within bounds
    }

    private void UpdateCounts()
    {
        if (_statusInfo is null) return;
        var (chars, words, line, col) = Editor.GetStatus();
        _statusInfo.Text = string.Format(Loc("StatusFormat"), chars, words, line, col);
    }

    private void UpdateImageWarning()
        => _limitInfo.Text = string.Format(Loc("ImageLimitWarning"), Editor.GetImageCount(), Editor.MaxRecommendedImages);
}
