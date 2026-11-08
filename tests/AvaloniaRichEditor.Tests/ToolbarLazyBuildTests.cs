using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Lightness (2026-11-09): a RichEditorView allocated 7.8 MB to its first frame, nearly all of it the toolbar —
// the strip was built three times (constructor, Target, ToolbarLevel) and every build made the pickers' panels too.
// Now a toolbar without a target builds no strip, and a picker's panel is made the first time it opens.
public class ToolbarLazyBuildTests : IDisposable
{
    private readonly List<InteractionHost> _hosts = new();

    public void Dispose()
    {
        foreach (var h in _hosts) h.Dispose();
    }

    private static IEnumerable<FlyoutBase> Flyouts(RichEditorToolbar toolbar)
        => toolbar.GetLogicalDescendants().OfType<Control>()
            .Select(c => (c as Button)?.Flyout ?? FlyoutBase.GetAttachedFlyout(c))
            .OfType<FlyoutBase>()
            .Distinct();

    private RichEditorToolbar Shown()
    {
        var (host, toolbar) = InteractionHost.CreateWithToolbar(new RichEditor());
        _hosts.Add(host);
        return toolbar;
    }

    [AvaloniaFact]
    public void WithoutTarget_NoStripIsBuilt_AndTheTargetBuildsIt()
    {
        var tb = new RichEditorToolbar { ToolbarLevel = ToolbarLevel.Maximum };
        Assert.Null(tb.Content);
        Assert.False(tb.IsVisible);

        tb.Target = new RichEditor();
        Assert.NotNull(tb.Content);
        Assert.True(tb.IsVisible);

        tb.Target = null; // the old strip goes with its target
        Assert.Null(tb.Content);
        Assert.False(tb.IsVisible);
    }

    [AvaloniaFact]
    public void HostItemsAddedBeforeTheTarget_AppearOnceItArrives()
    {
        var lead = new Button { Content = "lead" };
        var tb = new RichEditorToolbar();
        tb.LeadingItems.Add(lead);
        tb.Target = new RichEditor();
        Assert.Contains(lead, ((Panel)((Border)tb.Content!).Child!).Children);
    }

    [AvaloniaFact]
    public void PickerPanels_AreBuiltWhenFirstOpened()
    {
        var toolbar = Shown();
        var flyouts = Flyouts(toolbar).OfType<Flyout>().ToList();
        // Colour ×2, table, line spacing, bullet and number lists are lazy; the margin menu (Normal level has none) is not here.
        Assert.Equal(6, flyouts.Count(f => f.Content == null));

        foreach (var f in flyouts)
        {
            f.ShowAt(toolbar);
            Assert.NotNull(f.Content);
            var first = f.Content;
            f.Hide();
            f.ShowAt(toolbar); // reopening keeps the panel
            Assert.Same(first, f.Content);
            f.Hide();
        }
    }

    // The palette is read when a colour picker first opens, so the swatches are the palette at that moment.
    [AvaloniaFact]
    public void Palette_ReachesTheOpenedPicker()
    {
        var original = RichEditorToolbar.Palette;
        try
        {
            RichEditorToolbar.Palette = new[] { "#112233", "not-a-colour" }; // a bad entry degrades, it does not throw
            var toolbar = Shown();
            var swatchColors = new List<Color>();
            foreach (var f in Flyouts(toolbar).OfType<Flyout>())
            {
                f.ShowAt(toolbar);
                if (f.Content is StackPanel { Children: [UniformGrid { Columns: 8 } grid, ..] })
                    swatchColors.AddRange(grid.Children.OfType<Button>().Select(b => ((ISolidColorBrush)b.Background!).Color));
                f.Hide();
            }
            // Text colour and highlight: each the two entries, the bad one as black.
            Assert.Equal(new[] { Color.Parse("#112233"), Colors.Black, Color.Parse("#112233"), Colors.Black }, swatchColors);
        }
        finally { RichEditorToolbar.Palette = original; }
    }
}
