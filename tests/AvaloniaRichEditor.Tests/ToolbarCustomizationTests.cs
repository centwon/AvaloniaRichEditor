using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorToolbar.FontSizes / Palette let a host replace the built-in options. Both are global state,
// so every test restores them.
//
// The setters validate instead of taking whatever they are given: the arrays are consumed while the
// toolbar builds itself, so a null or a nonsense size would otherwise surface as a crash inside the
// build with nothing pointing back at the assignment that caused it.
public class ToolbarCustomizationTests
{
    private static ComboBox[] Combos(RichEditorToolbar tb) =>
        ((Panel)((Border)tb.Content!).Child!).Children.OfType<ComboBox>().ToArray();

    // ---- the options actually reach the toolbar ----------------------------

    [AvaloniaFact]
    public void FontSizes_ReplacesTheSizeComboContents()
    {
        var original = RichEditorToolbar.FontSizes;
        try
        {
            RichEditorToolbar.FontSizes = new double[] { 11, 13, 17 };
            var tb = new RichEditorToolbar { Target = new RichEditor() };

            var texts = Combos(tb)
                .Select(c => c.Items.OfType<ComboBoxItem>().Select(i => i.Content?.ToString()).ToArray())
                .FirstOrDefault(items => items.Contains("11"));

            Assert.NotNull(texts);
            Assert.Equal(new[] { "11", "13", "17" }, texts);
        }
        finally { RichEditorToolbar.FontSizes = original; }
    }

    // The combo matches its selection by item TEXT, so labels and the reflected caret value have to be
    // formatted the same way. Before this they were not: items were ints and the reflect cast to int,
    // which meant a fractional size could never show as selected.
    [AvaloniaFact]
    public void FontSizes_SupportsFractionalPointSizes()
    {
        var original = RichEditorToolbar.FontSizes;
        try
        {
            RichEditorToolbar.FontSizes = new[] { 10.5, 12 };
            var tb = new RichEditorToolbar { Target = new RichEditor() };

            var texts = Combos(tb)
                .Select(c => c.Items.OfType<ComboBoxItem>().Select(i => i.Content?.ToString()).ToArray())
                .FirstOrDefault(items => items.Contains("10.5"));

            Assert.NotNull(texts);
            Assert.Equal(new[] { "10.5", "12" }, texts);
        }
        finally { RichEditorToolbar.FontSizes = original; }
    }

    [AvaloniaFact]
    public void Palette_ReplacementIsAccepted()
    {
        var original = RichEditorToolbar.Palette;
        try
        {
            RichEditorToolbar.Palette = new[] { "#112233", "#445566" };
            Assert.Equal(new[] { "#112233", "#445566" }, RichEditorToolbar.Palette);
            _ = new RichEditorToolbar { Target = new RichEditor() }; // builds without throwing
        }
        finally { RichEditorToolbar.Palette = original; }
    }

    // ---- guards ------------------------------------------------------------

    [Fact]
    public void FontSizes_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => RichEditorToolbar.FontSizes = null!);

    [Fact]
    public void FontSizes_RejectsEmpty()
        => Assert.Throws<ArgumentException>(() => RichEditorToolbar.FontSizes = Array.Empty<double>());

    [Theory]
    [InlineData(0)]
    [InlineData(-12)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FontSizes_RejectsSizesThatAreNotPositiveAndFinite(double bad)
        => Assert.Throws<ArgumentException>(() => RichEditorToolbar.FontSizes = new[] { 10, bad });

    [Fact]
    public void Palette_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => RichEditorToolbar.Palette = null!);

    [Fact]
    public void Palette_RejectsEmpty()
        => Assert.Throws<ArgumentException>(() => RichEditorToolbar.Palette = Array.Empty<string>());

    // A rejected assignment must not have half-applied.
    [Fact]
    public void RejectedAssignment_LeavesTheDefaultsIntact()
    {
        var sizes = RichEditorToolbar.FontSizes;
        var palette = RichEditorToolbar.Palette;
        Assert.Throws<ArgumentException>(() => RichEditorToolbar.FontSizes = Array.Empty<double>());
        Assert.Throws<ArgumentNullException>(() => RichEditorToolbar.Palette = null!);
        Assert.Same(sizes, RichEditorToolbar.FontSizes);
        Assert.Same(palette, RichEditorToolbar.Palette);
    }

    // Entry FORMAT is deliberately not validated: the swatch grid is cosmetic and an unparseable entry
    // degrades to a fallback swatch, so a typo shows up as a wrong colour rather than a crash.
    [AvaloniaFact]
    public void Palette_UnparseableEntryDoesNotThrow()
    {
        var original = RichEditorToolbar.Palette;
        try
        {
            RichEditorToolbar.Palette = new[] { "not-a-colour", "#00FF00" };
            _ = new RichEditorToolbar { Target = new RichEditor() };
        }
        finally { RichEditorToolbar.Palette = original; }
    }
}
