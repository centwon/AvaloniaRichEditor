using System;
using System.Linq;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// N6-2: images are stored as their original encoded bytes (RawBytes + MimeType); the Bitmap is
// only a lazy render cache. These tests use fake JPEG bytes — valid magic number, not decodable —
// which doubles as proof that serialization/HTML export never re-encode (re-encoding would have
// to decode first and would blow up / drop the image).
public class ImageRawBytesTests
{
    // FF D8 FF = JPEG magic; the rest is garbage no decoder accepts.
    private static byte[] FakeJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04 };

    [Fact]
    public void JsonRoundTrip_PreservesRawBytesAndMime_WithoutDecoding()
    {
        var bytes = FakeJpeg();
        var doc = new FlowDocument();
        var ib = new ImageBlock { Width = 100, Height = 50 };
        ib.SetImageData(bytes, "image/jpeg");
        doc.Blocks.Add(ib);

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));

        var ib2 = Assert.IsType<ImageBlock>(doc2.Blocks.First(b => b is ImageBlock));
        Assert.Equal(bytes, ib2.RawBytes);
        Assert.Equal("image/jpeg", ib2.MimeType);
        Assert.Equal(100, ib2.Width);
    }

    [Fact]
    public void JsonRoundTrip_InlineImage_PreservesRawBytesAndMime()
    {
        var bytes = FakeJpeg();
        var doc = new FlowDocument();
        var p = new Paragraph();
        var im = new InlineImage { Width = 20, Height = 20 };
        im.SetImageData(bytes, "image/jpeg");
        p.Inlines.Add(new Run { Text = "a" });
        p.Inlines.Add(im);
        doc.Blocks.Add(p);

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));

        var im2 = Assert.IsType<Paragraph>(doc2.Blocks[0]).Inlines.OfType<InlineImage>().Single();
        Assert.Equal(bytes, im2.RawBytes);
        Assert.Equal("image/jpeg", im2.MimeType);
    }

    [Fact]
    public void Deserialize_LegacyImageWithoutMimeType_DefaultsToPng()
    {
        // Pre-N6-2 documents always stored PNG and had no MimeType field.
        var b64 = Convert.ToBase64String(FakeJpeg());
        var json = "{\"Blocks\":[{\"Type\":\"Image\",\"ImageBase64\":\"" + b64 + "\",\"Width\":10,\"Height\":10}]}";

        var doc = DocumentSerializer.Deserialize(json);

        var ib = Assert.IsType<ImageBlock>(doc.Blocks.First(b => b is ImageBlock));
        Assert.NotNull(ib.RawBytes);
        Assert.Equal("image/png", ib.MimeType);
    }

    [Fact]
    public void ToHtml_EmitsOriginalMimeAndBytes_WithoutDecoding()
    {
        var bytes = FakeJpeg();
        var doc = new FlowDocument();
        var ib = new ImageBlock { Width = 100, Height = 50 };
        ib.SetImageData(bytes, "image/jpeg");
        doc.Blocks.Add(ib);

        var html = HtmlDocumentFormatter.ToHtml(doc);

        Assert.Contains("data:image/jpeg;base64," + Convert.ToBase64String(bytes), html);
    }

    [Fact]
    public void Clone_SharesRawBytesReference()
    {
        var bytes = FakeJpeg();
        var ib = new ImageBlock();
        ib.SetImageData(bytes, "image/jpeg");

        var clone = (ImageBlock)ib.Clone();

        Assert.Same(bytes, clone.RawBytes); // undo snapshots add no per-image memory
        Assert.Equal("image/jpeg", clone.MimeType);

        var im = new InlineImage();
        im.SetImageData(bytes, "image/jpeg");
        Assert.Same(bytes, ((InlineImage)im.Clone()).RawBytes);
    }

    [Fact]
    public void ImageSetter_DiscardsStaleRawBytes()
    {
        var ib = new ImageBlock();
        ib.SetImageData(FakeJpeg(), "image/jpeg");

        ib.Image = null; // direct bitmap assignment invalidates the encoded bytes

        Assert.Null(ib.RawBytes);
        Assert.Null(ib.MimeType);
    }

    // A2: a failed decode returns null without throwing AND keeps the encoded bytes. These [Fact] tests
    // run without the Avalonia platform, so `new Bitmap` genuinely throws (the headless platform's image
    // loader would instead stub a 1x1 bitmap) — the only way to exercise the decode-failure branch.
    [Fact]
    public void Image_UndecodableBytes_ReturnsNullButKeepsRawBytes()
    {
        var bytes = FakeJpeg();
        var ib = new ImageBlock();
        ib.SetImageData(bytes, "image/jpeg");

        Assert.Null(ib.Image);            // lazy decode fails gracefully (no throw)
        Assert.Same(bytes, ib.RawBytes);  // bytes are KEPT so a later save still round-trips the picture
        Assert.Equal("image/jpeg", ib.MimeType);
        Assert.Null(ib.Image);            // a second access doesn't retry the decode and doesn't drop them
        Assert.Same(bytes, ib.RawBytes);
    }

    [Fact]
    public void InlineImage_UndecodableBytes_ReturnsNullButKeepsRawBytes()
    {
        var bytes = FakeJpeg();
        var im = new InlineImage();
        im.SetImageData(bytes, "image/jpeg");

        Assert.Null(im.Image);
        Assert.Same(bytes, im.RawBytes);
        Assert.Equal("image/jpeg", im.MimeType);
    }

    [Fact]
    public void MimeDetect_RecognizesCommonFormats()
    {
        Assert.Equal("image/jpeg", ImageMime.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.Equal("image/png", ImageMime.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        Assert.Equal("image/gif", ImageMime.Detect(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8' }));
        Assert.Equal("image/png", ImageMime.Detect(new byte[] { 0x00, 0x01 })); // unknown -> png fallback
    }
}
