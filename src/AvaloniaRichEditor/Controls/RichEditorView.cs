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

    // Built-in page/zoom chrome (injected into the toolbar's trailing slot). _suppressChrome guards
    // their selection events while SyncChrome pushes editor/view state back into them.
    private ComboBox _zoomCombo = null!, _paperCombo = null!, _orientCombo = null!;
    private CheckBox _outlineCheck = null!;
    private bool _suppressChrome;

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

    // File-action buttons + status-bar widgets (built once in the ctor).
    private Button _exportBtn = null!, _importBtn = null!, _printBtn = null!;
    private Control _fileDivider = null!;
    private TextBlock _statusInfo = null!, _pageInfo = null!, _limitInfo = null!;
    private Border _statusBar = null!;

    private EventHandler? _printRequested;

    /// <summary>Raised when the user clicks the built-in Print button. Printing is platform-specific
    /// (and intentionally not implemented in this cross-platform library), so a host handles this to
    /// drive its own print/preview. The Print button is hidden until at least one handler is attached.</summary>
    public event EventHandler? PrintRequested
    {
        add { _printRequested += value; UpdateFileActionVisibility(); }
        remove { _printRequested -= value; UpdateFileActionVisibility(); }
    }

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

        // View defaults: A4 paper (the editor's own default) as a bare fit-to-width column — no page
        // outline/desk chrome. A host can still flip these on Editor / this view.
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
                SyncChrome();    // reflect the new paper state in the built-in combos
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

        BuildChrome();
        BuildFileActions();

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
            SyncChrome();
        }
        else if (change.Property == FitToWidthProperty)
        {
            UpdateHorizontalScroll();
            if (FitToWidth) ApplyFitWidth();
            SyncChrome();
        }
        else if (change.Property == ShowStatusBarProperty)
        {
            if (_statusBar != null) _statusBar.IsVisible = ShowStatusBar;
        }
        else if (change.Property == ShowFileActionsProperty)
        {
            UpdateFileActionVisibility();
        }
    }

    // ---------------- Built-in page / zoom chrome ----------------

    // Paper size, orientation, page-outline toggle and a zoom (fit + presets) combo, injected into the
    // toolbar's trailing slot so the bundled view is self-contained — a host need only drop in the view.
    private void BuildChrome()
    {
        ComboBox Combo(double width, string tip)
        {
            var cb = new ComboBox
            {
                Width = width, FontSize = 12, MinHeight = 28, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                BorderBrush = new SolidColorBrush(Color.Parse("#DCDCDC")),
            };
            ToolTip.SetTip(cb, tip);
            return cb;
        }

        // Zoom: "Fit" (index 0) + percent presets.
        _zoomCombo = Combo(84, Loc("ZoomTip"));
        _zoomCombo.Margin = new Thickness(0);
        _zoomCombo.Items.Add(new ComboBoxItem { Content = Loc("Fit") });
        foreach (var p in new[] { "50%", "75%", "100%", "125%", "150%", "200%" })
            _zoomCombo.Items.Add(new ComboBoxItem { Content = p });
        _zoomCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressChrome) return;
            if (_zoomCombo.SelectedIndex <= 0) SetCurrentValue(FitToWidthProperty, true);
            else if (_zoomCombo.SelectedItem is ComboBoxItem it
                     && int.TryParse(it.Content?.ToString()?.TrimEnd('%'), out int pct))
                ZoomToPercent(pct / 100.0);
        };

        // Paper size — "Continuous" reflows to width; a concrete size fixes the column.
        _paperCombo = Combo(96, Loc("PaperTip"));
        var sizes = new (RichEditorPageSize size, string label)[]
        {
            (RichEditorPageSize.Continuous, Loc("PaperContinuous")),
            (RichEditorPageSize.A4, "A4"), (RichEditorPageSize.A3, "A3"), (RichEditorPageSize.A5, "A5"),
            (RichEditorPageSize.B4, "B4"), (RichEditorPageSize.B5, "B5"),
            (RichEditorPageSize.Letter, "Letter"), (RichEditorPageSize.Legal, "Legal"), (RichEditorPageSize.Tabloid, "Tabloid"),
        };
        foreach (var (size, label) in sizes)
            _paperCombo.Items.Add(new ComboBoxItem { Content = label, Tag = size });
        _paperCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressChrome) return;
            if (_paperCombo.SelectedItem is ComboBoxItem { Tag: RichEditorPageSize size })
                Editor.PageSize = size;
        };

        // Orientation (meaningful only for a concrete paper).
        _orientCombo = Combo(76, Loc("OrientationTip"));
        _orientCombo.Items.Add(new ComboBoxItem { Content = Loc("OrientPortrait"), Tag = RichEditorPageOrientation.Portrait });
        _orientCombo.Items.Add(new ComboBoxItem { Content = Loc("OrientLandscape"), Tag = RichEditorPageOrientation.Landscape });
        _orientCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressChrome) return;
            if (_orientCombo.SelectedItem is ComboBoxItem { Tag: RichEditorPageOrientation o })
                Editor.PageOrientation = o;
        };

        // Page outline (desk + paper chrome).
        _outlineCheck = new CheckBox { Content = Loc("PageOutline"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _outlineCheck.IsCheckedChanged += (_, _) =>
        {
            if (_suppressChrome) return;
            Editor.ShowPageBoundaries = _outlineCheck.IsChecked == true;
        };

        Toolbar.TrailingItems.Add(_zoomCombo);
        Toolbar.TrailingItems.Add(_paperCombo);
        Toolbar.TrailingItems.Add(_orientCombo);
        Toolbar.TrailingItems.Add(_outlineCheck);

        SyncChrome();
    }

    // Reflects the current editor/view state into the chrome controls (idempotent; guarded so its
    // selection writes don't loop back through the controls' change handlers).
    private void SyncChrome()
    {
        if (_zoomCombo is null) return; // before BuildChrome
        _suppressChrome = true;
        try
        {
            foreach (var it in _paperCombo.Items)
                if (it is ComboBoxItem { Tag: RichEditorPageSize s } ci && s == Editor.PageSize) { _paperCombo.SelectedItem = ci; break; }
            bool paged = Editor.IsPaged;
            _orientCombo.IsEnabled = paged;
            _outlineCheck.IsEnabled = paged;
            foreach (var it in _orientCombo.Items)
                if (it is ComboBoxItem { Tag: RichEditorPageOrientation o } ci && o == Editor.PageOrientation) { _orientCombo.SelectedItem = ci; break; }
            _outlineCheck.IsChecked = Editor.ShowPageBoundaries;

            if (FitToWidth) _zoomCombo.SelectedIndex = 0;
            else
            {
                int pct = (int)Math.Round(ZoomFactor * 100);
                ComboBoxItem? match = null;
                for (int i = 1; i < _zoomCombo.Items.Count; i++)
                    if (_zoomCombo.Items[i] is ComboBoxItem ci && ci.Content?.ToString() == pct + "%") { match = ci; break; }
                _zoomCombo.SelectedItem = match; // null = an off-grid zoom (fit %, Ctrl+wheel step)
            }
        }
        finally { _suppressChrome = false; }
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

    // ---------------- Built-in file actions (toolbar tail) ----------------

    // Export / Import / Print, placed at the very end of the toolbar. Export/Import drive the platform
    // file picker; Print is delegated to the host via PrintRequested (so no platform print dependency
    // leaks into this cross-platform library).
    private void BuildFileActions()
    {
        _fileDivider = new Border
        {
            Width = 1, Height = 22, Margin = new Thickness(6, 4),
            Background = new SolidColorBrush(Color.Parse("#DCDCDC")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _exportBtn = FileButton(RichEditorIcon.Export, "Export", () => _ = ExportAsync());
        _importBtn = FileButton(RichEditorIcon.Import, "Import", () => _ = ImportAsync());
        _printBtn = FileButton(RichEditorIcon.Print, "Print", () => _printRequested?.Invoke(this, EventArgs.Empty));

        Toolbar.TrailingItems.Add(_fileDivider);
        Toolbar.TrailingItems.Add(_exportBtn);
        Toolbar.TrailingItems.Add(_importBtn);
        Toolbar.TrailingItems.Add(_printBtn);
        UpdateFileActionVisibility();
    }

    private Button FileButton(RichEditorIcon icon, string tipKey, Action onClick)
    {
        var b = new Button
        {
            Content = RichEditorIcons.TryCreate(icon) ?? ToolbarIcons.Create(icon),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 5),
            Margin = new Thickness(1, 0),
            MinWidth = 30,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, Loc(tipKey));
        b.Click += (_, _) => onClick();
        return b;
    }

    private void UpdateFileActionVisibility()
    {
        if (_exportBtn is null) return; // before BuildFileActions
        bool show = ShowFileActions;
        _fileDivider.IsVisible = show;
        _exportBtn.IsVisible = show;
        _importBtn.IsVisible = show;
        _printBtn.IsVisible = show && _printRequested != null; // hidden until a host handles printing
    }

    private async Task ExportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null || Editor.Document == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = Loc("Export"),
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("JSON document") { Patterns = new[] { "*.json" } },
                new Avalonia.Platform.Storage.FilePickerFileType("Flow package") { Patterns = new[] { "*.flow" } },
                new Avalonia.Platform.Storage.FilePickerFileType("HTML document") { Patterns = new[] { "*.html", "*.htm" } },
                new Avalonia.Platform.Storage.FilePickerFileType("RTF document") { Patterns = new[] { "*.rtf" } },
            }
        });
        if (file == null) return;

        // Format follows the chosen extension: .flow = ZIP package (raw image bytes), .html/.htm = HTML,
        // .rtf = Rich Text Format, anything else = plain JSON.
        var name = file.Name;
        if (name.EndsWith(".flow", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = await file.OpenWriteAsync();
            await Editor.SavePackageAsync(stream);
        }
        else if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            string html = Editor.ToHtml();
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(html);
        }
        else if (name.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
        {
            string rtf = Editor.ToRtf();
            using var stream = await file.OpenWriteAsync();
            // RTF is ASCII (non-ASCII text is \u-escaped), so Latin-1 keeps the bytes exact.
            using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.Latin1);
            await writer.WriteAsync(rtf);
        }
        else
        {
            string json = await Editor.ToJsonAsync(); // serialize off the UI thread
            using var stream = await file.OpenWriteAsync();
            using var writer = new System.IO.StreamWriter(stream);
            await writer.WriteAsync(json);
        }
    }

    private async Task ImportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = Loc("Import"),
            AllowMultiple = false,
        });
        if (files == null || files.Count == 0) return;
        try
        {
            using var stream = await files[0].OpenReadAsync();
            using var ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            // Sniff the content: ZIP magic ("PK") = .flow package, "{\rtf" = RTF, anything else = JSON.
            if (ms.Length >= 2 && ms.GetBuffer()[0] == (byte)'P' && ms.GetBuffer()[1] == (byte)'K')
            {
                await Editor.LoadPackageAsync(ms);
            }
            else
            {
                // RTF bytes are ASCII/Latin-1 (the parser decodes \'hh with the document code page itself),
                // so decode as Latin-1 to keep bytes intact; fall back to UTF-8 JSON otherwise.
                string latin1 = System.Text.Encoding.Latin1.GetString(ms.ToArray());
                string utf8 = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                if (AvaloniaRichEditor.Formatters.RtfDocumentFormatter.LooksLikeRtf(latin1))
                    Editor.LoadRtf(latin1);
                else if (utf8.TrimStart().StartsWith("<", StringComparison.Ordinal))
                    Editor.LoadHtml(utf8); // an HTML/.htm export (JSON starts with '{', RTF was handled above)
                else
                    await Editor.LoadJsonAsync(utf8);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
        }
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
