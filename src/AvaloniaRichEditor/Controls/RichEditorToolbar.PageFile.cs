using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaRichEditor.Formatters;

namespace AvaloniaRichEditor.Controls;

/// <summary>How much of the toolbar is shown. A coarse density knob layered over the individual controls;
/// capability (read-only, <see cref="RichEditor.AllowTables"/>/<see cref="RichEditor.AllowImages"/>) still
/// vetoes what a level would otherwise show.</summary>
public enum ToolbarLevel
{
    /// <summary>Derive from the target: editable → <see cref="Normal"/>. (Read-only always shows the view
    /// toolbar regardless of level.)</summary>
    Auto,
    /// <summary>Undo/redo, B/I/U/S, font size only.</summary>
    Minimal,
    /// <summary>Full text formatting (font, colour, heading, align, lists, indent, spacing) + inserts.</summary>
    Normal,
    /// <summary>Everything, plus the page/zoom controls and file actions (Export/Import/Print).</summary>
    Maximum,
}

// Built-in page/zoom controls (zoom · paper · orientation) and file actions (Export / Import / Print), so a
// standalone toolbar carries them too (ported from WinUIRichEditor). Paper/orientation drive the editor
// directly; zoom is view-level in this port (RichEditorView owns a LayoutTransform), so the zoom combo is
// driven through the host-wired hooks below (null on a bare toolbar → the combo is inert). Both sections
// default on and are appended by Build() at ToolbarLevel.Maximum (and in the read-only view toolbar).
public partial class RichEditorToolbar
{
    // ---- toolbar density level --------------------------------------------
    private ToolbarLevel _level = ToolbarLevel.Auto;

    /// <summary>Toolbar density. <see cref="ToolbarLevel.Auto"/> (default) resolves to
    /// <see cref="ToolbarLevel.Normal"/> for an editable target; a read-only target always shows the view
    /// toolbar (page/zoom + Export/Print) regardless of this. Capability (Allow*) still vetoes buttons.</summary>
    public ToolbarLevel ToolbarLevel
    {
        get => _level;
        set { if (_level == value) return; _level = value; Build(); Sync(); }
    }

    // The concrete level to build (Auto → Normal). Read-only is handled separately in Build().
    private ToolbarLevel EffectiveLevel() => _level == ToolbarLevel.Auto ? ToolbarLevel.Normal : _level;

    // ---- zoom hooks (host-wired; zoom is view-level in this port) ----------
    /// <summary>Returns the current zoom factor (1.0 = 100%). Wired by <see cref="RichEditorView"/>.</summary>
    public Func<double>? ZoomGetter { get; set; }
    /// <summary>Sets an explicit zoom factor (cancels fit-to-width). Wired by <see cref="RichEditorView"/>.</summary>
    public Action<double>? ZoomSetter { get; set; }
    /// <summary>Switches to fit-to-width. Wired by <see cref="RichEditorView"/>.</summary>
    public Action? FitWidthAction { get; set; }
    /// <summary>Returns whether fit-to-width is currently active. Wired by <see cref="RichEditorView"/>.</summary>
    public Func<bool>? IsFitWidthGetter { get; set; }

    // ---- page / zoom ------------------------------------------------------
    private ComboBox? _zoomCombo, _paperCombo, _orientCombo;

    private static readonly (RichEditorPageSize size, string label)[] PaperSizes =
    {
        (RichEditorPageSize.Continuous, "PaperContinuous"), (RichEditorPageSize.A4, "A4"),
        (RichEditorPageSize.A3, "A3"), (RichEditorPageSize.A5, "A5"), (RichEditorPageSize.B4, "B4"),
        (RichEditorPageSize.B5, "B5"), (RichEditorPageSize.Letter, "Letter"),
        (RichEditorPageSize.Legal, "Legal"), (RichEditorPageSize.Tabloid, "Tabloid"),
    };

    private bool _showPageControls = true;
    /// <summary>Whether the built-in zoom / paper-size / orientation controls are shown (at
    /// <see cref="ToolbarLevel.Maximum"/> or in the read-only view toolbar). Default true.</summary>
    public bool ShowPageControls
    {
        get => _showPageControls;
        set { if (_showPageControls == value) return; _showPageControls = value; Build(); Sync(); }
    }

