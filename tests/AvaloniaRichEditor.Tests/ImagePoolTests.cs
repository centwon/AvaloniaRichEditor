using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// JSON image pool (schema v2, roadmap N6): identical image bytes are stored once at the document
// level and referenced by SHA-256 hash; loading shares one byte[] per pool entry. Legacy v1
// documents (inline base64 per image) must still load.
public class ImagePoolTests
{
    private static byte[] FakeJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04 };
    private static byte[] OtherJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x09, 0x08, 0x07, 0x06 };

    private static FlowDocument DocWithDuplicateImages()
    {
        var doc = new FlowDocument();
        var b1 = new ImageBlock(); b1.SetImageData(FakeJpeg(), "image/jpeg");
        var b2 = new ImageBlock(); b2.SetImageData(FakeJpeg(), "image/jpeg");
        var p = new Paragraph();
        var im = new InlineImage(); im.SetImageData(FakeJpeg(), "image/jpeg");
        p.Inlines.Add(im);
        doc.Blocks.Add(b1);
        doc.Blocks.Add(b2);
        doc.Blocks.Add(p);
        return doc;
    }

    [Fact]
    public void DuplicateImages_AreStoredOnceInJson()
    {
        string json = DocumentSerializer.Serialize(DocWithDuplicateImages());
        string payload = System.Convert.ToBase64String(FakeJpeg());
        Assert.Single(Regex.Matches(json, Regex.Escape(payload)));
    }

    [Fact]
    public void PooledImages_RoundTrip_RestoresAllBytes()
    {
        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(DocWithDuplicateImages()));
        var b1 = Assert.IsType<ImageBlock>(doc2.Blocks[0]);
        var b2 = Assert.IsType<ImageBlock>(doc2.Blocks[1]);
        var im = Assert.IsType<InlineImage>(Assert.IsType<Paragraph>(doc2.Blocks[2]).Inlines[0]);
        Assert.Equal(FakeJpeg(), b1.RawBytes);
        Assert.Equal(FakeJpeg(), b2.RawBytes);
        Assert.Equal(FakeJpeg(), im.RawBytes);
        Assert.Equal("image/jpeg", b1.MimeType);
    }

    [Fact]
    public void PooledImages_ShareOneByteArrayAfterLoad()
    {
        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(DocWithDuplicateImages()));
        var b1 = Assert.IsType<ImageBlock>(doc2.Blocks[0]);
        var b2 = Assert.IsType<ImageBlock>(doc2.Blocks[1]);
        Assert.Same(b1.RawBytes, b2.RawBytes); // memory dedup, not just disk dedup
    }

    [Fact]
    public void DistinctImages_GetDistinctPoolEntries()
    {
        var doc = new FlowDocument();
        var b1 = new ImageBlock(); b1.SetImageData(FakeJpeg(), "image/jpeg");
        var b2 = new ImageBlock(); b2.SetImageData(OtherJpeg(), "image/jpeg");
        doc.Blocks.Add(b1);
        doc.Blocks.Add(b2);

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        Assert.Equal(FakeJpeg(), Assert.IsType<ImageBlock>(doc2.Blocks[0]).RawBytes);
        Assert.Equal(OtherJpeg(), Assert.IsType<ImageBlock>(doc2.Blocks[1]).RawBytes);
    }

    [Fact]
    public void LegacyV1Document_WithInlineBase64_StillLoads()
    {
        string payload = System.Convert.ToBase64String(FakeJpeg());
        string legacy = "{\"Version\":1,\"Blocks\":[{\"Type\":\"Image\",\"ImageBase64\":\"" + payload + "\",\"MimeType\":\"image/jpeg\",\"Width\":10,\"Height\":10}]}";
        var doc = DocumentSerializer.Deserialize(legacy);
        var ib = Assert.IsType<ImageBlock>(doc.Blocks[0]);
        Assert.Equal(FakeJpeg(), ib.RawBytes);
        Assert.Equal("image/jpeg", ib.MimeType);
    }

    [Fact]
    public void TableCellImages_AlsoUseThePool()
    {
        var doc = new FlowDocument();
        var ib = new ImageBlock(); ib.SetImageData(FakeJpeg(), "image/jpeg");
        doc.Blocks.Add(ib);
        var tb = new TableBlock(1, 1);
        var im = new InlineImage(); im.SetImageData(FakeJpeg(), "image/jpeg");
        tb.Cells[0][0].Inlines.Add(im);
        doc.Blocks.Add(tb);

        string json = DocumentSerializer.Serialize(doc);
        string payload = System.Convert.ToBase64String(FakeJpeg());
        Assert.Single(Regex.Matches(json, Regex.Escape(payload)));

        var doc2 = DocumentSerializer.Deserialize(json);
        // Cell paragraphs are seeded with an empty Run, so pick the image by type.
        var im2 = Assert.Single(Assert.IsType<TableBlock>(doc2.Blocks[1]).Cells[0][0].Inlines.OfType<InlineImage>());
        Assert.Equal(FakeJpeg(), im2.RawBytes);
    }
}
