using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AvaloniaRichEditor.Controls;

// Built-in find/replace bar for RichEditorView, from the WinUI port (2026-09-19): the library had the search engine
// (FindNext/FindPrev/ReplaceNext/ReplaceAll, highlight-all, the "n/m" position) but no way to reach it. Opened by the
// editor's Ctrl+F (find) / Ctrl+H (find + replace) through RichEditor.FindRequested; Enter = next, Shift+Enter =
// previous, Esc = close. Built lazily in code so a host that never searches pays nothing. A host with its own find
// UI subscribes to RichEditor.FindRequested itself and sets ShowBuiltInFindBar = false.
public partial class RichEditorView
{
    /// <inheritdoc cref="ShowBuiltInFindBar"/>
    public static readonly StyledProperty<bool> ShowBuiltInFindBarProperty =
        AvaloniaProperty.Register<RichEditorView, bool>(nameof(ShowBuiltInFindBar), true);

    /// <summary>When false, Ctrl+F/Ctrl+H do not open the built-in bar (for hosts with their own find UI subscribed
    /// to <see cref="RichEditor.FindRequested"/>). Default true.</summary>
    public bool ShowBuiltInFindBar
    {
        get => GetValue(ShowBuiltInFindBarProperty);
        set => SetValue(ShowBuiltInFindBarProperty, value);
    }

    private readonly Border _findBarHost = new() { IsVisible = false, BorderThickness = new Thickness(0, 0, 0, 1) };
    private StackPanel? _findBar;
    private TextBox? _findBox, _replaceBox;
    private ToggleButton? _matchCase, _expandReplace;
    private StackPanel? _replaceRow;
    private TextBlock? _matchLabel;

    private static string L(string key) => RichEditorLocalization.GetString(key);

    /// <summary>Opens the find bar (with the replace row when <paramref name="withReplace"/> and the editor is
    /// editable) and focuses the query box, pre-filled with the last query.</summary>
    public void ShowFindBar(bool withReplace)
    {
        if (!ShowBuiltInFindBar || !Editor.AllowFindReplace) return;
        BuildFindBar();
        SetReplaceVisible(withReplace && !Editor.IsReadOnly);
        _expandReplace!.IsVisible = !Editor.IsReadOnly; // a viewer cannot replace: no dead chevron
        _findBarHost.IsVisible = true;
        if (string.IsNullOrEmpty(_findBox!.Text) && Editor.LastFindQuery is { } last) _findBox.Text = last;
        Editor.SetFindHighlight(_findBox.Text, _matchCase?.IsChecked == true);
        UpdateMatchLabel();
        // Posted: the bar became visible in this same tick (usually inside the editor's Ctrl+F handler) and is not
        // laid out yet, so an immediate Focus() can be lost and typing keeps going into the document.
        var box = _findBox;
        Dispatcher.UIThread.Post(() => { box.SelectAll(); box.Focus(); });
    }

    /// <summary>Hides the find bar and returns focus to the editor.</summary>
    public void HideFindBar()
    {
        _findBarHost.IsVisible = false;
        Editor.ClearFindHighlight(); // the highlight-all overlay lives only while the bar is open
        Editor.Focus();
    }

    // The one place that drives the replace row and the chevron together, so Ctrl+H and the chevron never disagree.
    private void SetReplaceVisible(bool show)
    {
        _replaceRow!.IsVisible = show;
        _expandReplace!.IsChecked = show;
        _expandReplace.Content = show ? "▾" : "▸";
    }

    // The "n/m" counter ("m" alone when the selection is not on a match).
    private void UpdateMatchLabel()
    {
        if (_matchLabel == null) return;
        var (cur, total) = Editor.GetFindMatchPosition();
        _matchLabel.Text = total == 0 ? "0" : cur > 0 ? $"{cur}/{total}" : total.ToString();
    }

