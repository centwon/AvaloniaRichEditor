using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class DocumentSerializerTests
{
    private static FlowDocument SampleDoc()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(TestHelpers.Para(
            new Run { Text = "Hello", FontWeight = FontWeight.Bold, Foreground = Brushes.Red },
            new Run { Text = " world" }));
        doc.Blocks.Add(new Paragraph
        {
            HeadingLevel = 1,
            TextAlignment = TextAlignment.Center,
            Inlines = { new Run { Text = "Title" } }
        });
        var tb = new TableBlock(2, 2);
        tb.Cells[0][0].Inlines.Clear();
        tb.Cells[0][0].Inlines.Add(new Run { Text = "cell" });
        tb.MergeCells(0, 0, 0, 1);
        doc.Blocks.Add(tb);
        return doc;
    }

    [Fact]
    public void RoundTrip_PreservesBlockCount_AndText()
    {
        var doc = SampleDoc();
        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));

        Assert.Equal(doc.Blocks.Count, doc2.Blocks.Count);
        var p0 = Assert.IsType<Paragraph>(doc2.Blocks[0]);
        Assert.Equal("Hello world", p0.Text());
    }

    [Fact]
    public void RoundTrip_PreservesInlineFormatting()
    {
        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(SampleDoc()));
        var p0 = Assert.IsType<Paragraph>(doc2.Blocks[0]);
        var bold = Assert.IsType<Run>(p0.Inlines[0]);

        Assert.Equal(FontWeight.Bold, bold.FontWeight);
        Assert.Equal(Colors.Red, (bold.Foreground as ISolidColorBrush)?.Color);
    }

    [Fact]
    public void RoundTrip_PreservesParagraphAndTableStructure()
    {
        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(SampleDoc()));
        var heading = Assert.IsType<Paragraph>(doc2.Blocks[1]);
        Assert.Equal(1, heading.HeadingLevel);
        Assert.Equal(TextAlignment.Center, heading.TextAlignment);

        var tb = Assert.IsType<TableBlock>(doc2.Blocks[2]);
        Assert.Equal((2, 1), tb.SpanOf(0, 0)); // merged colspan survives
    }

    [Fact]
    public void Serialize_IsIdempotent()
    {
        var json1 = DocumentSerializer.Serialize(SampleDoc());
        var json2 = DocumentSerializer.Serialize(DocumentSerializer.Deserialize(json1));
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Serialize_WritesSchemaVersion()
    {
        var json = DocumentSerializer.Serialize(SampleDoc());
        Assert.Contains($"\"Version\": {DocumentSerializer.CurrentSchemaVersion}", json);
    }

    [Fact]
    public void Deserialize_LegacyDocWithoutVersion_StillLoads()
    {
        // Pre-versioning documents have no "version" field; they must still round-trip.
        var legacy = "{\"Blocks\":[{\"Type\":\"Paragraph\",\"Inlines\":[{\"Type\":\"Run\",\"Text\":\"hi\"}]}]}";
        var doc = DocumentSerializer.Deserialize(legacy);
        Assert.Single(doc.Blocks);
        Assert.Equal("hi", Assert.IsType<Paragraph>(doc.Blocks[0]).Text());
    }
}
