using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorToolbar (roadmap N3.6): Target wiring, feature-flag visibility (AllowTables/AllowImages
// hide insert buttons, ReadOnly hides the strip), and caret-state reflection on the toggle buttons.
public class RichEditorToolbarTests
{
    private static WrapPanel Strip(RichEditorToolbar tb) => (WrapPanel)((Border)tb.Content!).Child!;

    private static Button ButtonByContent(RichEditorToolbar tb, string content) =>
        Strip(tb).Children.OfType<Button>().First(b => Equals(b.Content, content));

    [AvaloniaFact]
    public void WithoutTarget_IsHidden()
    {
        var tb = new RichEditorToolbar();
        Assert.False(tb.IsVisible);
    }

    [AvaloniaFact]
    public void WithTarget_IsVisible_AndReadOnlyHidesIt()
    {
        var ed = new RichEditor();
        var tb = new RichEditorToolbar { Target = ed };
        Assert.True(tb.IsVisible);

        ed.IsReadOnly = true;
        Assert.False(tb.IsVisible);
        ed.IsReadOnly = false;
        Assert.True(tb.IsVisible);
    }

    [AvaloniaFact]
    public void FeatureFlags_HideInsertButtons()
    {
        var ed = new RichEditor();
        var tb = new RichEditorToolbar { Target = ed };
        var tableBtn = ButtonByContent(tb, "▦ ▾");
        var imageBtn = ButtonByContent(tb, "🖼");
        Assert.True(tableBtn.IsVisible);
        Assert.True(imageBtn.IsVisible);

        ed.AllowTables = false;
        ed.AllowImages = false;
        Assert.False(tableBtn.IsVisible);
        Assert.False(imageBtn.IsVisible);
    }

    [AvaloniaFact]
    public void CaretOnBoldText_ActivatesBoldButton()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p><b>ab</b></p>");
        ed.FocusDocumentEnd();
        Assert.True(ed.GetCaretFormat().Bold); // precondition

        var tb = new RichEditorToolbar { Target = ed };
        var boldBtn = Strip(tb).Children.OfType<Button>()
            .First(b => b.Content is TextBlock { Text: "B" });
        Assert.NotEqual(Avalonia.Media.Brushes.Transparent, boldBtn.Background);
    }

    [AvaloniaFact]
    public void FontFamilyChoices_DefaultsToNonEmptyList()
    {
        // Default = system fonts; platforms without enumeration (headless) fall back to a stock list.
        var ed = new RichEditor();
        Assert.NotEmpty(ed.FontFamilyChoices);
    }

    [AvaloniaFact]
    public void FontCombo_ListsTargetFontFamilyChoices()
    {
        var ed = new RichEditor { FontFamilyChoices = new[] { "FontA", "FontB" } };
        var tb = new RichEditorToolbar { Target = ed };
        var fonts = Strip(tb).Children.OfType<ComboBox>().First();
        Assert.Equal(new[] { "FontA", "FontB" },
            fonts.Items.OfType<ComboBoxItem>().Select(i => i.Content).ToArray());
    }
}
