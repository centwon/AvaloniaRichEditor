using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AvaloniaRichEditor.Controls;

// Single-line toolbar strip that collapses overflow into a "»" dropdown instead of clipping or
// scrolling. Items that don't fit the available width are moved (reparented) into a flyout opened
// by a trailing more-button; widening the host moves them back. The toolbar's own command/state
// wiring is by-reference, so an item works the same whether it sits on the strip or in the flyout.
//
// Reparenting mutates the visual tree, which the layout manager forbids *during* a measure/arrange
// pass (it throws). So MeasureOverride only computes the desired split and queues the actual
// reparent to run after the pass via the dispatcher; the tree and _fitCount always stay in sync
// within a pass.
internal sealed class OverflowToolbarPanel : Panel
{
    private readonly List<Control> _items;
    private readonly Button _overflowButton;
    private readonly StackPanel _overflowContent; // flyout body; a horizontal continuation of the strip
    private int _fitCount;                         // applied split: leading items currently on the strip
    private int _pendingFit;                       // split requested by the latest measure
    private bool _reparentQueued;

    public OverflowToolbarPanel(IEnumerable<Control> items)
    {
        _items = new List<Control>(items);

        _overflowContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _overflowButton = new Button
        {
            Content = ToolbarIcons.OverflowChevron(),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 5),
            Margin = new Thickness(1, 0),
            MinWidth = 30,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                Content = new Border { Padding = new Thickness(4, 2), Child = _overflowContent },
            },
        };

        foreach (var it in _items) Children.Add(it);
        Children.Add(_overflowButton);
        _fitCount = _pendingFit = _items.Count; // everything starts on the strip
    }

    private static double ItemWidth(Control c) => c.IsVisible ? c.DesiredSize.Width : 0;

    // How many leading items fit `avail`, reserving room for the overflow button when not all fit.
    private int ComputeFit(double avail)
    {
        if (double.IsInfinity(avail)) return _items.Count;

        double total = 0;
        foreach (var it in _items) total += ItemWidth(it);
        if (total <= avail) return _items.Count;

        double reserve = _overflowButton.DesiredSize.Width, used = 0;
        int fit = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            double w = ItemWidth(_items[i]);
            if (used + w + reserve <= avail) { used += w; fit++; }
            else break;
        }
        return fit;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Natural widths first (unconstrained), including items currently parked in the flyout.
        foreach (var it in _items) it.Measure(Size.Infinity);
        _overflowButton.Measure(Size.Infinity);

        int target = ComputeFit(availableSize.Width);
        if (target != _fitCount) QueueReparent(target);

        // Measure to the split that's actually applied to the tree right now (not `target`), so this
        // pass stays self-consistent; the queued reparent triggers a fresh pass once it lands.
        double width = 0, height = 0;
        for (int i = 0; i < _fitCount && i < _items.Count; i++) { width += ItemWidth(_items[i]); height = Math.Max(height, _items[i].DesiredSize.Height); }
        if (_fitCount < _items.Count) { width += _overflowButton.DesiredSize.Width; height = Math.Max(height, _overflowButton.DesiredSize.Height); }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < _fitCount && i < _items.Count; i++)
        {
            double w = ItemWidth(_items[i]);
            _items[i].Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        if (_overflowButton.IsVisible)
            _overflowButton.Arrange(new Rect(x, 0, _overflowButton.DesiredSize.Width, finalSize.Height));
        return finalSize;
    }

    private void QueueReparent(int target)
    {
        _pendingFit = target;
        if (_reparentQueued) return;
        _reparentQueued = true;
        // After the current layout pass: mutating Children here is safe and triggers a clean re-layout.
        Dispatcher.UIThread.Post(() =>
        {
            _reparentQueued = false;
            if (_pendingFit == _fitCount) return;
            Reparent(_pendingFit);
            _fitCount = _pendingFit;
            InvalidateMeasure();
        }, DispatcherPriority.Render);
    }

    private void Reparent(int fit)
    {
        Children.Clear();
        _overflowContent.Children.Clear();
        for (int i = 0; i < fit; i++) Children.Add(_items[i]);
        bool overflow = fit < _items.Count;
        _overflowButton.IsVisible = overflow;
        Children.Add(_overflowButton);
        if (overflow)
            for (int i = fit; i < _items.Count; i++) _overflowContent.Children.Add(_items[i]);
    }
}
