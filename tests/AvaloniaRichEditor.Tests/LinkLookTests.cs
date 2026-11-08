using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A link is drawn as on the WinUI control: link blue unless it has a colour of its own, and always underlined — a
// struck-through link too. It was drawn in the plain text colour, and without an underline once it carried a
// strikethrough. The caret format reports the link as drawn, so the toolbar shows what is on screen (2026-11-09).
public class LinkLookTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static RichEditor CaretIn(Run run)
    {
        var p = new Paragraph { Inlines = { run } };
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc };
        foreach (var name in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(name, NP)!.SetValue(ed, new TextPointer(p, 2));
        return ed;
    }

    private static Color? ColorOf(IBrush? b) => (b as ISolidColorBrush)?.Color;

    [AvaloniaFact]
    public void ALinkWithoutAColour_IsReportedLinkBlueAndUnderlined()
    {
        var f = CaretIn(new Run { Text = "link", NavigateUri = "https://a.example/" }).GetCaretFormat();
        Assert.Equal(Color.FromRgb(0, 0, 255), ColorOf(f.Foreground));
        Assert.True(f.Underline);
    }

    [AvaloniaFact]
    public void AStruckThroughLink_IsStillUnderlined()
    {
        var run = new Run { Text = "link", NavigateUri = "https://a.example/",
            TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } } };
        var f = CaretIn(run).GetCaretFormat();
        Assert.True(f.Underline);
        Assert.True(f.Strike);
    }

    [AvaloniaFact]
    public void ALinkWithItsOwnColour_KeepsIt()
    {
        var red = Color.FromRgb(200, 0, 0);
        var f = CaretIn(new Run { Text = "link", NavigateUri = "https://a.example/", Foreground = new SolidColorBrush(red) }).GetCaretFormat();
        Assert.Equal(red, ColorOf(f.Foreground));
    }

    [AvaloniaFact]
    public void TheLayout_DrawsALinkBlueAndUnderlined_WithAStrikethrough()
    {
        var run = new Run { Text = "link", NavigateUri = "https://a.example/",
            TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Strikethrough } } };
        var ed = CaretIn(run);
        var p = (Paragraph)ed.Document!.Blocks[0];
        var layout = (Avalonia.Media.TextFormatting.TextLayout)typeof(RichEditor).GetMethod("BuildTextLayout", NP, null,
            new[] { typeof(Paragraph), typeof(double), typeof(int), typeof(string) }, null)!.Invoke(ed, new object?[] { p, 400.0, -1, null })!;
        var props = layout.TextLines.SelectMany(l => l.TextRuns).Select(t => t.Properties).First(x => x != null)!;
        Assert.Equal(Color.FromRgb(0, 0, 255), ColorOf(props.ForegroundBrush));
        Assert.NotNull(props.TextDecorations);
        Assert.Contains(props.TextDecorations!, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(props.TextDecorations!, d => d.Location == TextDecorationLocation.Strikethrough);
    }
}
