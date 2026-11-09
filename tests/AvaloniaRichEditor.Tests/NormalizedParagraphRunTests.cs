using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The paragraph the editor keeps before, after and between tables was written with no runs here and with one empty run by
// the WinUI control: the same document saved as different JSON. Both write the empty run, as an Enter split does (2026-11-08).
public class NormalizedParagraphRunTests
{
    [AvaloniaFact]
    public void TheParagraphsAroundATable_SaveWithOneEmptyRun()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(new TableBlock(1, 1));
        doc.Blocks.Add(new TableBlock(1, 1));
        var ed = new RichEditor { Document = doc };
        using var json = System.Text.Json.JsonDocument.Parse(DocumentSerializer.Serialize(ed.Document!));
        int paragraphs = 0;
        foreach (var block in json.RootElement.GetProperty("Blocks").EnumerateArray())
        {
            if (block.TryGetProperty("Type", out var type) && type.GetString() != "Paragraph") continue;
            paragraphs++;
            Assert.True(block.TryGetProperty("Inlines", out var inlines), block.GetRawText());
            Assert.Equal("[{\"Text\":\"\"}]", inlines.GetRawText());
        }
        Assert.Equal(3, paragraphs);
    }

    // RTF has no empty run to write, so a blank line read back with none — and the next save differed from the last.
    [Fact]
    public void ABlankRtfLine_ReadsWithOneEmptyRun()
    {
        var doc = RtfDocumentFormatter.Parse("{\\rtf1 \\pard a\\par \\pard\\par \\pard b\\par}");
        var blank = doc.Blocks.OfType<Paragraph>().ElementAt(1);
        var run = Assert.IsType<Run>(Assert.Single(blank.Inlines));
        Assert.Equal("", run.Text);
    }
}