    private void BuildFindBar()
    {
        if (_findBar != null) return;

        _findBox = new TextBox { MinWidth = 200, PlaceholderText = L("Find"), FontSize = 12 };
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DoFind(backwards: e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
            else if (e.Key == Key.Escape) { HideFindBar(); e.Handled = true; }
        };
        // Highlight-all as the user types (browser find); the counter follows.
        _findBox.TextChanged += (_, _) =>
        {
            Editor.SetFindHighlight(_findBox.Text, _matchCase?.IsChecked == true);
            UpdateMatchLabel();
        };

        _matchCase = new ToggleButton { Content = "Aa", FontSize = 12, Padding = new Thickness(6, 2), Focusable = false };
        ToolTip.SetTip(_matchCase, L("MatchCase"));
        _matchCase.Click += (_, _) =>
        {
            Editor.SetFindHighlight(_findBox.Text, _matchCase.IsChecked == true);
            UpdateMatchLabel();
        };

        _matchLabel = new TextBlock { FontSize = 12, MinWidth = 40, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };

        Button Btn(string text, string tip, Action act)
        {
            // Not focusable: focus stays in the query box (browser / VS Code), or the next Enter would re-press the
            // button instead of searching on.
            var b = new Button { Content = text, FontSize = 12, Padding = new Thickness(8, 2), Focusable = false };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => act();
            return b;
        }

        // Leading chevron: expands the replace row in place, so a search started with Ctrl+F need not be reopened.
        _expandReplace = new ToggleButton { Content = "▸", FontSize = 12, Padding = new Thickness(6, 2), Focusable = false };
        ToolTip.SetTip(_expandReplace, L("ToggleReplace") + " (Ctrl+H)");
        _expandReplace.Click += (_, _) =>
        {
            bool show = _expandReplace.IsChecked == true && !Editor.IsReadOnly;
            SetReplaceVisible(show);
            if (show) _replaceBox?.Focus();
        };

        var findRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        findRow.Children.Add(_expandReplace);
        findRow.Children.Add(_findBox);
        findRow.Children.Add(_matchLabel);
        findRow.Children.Add(Btn("◀", L("FindPrevious"), () => DoFind(backwards: true)));
        findRow.Children.Add(Btn("▶", L("FindNext") + " (F3)", () => DoFind(backwards: false)));
        findRow.Children.Add(_matchCase);
        findRow.Children.Add(Btn("✕", L("Cancel"), HideFindBar));

        _replaceBox = new TextBox { MinWidth = 200, PlaceholderText = L("Replace"), FontSize = 12 };
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DoReplace(); e.Handled = true; }
            else if (e.Key == Key.Escape) { HideFindBar(); e.Handled = true; }
        };

        _replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        _replaceRow.Children.Add(_replaceBox);
        _replaceRow.Children.Add(Btn(L("Replace"), L("Replace"), DoReplace));
        _replaceRow.Children.Add(Btn(L("ReplaceAll"), L("ReplaceAll"), DoReplaceAll));

        _findBar = new StackPanel { Spacing = 4, Margin = new Thickness(8, 6) };
        _findBar.Children.Add(findRow);
        _findBar.Children.Add(_replaceRow);
        _findBarHost.Child = _findBar;
        _findBarHost.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        _findBarHost.Background = new SolidColorBrush(Color.FromArgb(10, 0, 0, 0));
    }

    private void DoFind(bool backwards)
    {
        string q = _findBox?.Text ?? "";
        if (q.Length == 0) return;
        bool mc = _matchCase?.IsChecked == true;
        if (backwards) Editor.FindPrev(q, mc); else Editor.FindNext(q, mc);
        UpdateMatchLabel();
    }

    private void DoReplace()
    {
        string q = _findBox?.Text ?? "";
        if (q.Length == 0 || Editor.IsReadOnly) return;
        Editor.ReplaceNext(q, _replaceBox?.Text ?? "", _matchCase?.IsChecked == true);
        UpdateMatchLabel();
    }

    private void DoReplaceAll()
    {
        string q = _findBox?.Text ?? "";
        if (q.Length == 0 || Editor.IsReadOnly) return;
        Editor.ReplaceAll(q, _replaceBox?.Text ?? "", _matchCase?.IsChecked == true);
        UpdateMatchLabel();
    }
}
