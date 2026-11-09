using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A fixed line height set on a paragraph with a proportional line spacing changed nothing on screen (the spacing wins) and
// the file kept both. Setting either form clears the other, as on the WinUI control (2026-11-09).
public class LineSpacingExclusiveTests
{
    private static (RichEditor ed, Paragraph p) OneParagraph()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>text</p>");
        return (ed, ed.Document!.Blocks.OfType<Paragraph>().First());
    }

    [AvaloniaFact]
    public void SettingALineHeight_ClearsTheSpacing()
    {
        var (ed, p) = OneParagraph();
        ed.SetLineSpacing(2.0);
        ed.SetLineHeight(30);
        Assert.Equal(30, p.LineHeight);
        Assert.True(double.IsNaN(p.LineSpacing));
    }

    [AvaloniaFact]
    public void SettingASpacing_ClearsTheLineHeight()
    {
        var (ed, p) = OneParagraph();
        ed.SetLineHeight(30);
        ed.SetLineSpacing(2.0);
        Assert.Equal(2.0, p.LineSpacing);
        Assert.True(double.IsNaN(p.LineHeight));
    }
}
