using System;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

/// <summary>
/// Optional formatting toolbar for <see cref="RichEditor"/> (roadmap N3.6). Point <see cref="Target"/>
/// at an editor and the toolbar drives it through the editor's public commands, reflects the caret's
/// formatting on its buttons (via <see cref="RichEditor.StatusChanged"/> + <see cref="RichEditor.GetCaretFormat"/>),
/// and follows the editor's feature flags: <see cref="RichEditor.AllowImages"/>/<see cref="RichEditor.AllowTables"/>
/// hide the insert buttons and <see cref="RichEditor.IsReadOnly"/> hides the whole toolbar.
/// Labels/tooltips come from <see cref="RichEditorLocalization"/>. Layout/placement is up to the host —
/// this control is only the strip itself. App-shell concerns (save/open, zoom, printing) are deliberately
/// out of scope.
/// </summary>
public class RichEditorToolbar : UserControl
{
    /// <inheritdoc cref="Target"/>
    public static readonly StyledProperty<RichEditor?> TargetProperty =
        AvaloniaProperty.Register<RichEditorToolbar, RichEditor?>(nameof(Target));

    /// <summary>The editor this toolbar drives. The toolbar is hidden while this is null.</summary>
    public RichEditor? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Host controls shown at the start of the strip, before the formatting buttons (e.g.
    /// app-shell actions like save/open). Add/remove controls and the toolbar rebuilds. They share the
    /// strip's wrapping, so the whole toolbar stays a single row that wraps together when narrow.</summary>
    public AvaloniaList<Control> LeadingItems { get; } = new();

    /// <summary>Host controls shown at the end of the strip, after the formatting buttons (e.g. zoom).</summary>
    public AvaloniaList<Control> TrailingItems { get; } = new();

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#90CAF9"));

    // Controls that reflect caret state (assigned in Build).
    private Button? _boldBtn, _italicBtn, _underlineBtn, _strikeBtn, _painterBtn, _bulletBtn, _numberBtn, _undoBtn, _redoBtn;
    private ComboBox? _fontCombo, _sizeCombo, _headingCombo, _alignCombo, _spacingCombo;
    private Control? _tableBtn, _imageBtn, _dividerBtn;
    // Color-picker faces, synced to the caret's run: either a swatch bar under the built-in glyph,
    // or (with a host icon) a wrapper whose Foreground tints the icon's colour-inheriting layers.
    private Border? _colorSwatch, _highlightSwatch;
    private ContentControl? _colorIconHost, _highlightIconHost;

    private static readonly IBrush NoColorBrush = new SolidColorBrush(Color.Parse("#DDDDDD"));

    // Shows `brush` as the picker's current colour, whichever face style is in use.
    private void ReflectPickerColor(bool highlight, IBrush brush)
    {
        if (highlight)
        {
            if (_highlightSwatch != null) _highlightSwatch.Background = brush;
            if (_highlightIconHost != null) _highlightIconHost.Foreground = brush;
        }
        else
        {
            if (_colorSwatch != null) _colorSwatch.Background = brush;
            if (_colorIconHost != null) _colorIconHost.Foreground = brush;
        }
    }
    private bool _suppress; // guards combo SelectionChanged while syncing toolbar -> caret state

    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    /// <summary>Creates the toolbar. Assign <see cref="Target"/> to connect it to an editor.</summary>
    public RichEditorToolbar()
    {
        // Disabled buttons (undo/redo) dim via opacity instead of the theme's grey fill, which
        // reads as a stray box on this flat transparent strip.
        Styles.Add(new Style(x => x.OfType<Button>().Class(":disabled"))
        {
            Setters = { new Setter(OpacityProperty, 0.35) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class(":disabled").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) },
        });
        // Host item slots: changing them rebuilds the strip so they sit inline with the formatting buttons.
        LeadingItems.CollectionChanged += (_, _) => { Build(); Sync(); };
        TrailingItems.CollectionChanged += (_, _) => { Build(); Sync(); };
        Build();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RichEditorLocalization.LanguageChanged += OnLanguageChanged;
        Sync();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        RichEditorLocalization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Build();
        Sync();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TargetProperty)
        {
            if (change.OldValue is RichEditor old)
            {
                old.StatusChanged -= OnTargetStatusChanged;
                old.PropertyChanged -= OnTargetPropertyChanged;
            }
            if (change.NewValue is RichEditor rt)
            {
                rt.StatusChanged += OnTargetStatusChanged;
                rt.PropertyChanged += OnTargetPropertyChanged;
            }
            Build(); // font list comes from the target, so rebuild
            Sync();
        }
    }

    private void OnTargetStatusChanged(object? sender, EventArgs e) => Sync();

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == RichEditor.FontFamilyChoicesProperty) { Build(); Sync(); }
        else if (e.Property == RichEditor.AllowImagesProperty
              || e.Property == RichEditor.AllowTablesProperty
              || e.Property == RichEditor.IsReadOnlyProperty) ApplyFlags();
    }

    // ---------------- UI construction ----------------

    private void Build()
    {
        var items = new System.Collections.Generic.List<Control>();
        void Add(Control c) => items.Add(c);

        Button Btn(object content, string tip, Action click, RichEditorIcon? icon = null)
        {
            // Icon precedence: host override (RichEditorIcons.Provider) > built-in vector glyph
            // (ToolbarIcons) > styled-text fallback (`content`, for letter-conventional buttons).
            var b = new Button
            {
                Content = (icon is { } k ? RichEditorIcons.TryCreate(k) : null)
                          ?? (icon is { } vk ? ToolbarIcons.Create(vk) : null)
                          ?? content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(9, 5),
                Margin = new Thickness(1, 0),
                MinWidth = 30,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { click(); Sync(); };
            return b;
        }
        ComboBox Combo(string tip, double minWidth = 0)
        {
            var cb = new ComboBox
            {
                Margin = new Thickness(2, 0),
                MinHeight = 30,
                MinWidth = minWidth,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                // The Fluent theme's default combo border is much darker than the rest of the strip.
                BorderBrush = new SolidColorBrush(Color.Parse("#DCDCDC")),
            };
            ToolTip.SetTip(cb, tip);
            return cb;
        }
        Control Div() => new Border
        {
            Width = 1, Height = 22, Margin = new Thickness(6, 4),
            Background = new SolidColorBrush(Color.Parse("#DCDCDC")),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Undo/redo lead the strip (quick-access convention), so they keep a stable spot regardless
        // of how the rest wraps.
        _undoBtn = Btn("↶", Loc("Undo") + " (Ctrl+Z)", () => Target?.Undo(), RichEditorIcon.Undo);
        _redoBtn = Btn("↷", Loc("Redo") + " (Ctrl+Y)", () => Target?.Redo(), RichEditorIcon.Redo);
        Add(_undoBtn); Add(_redoBtn);
        Add(Div());

        // Character toggles
        _boldBtn = Btn(new TextBlock { Text = "B", FontWeight = FontWeight.Bold }, Loc("Bold") + " (Ctrl+B)", () => Target?.ToggleBold(), RichEditorIcon.Bold);
        _italicBtn = Btn(new TextBlock { Text = "I", FontStyle = FontStyle.Italic }, Loc("Italic") + " (Ctrl+I)", () => Target?.ToggleItalic(), RichEditorIcon.Italic);
        _underlineBtn = Btn(new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline }, Loc("Underline") + " (Ctrl+U)", () => Target?.ToggleUnderline(), RichEditorIcon.Underline);
        _strikeBtn = Btn(new TextBlock { Text = "S", TextDecorations = TextDecorations.Strikethrough }, Loc("Strikethrough"), () => Target?.ToggleStrikethrough(), RichEditorIcon.Strikethrough);
        _painterBtn = Btn("🖌", Loc("FormatPainterTip"), () => Target?.StartFormatPainter(), RichEditorIcon.FormatPainter);
        Add(_boldBtn); Add(_italicBtn); Add(_underlineBtn); Add(_strikeBtn); Add(_painterBtn);
        Add(Div());

        // Color pickers
        Add(BuildColorButton(highlight: false));
        Add(BuildColorButton(highlight: true));
        Add(Div());

        // Font family (from the target's host-overridable list) + size
        _fontCombo = Combo(Loc("FontFamily"), 120);
        foreach (var f in Target?.FontFamilyChoices ?? Array.Empty<string>())
            _fontCombo.Items.Add(new ComboBoxItem { Content = f });
        _fontCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || _fontCombo.SelectedItem is not ComboBoxItem it || it.Content is not string fam) return;
            Target?.SetFontFamily(fam);
        };
        Add(_fontCombo);

        _sizeCombo = Combo(Loc("FontSize"), 60);
        foreach (var s in new[] { 10, 12, 14, 18, 24, 32 })
            _sizeCombo.Items.Add(new ComboBoxItem { Content = s.ToString() });
        _sizeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || _sizeCombo.SelectedItem is not ComboBoxItem it) return;
            if (double.TryParse(it.Content?.ToString(), out double size)) Target?.SetFontSize(size);
        };
        Add(_sizeCombo);
        Add(Div());

        // Paragraph style / alignment
        _headingCombo = Combo(Loc("ParagraphStyle"));
        foreach (var key in new[] { "BodyText", "Heading1", "Heading2", "Heading3" })
            _headingCombo.Items.Add(new ComboBoxItem { Content = Loc(key) });
        _headingCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || _headingCombo.SelectedIndex < 0) return;
            Target?.SetHeading(_headingCombo.SelectedIndex);
        };
        Add(_headingCombo);

        _alignCombo = Combo(Loc("Alignment"));
        foreach (var key in new[] { "AlignLeft", "AlignCenter", "AlignRight" })
            _alignCombo.Items.Add(new ComboBoxItem { Content = Loc(key) });
        _alignCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || _alignCombo.SelectedIndex < 0) return;
            Target?.SetTextAlignment(_alignCombo.SelectedIndex switch
            {
                1 => TextAlignment.Center,
                2 => TextAlignment.Right,
                _ => TextAlignment.Left,
            });
        };
        Add(_alignCombo);
        Add(Div());

        // Lists / indent
        _bulletBtn = Btn("•", Loc("BulletList"), () => Target?.ToggleBullet(), RichEditorIcon.BulletList);
        _numberBtn = Btn("1.", Loc("NumberedList"), () => Target?.ToggleNumbering(), RichEditorIcon.NumberedList);
        Add(_bulletBtn); Add(_numberBtn);
        Add(Btn("→|", Loc("IndentIncrease"), () => Target?.Indent(20), RichEditorIcon.IndentIncrease));
        Add(Btn("|←", Loc("IndentDecrease"), () => Target?.Indent(-20), RichEditorIcon.IndentDecrease));
        Add(Div());

        // Line spacing (1.0 = natural, 1.5/2.0 = fixed line heights, matching the demo's presets)
        _spacingCombo = Combo(Loc("LineSpacing"));
        foreach (var v in new[] { "1.0", "1.5", "2.0" })
            _spacingCombo.Items.Add(new ComboBoxItem { Content = string.Format(Loc("LineSpacingFormat"), v) });
        _spacingCombo.SelectedIndex = 0;
        _spacingCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            Target?.SetLineHeight(_spacingCombo.SelectedIndex switch { 1 => 24, 2 => 32, _ => double.NaN });
        };
        Add(_spacingCombo);
        Add(Div());

        // Block inserts (gated by the target's feature flags in ApplyFlags)
        _tableBtn = BuildTableButton();
        _imageBtn = Btn("🖼", Loc("InsertImage"), () => { _ = Target?.InsertImageFromFileAsync(); }, RichEditorIcon.InsertImage);
        _dividerBtn = Btn("―", Loc("InsertDivider"), () => Target?.InsertDivider(), RichEditorIcon.InsertDivider);
        Add(_tableBtn); Add(_imageBtn); Add(_dividerBtn);

        // When the host is narrower than the strip, items wrap to additional rows instead of
        // clipping or scrolling. WrapPanel never mutates the visual tree during layout, so it is
        // immune to the layout-reentrancy crash that a reparenting overflow dropdown hit during an
        // interactive window resize.
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        // Host items detach from the previous build's panel before re-adding (a control has one parent).
        void AddHost(Control c) { (c.Parent as Panel)?.Children.Remove(c); wrap.Children.Add(c); }
        foreach (var c in LeadingItems) AddHost(c);
        if (LeadingItems.Count > 0) wrap.Children.Add(Div());
        foreach (var c in items) wrap.Children.Add(c);
        if (TrailingItems.Count > 0) wrap.Children.Add(Div());
        foreach (var c in TrailingItems) AddHost(c);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F5F6F8")),
            Padding = new Thickness(8, 4),
            Child = wrap,
        };
        ApplyFlags();
    }

    // The 40-swatch palette (greys + hues in a few shades) shared by the text-color/highlight pickers.
    private static readonly string[] Palette =
    {
        "#000000","#444444","#666666","#999999","#BBBBBB","#DDDDDD","#EEEEEE","#FFFFFF",
        "#FF0000","#E67E22","#F1C40F","#2ECC71","#1ABC9C","#3498DB","#9B59B6","#E91E63",
        "#C0392B","#D35400","#F39C12","#27AE60","#16A085","#2980B9","#8E44AD","#AD1457",
        "#7B241C","#935116","#9A7D0A","#196F3D","#0E6251","#1A5276","#5B2C6F","#78281F",
        "#FFCDD2","#FFE0B2","#FFF9C4","#C8E6C9","#B2DFDB","#BBDEFB","#E1BEE7","#F8BBD0",
    };

    // A palette + hex-input flyout button. `highlight` selects whether the chosen colour is applied
    // as text foreground or as a highlight (background) brush.
    private Button BuildColorButton(bool highlight)
    {
        // Button face. With a host-provided icon the icon is the whole face: the current colour is
        // pushed through the host wrapper's Foreground, which icon layers without an explicit
        // Foreground inherit — so a layered icon (mono letter over an accent bar) shows the colour
        // in its own bar, with no separate swatch. Without a provider the face is the built-in
        // glyph over a swatch bar.
        var initial = new SolidColorBrush(highlight ? Color.Parse("#FFF176") : Colors.Black);
        Control face;
        if (RichEditorIcons.TryCreate(highlight ? RichEditorIcon.Highlight : RichEditorIcon.TextColor) is { } icon)
        {
            var host = new ContentControl { Content = icon, Foreground = initial };
            if (highlight) { _highlightIconHost = host; _highlightSwatch = null; }
            else { _colorIconHost = host; _colorSwatch = null; }
            face = host;
        }
        else
        {
            var swatch = new Border
            {
                Height = 6, MinWidth = 24,
                CornerRadius = new CornerRadius(1),
                Background = initial,
                Margin = new Thickness(0, 2, 0, 0),
            };
            if (highlight) { _highlightSwatch = swatch; _highlightIconHost = null; }
            else { _colorSwatch = swatch; _colorIconHost = null; }
            // Highlight uses the built-in marker vector; text color keeps the conventional "A".
            Control glyph = highlight
                ? (ToolbarIcons.Create(RichEditorIcon.Highlight) ?? (Control)new TextBlock { Text = "🖍", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center })
                : new TextBlock { Text = "A", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            var stack = new StackPanel();
            stack.Children.Add(glyph);
            stack.Children.Add(swatch);
            face = stack;
        }

        var btn = new Button
        {
            Content = face,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            // The stacked glyph+swatch face is taller, so it gets less vertical padding; an
            // icon-only face uses the same padding as the other toolbar buttons.
            Padding = face is StackPanel ? new Thickness(9, 3) : new Thickness(9, 5),
            Margin = new Thickness(1, 0),
            MinWidth = 30,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(btn, Loc(highlight ? "Highlight" : "TextColor"));

        void Apply(Color? c)
        {
            if (Target == null) return;
            IBrush? brush = c.HasValue ? new SolidColorBrush(c.Value) : null;
            if (highlight) Target.SetHighlight(brush);
            else Target.SetForeground(brush ?? Brushes.Black);
            // Reflect the chosen colour on the button (cleared highlight -> light grey).
            ReflectPickerColor(highlight, c.HasValue ? new SolidColorBrush(c.Value) : NoColorBrush);
            (btn.Flyout as FlyoutBase)?.Hide();
        }

        var grid = new UniformGrid { Columns = 8 };
        foreach (var hex in Palette)
        {
            var color = Color.Parse(hex);
            var sw = new Button
            {
                Background = new SolidColorBrush(color),
                Width = 22, Height = 22, Margin = new Thickness(1), Padding = new Thickness(0),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            };
            sw.Click += (_, _) => Apply(color);
            grid.Children.Add(sw);
        }

        var panel = new StackPanel { Spacing = 6, Width = 200 };
        panel.Children.Add(grid);

        if (highlight)
        {
            var none = new Button { Content = Loc("NoHighlight"), HorizontalAlignment = HorizontalAlignment.Stretch };
            none.Click += (_, _) => Apply(null);
            panel.Children.Add(none);
        }

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var hexBox = new TextBox { PlaceholderText = "#RRGGBB", Width = 110 };
        var applyBtn = new Button { Content = Loc("Apply") };
        applyBtn.Click += (_, _) => { if (Color.TryParse(hexBox.Text, out var c)) Apply(c); };
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(applyBtn);
        panel.Children.Add(hexRow);

        btn.Flyout = new Flyout { Content = panel };
        return btn;
    }

    // A drag-to-size table picker (hover the grid to choose rows×columns, click to insert).
    private Button BuildTableButton()
    {
        // Grid glyph + dropdown chevron. Host icon wins, else the built-in vector grid, else text.
        object tableFace = "▦ ▾";
        var tableGlyph = RichEditorIcons.TryCreate(RichEditorIcon.InsertTable) ?? ToolbarIcons.Create(RichEditorIcon.InsertTable);
        if (tableGlyph != null)
        {
            var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(tableGlyph);
            face.Children.Add(ToolbarIcons.ChevronDown());
            tableFace = face;
        }
        var btn = new Button
        {
            Content = tableFace,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 5),
            Margin = new Thickness(1, 0),
            MinWidth = 30,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(btn, Loc("InsertTable"));

        const int rows = 8, cols = 10;
        var cells = new Border[rows, cols];
        var grid = new UniformGrid { Columns = cols, Rows = rows };
        var label = new TextBlock { Text = Loc("DragToSelectSize"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };

        void Highlight(int hr, int hc)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    cells[r, c].Background = (r <= hr && c <= hc) ? ActiveBrush : Brushes.White;
            label.Text = $"{hr + 1} × {hc + 1}";
        }

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int rr = r, cc = c;
                var cell = new Border
                {
                    Width = 16, Height = 16, Margin = new Thickness(1),
                    Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                };
                cell.PointerEntered += (_, _) => Highlight(rr, cc);
                cell.PointerPressed += (_, _) =>
                {
                    Target?.InsertTable(rr + 1, cc + 1);
                    (btn.Flyout as FlyoutBase)?.Hide();
                };
                cells[r, c] = cell;
                grid.Children.Add(cell);
            }

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(grid);
        panel.Children.Add(label);
        btn.Flyout = new Flyout { Content = panel };
        return btn;
    }

    // ---------------- Target state -> toolbar ----------------

    // Feature flags: insert buttons follow AllowTables/AllowImages; ReadOnly (or no target) hides the strip.
    private void ApplyFlags()
    {
        IsVisible = Target is { IsReadOnly: false };
        if (Target == null) return;
        if (_tableBtn != null) _tableBtn.IsVisible = Target.AllowTables;
        if (_imageBtn != null) _imageBtn.IsVisible = Target.AllowImages;
        if (_dividerBtn != null) _dividerBtn.IsVisible = Target.AllowTables || Target.AllowImages;
    }

    // Reflects the caret's formatting on the toolbar: active B/I/U/S, list, font, alignment, undo/redo.
    private void Sync()
    {
        var rt = Target;
        if (rt == null) return;
        var f = rt.GetCaretFormat();

        static void SetActive(Button? b, bool active)
        {
            if (b == null) return;
            if (active) b.Background = ActiveBrush;
            else b.Background = Brushes.Transparent;
        }
        SetActive(_boldBtn, f.Bold);
        SetActive(_italicBtn, f.Italic);
        SetActive(_underlineBtn, f.Underline);
        SetActive(_strikeBtn, f.Strike);
        SetActive(_bulletBtn, f.List == ListKind.Bullet);
        SetActive(_numberBtn, f.List == ListKind.Ordered);
        SetActive(_painterBtn, rt.IsFormatPainterActive);
        if (_undoBtn != null) _undoBtn.IsEnabled = rt.CanUndo;
        if (_redoBtn != null) _redoBtn.IsEnabled = rt.CanRedo;

        // Picker colours follow the caret's run: explicit colours show as-is, defaults fall back to
        // black text / "no highlight" grey (same brush Apply() uses for a cleared highlight).
        ReflectPickerColor(highlight: false, f.Foreground ?? Brushes.Black);
        ReflectPickerColor(highlight: true, f.Background ?? NoColorBrush);

        _suppress = true;
        if (_sizeCombo != null) SelectByContent(_sizeCombo, ((int)f.FontSize).ToString());
        if (_fontCombo != null)
        {
            // Runs without an explicit font fall back to the editor's DefaultFontFamily for
            // rendering, so the combo shows that effective default as placeholder text instead of
            // faking a selection (selecting would suggest the run carries the font explicitly).
            if (f.FontFamily != null)
            {
                SelectByContent(_fontCombo, f.FontFamily);
            }
            else
            {
                _fontCombo.SelectedItem = null;
                _fontCombo.PlaceholderText = EffectiveDefaultFamilyName(rt);
            }
        }
        if (_headingCombo != null) _headingCombo.SelectedIndex = Math.Min(f.Heading, 3);
        if (_alignCombo != null) _alignCombo.SelectedIndex = f.Align switch
        {
            TextAlignment.Center => 1,
            TextAlignment.Right => 2,
            _ => 0,
        };
        _suppress = false;
    }

    // Display name of the font a run without explicit FontFamily actually renders with: the
    // editor's DefaultFontFamily, resolving Avalonia's "$Default" sentinel through the FontManager
    // (e.g. "Inter" when the app uses WithInterFont()).
    private static string EffectiveDefaultFamilyName(RichEditor rt)
    {
        var fam = rt.DefaultFontFamily;
        if (fam.Name != FontFamily.DefaultFontFamilyName) return fam.Name;
        return FontManager.Current.DefaultFontFamily.Name;
    }

    private static void SelectByContent(ComboBox cb, string content)
    {
        foreach (var it in cb.Items)
            if (it is ComboBoxItem ci && ci.Content?.ToString() == content) { cb.SelectedItem = ci; return; }
    }
}