    private ComboBox PageCombo(double width, string tip)
    {
        var cb = new ComboBox
        {
            Width = width, FontSize = 12, MinHeight = 28, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#DCDCDC")),
        };
        ToolTip.SetTip(cb, tip);
        return cb;
    }

    private void BuildPageControls(System.Collections.Generic.List<Control> items)
    {
        // Zoom: "Fit" (index 0) + percent presets. Driven through the host zoom hooks (view-level zoom).
        _zoomCombo = PageCombo(88, Loc("ZoomTip"));
        _zoomCombo.Items.Add(new ComboBoxItem { Content = Loc("Fit") });
        foreach (var p in new[] { "50%", "75%", "100%", "125%", "150%", "200%" })
            _zoomCombo.Items.Add(new ComboBoxItem { Content = p });
        _zoomCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            if (_zoomCombo.SelectedIndex <= 0) FitWidthAction?.Invoke();
            else if (_zoomCombo.SelectedItem is ComboBoxItem it
                     && int.TryParse(it.Content?.ToString()?.TrimEnd('%'), out int pct))
                ZoomSetter?.Invoke(pct / 100.0);
        };
        items.Add(_zoomCombo);

        // Paper size — "Continuous" reflows to width; a concrete size fixes the column and shows the outline.
        _paperCombo = PageCombo(100, Loc("PaperTip"));
        foreach (var (size, label) in PaperSizes)
            _paperCombo.Items.Add(new ComboBoxItem { Content = label == "PaperContinuous" ? Loc(label) : label, Tag = size });
        _paperCombo.SelectionChanged += (_, _) => OnPaperChanged();
        items.Add(_paperCombo);

