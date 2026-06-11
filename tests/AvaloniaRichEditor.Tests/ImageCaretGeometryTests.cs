using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Media.TextFormatting;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Avalonia drops a line-trailing DrawableTextRun from caret-distance computation, so the position
// right after a trailing inline image collapses to the image's LEFT edge (caret looked stuck in
// front of the image while typing landed behind it). FixCaretAfterTrailingImage pins it right.
public class ImageCaretGeometryTests
{
    private static (Paragraph p, TextLayout layout) LayoutOf(params Inline[] inlines)
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var p = new Paragraph();
        foreach (var i in inlines) p.Inlines.Add(i);
        doc.Blocks.Add(p);
        ed.Document = doc;
        // BuildTextLayout is the editor's single source of truth for geometry; private by design.
        var mi = typeof(RichEditor).GetMethod("BuildTextLayout", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (p, (TextLayout)mi.Invoke(ed, new object?[] { p, 670.0, -1, null })!);
    }

    [AvaloniaFact]
    public void CaretAfterTrailingImage_SitsAtTheImagesRightEdge()
    {
        var (p, layout) = LayoutOf(new Run { Text = "abc" }, new InlineImage { Width = 50, Height = 50 });
        double imgLeft = layout.HitTestTextPosition(3).X;

        var raw = layout.HitTestTextPosition(4); // Avalonia quirk: collapses to the image's left edge
        var fixedRect = RichEditor.FixCaretAfterTrailingImage(layout, p, 4, 4, raw);

        Assert.Equal(imgLeft + 50, fixedRect.X, 1);
    }

    [AvaloniaFact]
    public void CaretAfterImage_WithFollowingText_IsNotTouched()
    {
        var (p, layout) = LayoutOf(new Run { Text = "abc" }, new InlineImage { Width = 50, Height = 50 }, new Run { Text = "def" });
        var raw = layout.HitTestTextPosition(4); // healthy: already at the image's right edge
        var fixedRect = RichEditor.FixCaretAfterTrailingImage(layout, p, 4, 4, raw);
        Assert.Equal(raw.X, fixedRect.X, 1);
    }

    [AvaloniaFact]
    public void CaretNotAfterAnImage_IsNotTouched()
    {
        var (p, layout) = LayoutOf(new Run { Text = "abc" }, new InlineImage { Width = 50, Height = 50 });
        var raw = layout.HitTestTextPosition(2); // inside "abc"
        var fixedRect = RichEditor.FixCaretAfterTrailingImage(layout, p, 2, 2, raw);
        Assert.Equal(raw.X, fixedRect.X, 1);
    }
}
