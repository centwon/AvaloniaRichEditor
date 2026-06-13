using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorToolbar (roadmap N3.6): Target wiring, feature-flag visibility (AllowTables/AllowImages
// hide insert buttons, ReadOnly hides the strip), and caret-state reflection on the toggle buttons.
public class RichEditorToolbarTests
{
    private static Panel Strip(RichEditorToolbar tb) =>
        (Panel)((Border)tb.Content!).Child!;

    private static Button ButtonByContent(RichEditorToolbar tb, string content) =>
        Strip(tb).Children.OfType<Button>().First(b => Equals(b.Content, content));

    // Insert buttons now use vector/dropdown faces (not string content), so locate them by tooltip.
    private static Button ButtonByTip(RichEditorToolbar tb, string tipKey) =>
        Strip(tb).Children.OfType<Button>().First(b =>
            ToolTip.GetTip(b) is string s && s == RichEditorLocalization.GetString(tipKey));

    [AvaloniaFact]
    public void WithoutTarget_IsHidden()
    {
        var tb = new RichEditorToolbar();
        Assert.False(tb.IsVisible);
    }

    [AvaloniaFact]
    public void HostItems_AppearInTheStrip()
    {
        var tb = new RichEditorToolbar { Target = new RichEditor() };
        var lead = new Button { Content = "lead" };
        var trail = new Button { Content = "trail" };
        tb.LeadingItems.Add(lead);
        tb.TrailingItems.Add(trail);

        var children = Strip(tb).Children;
        Assert.Contains(lead, children);
        Assert.Contains(trail, children);
        // Leading sits before trailing, with the formatting buttons in between.
        Assert.True(children.IndexOf(lead) < children.IndexOf(trail));
    }

    // Narrowing the host wraps the toolbar onto more rows (WrapPanel) without throwing or clipping.
    [AvaloniaFact]
    public void Narrowing_WrapsWithoutThrowing()
    {
        var ed = new RichEditor();
        var win = new Window { Width = 1000, Height = 200, Content = new RichEditorToolbar { Target = ed } };
        win.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var w in new double[] { 600, 400, 300, 250, 200, 150, 100, 60, 30, 200, 1000 })
            {
                win.Width = w;
                win.Measure(new Avalonia.Size(w, 200));
                win.Arrange(new Avalonia.Rect(0, 0, w, 200));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }
        finally { win.Close(); }
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
        var tableBtn = ButtonByTip(tb, "InsertTable");
        var imageBtn = ButtonByTip(tb, "InsertImage");
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
    public void FontCombo_NoExplicitFont_ShowsEffectiveDefaultAsPlaceholder()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>plain</p>"); // run carries no explicit FontFamily
        ed.FocusDocumentEnd();
        var tb = new RichEditorToolbar { Target = ed };
        var fonts = Strip(tb).Children.OfType<ComboBox>().First();
        Assert.Null(fonts.SelectedItem);
        Assert.False(string.IsNullOrEmpty(fonts.PlaceholderText));
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