        // Orientation (meaningful only for a concrete paper).
        _orientCombo = PageCombo(84, Loc("OrientationTip"));
        _orientCombo.Items.Add(new ComboBoxItem { Content = Loc("OrientPortrait"), Tag = RichEditorPageOrientation.Portrait });
        _orientCombo.Items.Add(new ComboBoxItem { Content = Loc("OrientLandscape"), Tag = RichEditorPageOrientation.Landscape });
        _orientCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || Target == null || _orientCombo.SelectedItem is not ComboBoxItem { Tag: RichEditorPageOrientation o }) return;
            Target.PageOrientation = o;
        };
        items.Add(_orientCombo);
    }

    /// <summary>Reflects the host's current zoom / fit-width and the editor's paper state onto the built-in
    /// page controls. Call after changing view-level zoom (which the toolbar can't observe directly).</summary>
    public void RefreshPageControls()
    {
        _suppress = true;
        try { SyncPage(); }
        finally { _suppress = false; }
    }

    private void OnPaperChanged()
    {
        if (_suppress || Target == null || _paperCombo?.SelectedItem is not ComboBoxItem { Tag: RichEditorPageSize size }) return;
        Target.PageSize = size;
        // A concrete paper size shows the page outline (page view); Continuous reflows with no chrome.
        Target.ShowPageBoundaries = size != RichEditorPageSize.Continuous;
        SyncPage();
    }

    // Reflect the editor's page/zoom state onto the built-in controls (called from Sync()).
    private void SyncPage()
    {
        if (Target == null) return;
        bool paged = Target.PageSize != RichEditorPageSize.Continuous;
        if (_paperCombo != null)
            foreach (var it in _paperCombo.Items)
                if (it is ComboBoxItem { Tag: RichEditorPageSize s } ci && s == Target.PageSize) { _paperCombo.SelectedItem = ci; break; }
        if (_orientCombo != null)
        {
            foreach (var it in _orientCombo.Items)
                if (it is ComboBoxItem { Tag: RichEditorPageOrientation o } ci && o == Target.PageOrientation) { _orientCombo.SelectedItem = ci; break; }
            _orientCombo.IsEnabled = paged; // orientation is meaningless in Continuous
        }
        if (_zoomCombo != null)
        {
            if (IsFitWidthGetter?.Invoke() == true) _zoomCombo.SelectedIndex = 0;
            else
            {
                int pct = (int)Math.Round((ZoomGetter?.Invoke() ?? 1.0) * 100);
                ComboBoxItem? match = null;
                for (int i = 1; i < _zoomCombo.Items.Count; i++)
                    if (_zoomCombo.Items[i] is ComboBoxItem ci && ci.Content?.ToString() == pct + "%") { match = ci; break; }
                _zoomCombo.SelectedItem = match; // null = an off-grid zoom (fit %, Ctrl+wheel step)
            }
        }
    }

    // ---- file actions -----------------------------------------------------
    private Button? _exportBtn, _importBtn, _printBtn;

    private bool _showFileActions = true;
    /// <summary>Whether the built-in Export / Import (and Print, once <see cref="PrintRequested"/> is
    /// handled) buttons are shown (at <see cref="ToolbarLevel.Maximum"/> or in the read-only view toolbar,
    /// where Import is dropped). Export/Import use the platform file picker. Default true.</summary>
    public bool ShowFileActions
    {
        get => _showFileActions;
        set { if (_showFileActions == value) return; _showFileActions = value; Build(); Sync(); }
    }

    private EventHandler? _printRequested;
    /// <summary>Raised when the user clicks the built-in Print button. Printing is platform-specific (and
    /// intentionally not implemented in this cross-platform library), so a host handles this to drive its
    /// own print/preview. The Print button stays hidden until a handler is attached.</summary>
    public event EventHandler? PrintRequested
    {
        add { _printRequested += value; SyncFileActions(); }
        remove { _printRequested -= value; SyncFileActions(); }
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

    private void BuildFileActions(System.Collections.Generic.List<Control> items)
    {
        _exportBtn = FileButton(RichEditorIcon.Export, "Export", () => _ = ExportAsync());
        _importBtn = FileButton(RichEditorIcon.Import, "Import", () => _ = ImportAsync());
        _printBtn = FileButton(RichEditorIcon.Print, "Print", () => _printRequested?.Invoke(this, EventArgs.Empty));
        items.Add(_exportBtn);
        items.Add(_importBtn);
        items.Add(_printBtn);
    }

    // Import edits, so it's hidden in the read-only view toolbar; Print stays hidden until a host handles
    // PrintRequested. (Called from Sync().)
    private void SyncFileActions()
    {
        bool ro = Target?.IsReadOnly == true;
        if (_importBtn != null) _importBtn.IsVisible = !ro;
        if (_printBtn != null) _printBtn.IsVisible = _printRequested != null;
    }

    private async Task ExportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null || Target?.Document == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc("Export"),
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON document") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("Flow package") { Patterns = new[] { "*.flow" } },
                new FilePickerFileType("HTML document") { Patterns = new[] { "*.html", "*.htm" } },
                new FilePickerFileType("RTF document") { Patterns = new[] { "*.rtf" } },
            }
        });
        if (file == null) return;

        // Format follows the chosen extension: .flow = ZIP package, .html/.htm = HTML, .rtf = RTF, else JSON.
        var name = file.Name;
        if (name.EndsWith(".flow", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = await file.OpenWriteAsync();
            await Target.SavePackageAsync(stream);
        }
        else if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            string html = Target.ToHtml();
            using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(html);
        }
        else if (name.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
        {
            string rtf = Target.ToRtf();
            using var stream = await file.OpenWriteAsync();
            // RTF is ASCII (non-ASCII text is \u-escaped), so Latin-1 keeps the bytes exact.
            using var writer = new StreamWriter(stream, System.Text.Encoding.Latin1);
            await writer.WriteAsync(rtf);
        }
        else
        {
            string json = await Target.ToJsonAsync(); // serialize off the UI thread
            using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
        }
    }

    private async Task ImportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null || Target == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc("Import"),
            AllowMultiple = false,
        });
        if (files == null || files.Count == 0) return;
        try
        {
            using var stream = await files[0].OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            // Sniff the content: ZIP magic ("PK") = .flow package, "{\rtf" = RTF, "<" = HTML, else JSON.
            if (ms.Length >= 2 && ms.GetBuffer()[0] == (byte)'P' && ms.GetBuffer()[1] == (byte)'K')
            {
                await Target.LoadPackageAsync(ms);
            }
            else
            {
                string latin1 = System.Text.Encoding.Latin1.GetString(ms.ToArray());
                string utf8 = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                if (RtfDocumentFormatter.LooksLikeRtf(latin1)) Target.LoadRtf(latin1);
                else if (utf8.TrimStart().StartsWith("<", StringComparison.Ordinal)) Target.LoadHtml(utf8);
                else await Target.LoadJsonAsync(utf8);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Import failed: {ex.Message}");
        }
    }
}
