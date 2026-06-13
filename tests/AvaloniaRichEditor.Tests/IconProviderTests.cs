using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorIcons hook: a host-provided factory replaces the built-in toolbar glyphs; no provider
// (or a null return) keeps the text glyphs. Provider is global state, so each test restores it.
public class IconProviderTests
{
    private static IEnumerable<object?> ButtonFaces(RichEditorToolbar tb) =>
        ((Panel)((Border)tb.Content!).Child!).Children.OfType<Button>().Select(b => b.Content);

    [AvaloniaFact]
    public void Provider_ReplacesToolbarGlyphs()
    {
        try
        {
            RichEditorIcons.Provider = key =>
                key == RichEditorIcon.Bold ? new Border { Tag = "custom-bold" } : null;
            var tb = new RichEditorToolbar { Target = new RichEditor() };

            var faces = ButtonFaces(tb).ToList();
            Assert.Contains(faces, c => c is Border { Tag: "custom-bold" });   // provided slot swapped
            Assert.Contains(faces, c => c is TextBlock { Text: "I" });        // null return keeps the glyph
        }
        finally
        {
            RichEditorIcons.Provider = null;
        }
    }

    [AvaloniaFact]
    public void NoProvider_KeepsBuiltInGlyphs()
    {
        RichEditorIcons.Provider = null;
        var tb = new RichEditorToolbar { Target = new RichEditor() };

        Assert.Contains(ButtonFaces(tb), c => c is TextBlock { Text: "B" });
    }
}
