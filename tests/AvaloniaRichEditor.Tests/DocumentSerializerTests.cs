using System.Linq;
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

    // Regression: a NaN-sized inline image must serialize without throwing (System.Text.Json rejects a
    // raw NaN). NanToNull drops it to null, and read-back falls to the 16px default like the block path.
    [Fact]
    public void Serialize_InlineImageWithNaNSize_DoesNotThrow()
    {
        var img = new InlineImage { Width = double.NaN, Height = double.NaN };
        img.SetImageData(new byte[] { 1, 2, 3 }, "image/png");
        var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "x" }, img));

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        var p = Assert.IsType<Paragraph>(doc2.Blocks[0]);
        var im = p.Inlines.OfType<InlineImage>().Single();
        Assert.Equal(16, im.Width);
        Assert.Equal(16, im.Height);
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
    public void RoundTrip_PreservesLineSpacing()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { LineSpacing = 1.6, Inlines = { new Run { Text = "spaced" } } });
        doc.Blocks.Add(new Paragraph { LineHeight = 30, Inlines = { new Run { Text = "fixed" } } });

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        var p0 = Assert.IsType<Paragraph>(doc2.Blocks[0]);
        var p1 = Assert.IsType<Paragraph>(doc2.Blocks[1]);

        Assert.Equal(1.6, p0.LineSpacing, 3);
        Assert.True(double.IsNaN(p0.LineHeight));   // proportional only — absolute stays unset
        Assert.Equal(30, p1.LineHeight, 3);
        Assert.True(double.IsNaN(p1.LineSpacing));  // absolute only — proportional stays unset
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
        Assert.Contains($"\"Version\": \"{DocumentSerializer.CurrentSchemaVersion}\"", json);
    }

    [Fact]
    public void Deserialize_AcceptsLegacyNumericVersion_AndReSerializesAsSemVer()
    {
        // Files written before the SemVer switch carry a numeric "Version" (e.g. 2). The tolerant
        // converter must read them, and re-serialization stamps the current string version.
        var legacy = """{"Version":2,"Blocks":[{"Type":"Paragraph","Inlines":[{"Type":"Run","Text":"hi"}]}]}""";
        var doc = DocumentSerializer.Deserialize(legacy);
        Assert.Equal("hi", Assert.IsType<Paragraph>(doc.Blocks[0]).Text());

        var json = DocumentSerializer.Serialize(doc);
        Assert.Contains("\"Version\": \"1.0\"", json);
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
