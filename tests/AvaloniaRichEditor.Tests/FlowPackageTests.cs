using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// .flow package (roadmap N6-7): ZIP with document.json + images/<sha256> entries. Image bytes are
// stored raw (no base64, no deflate) and deduplicated by hash; the document round-trips losslessly
// against the JSON serializer.
public class FlowPackageTests
{
    private static byte[] FakeJpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02, 0x03, 0x04 };

    private static FlowDocument SampleDoc()
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "Hello ", FontWeight = FontWeight.Bold, Foreground = Brushes.Red });
        p.Inlines.Add(new Run { Text = "flow" });
        var icon = new InlineImage { Width = 20, Height = 20 };
        icon.SetImageData(FakeJpeg(), "image/jpeg");
        p.Inlines.Add(icon);
        doc.Blocks.Add(p);
        var b1 = new ImageBlock { Width = 100, Height = 50 };
        b1.SetImageData(FakeJpeg(), "image/jpeg"); // same bytes as the icon -> one pool entry
        doc.Blocks.Add(b1);
        var tb = new TableBlock(2, 2);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "cell" });
        doc.Blocks.Add(tb);
        return doc;
    }

    private static MemoryStream SaveToStream(FlowDocument doc)
    {
        var ms = new MemoryStream();
        DocumentPackage.Save(doc, ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void RoundTrip_MatchesJsonSerialization()
    {
        var doc = SampleDoc();
        using var ms = SaveToStream(doc);
        var doc2 = DocumentPackage.Load(ms);
        Assert.Equal(DocumentSerializer.Serialize(doc), DocumentSerializer.Serialize(doc2));
    }

    [Fact]
    public void RoundTrip_RestoresImageBytesAndMime()
    {
        using var ms = SaveToStream(SampleDoc());
        var doc2 = DocumentPackage.Load(ms);
        var ib = Assert.Single(doc2.Blocks.OfType<ImageBlock>());
        Assert.Equal(FakeJpeg(), ib.RawBytes);
        Assert.Equal("image/jpeg", ib.MimeType);
        var icon = Assert.Single(Assert.IsType<Paragraph>(doc2.Blocks[0]).Inlines.OfType<InlineImage>());
        Assert.Same(ib.RawBytes, icon.RawBytes); // shared pool byte[] like the JSON path
    }

    [Fact]
    public void Package_StoresImagesOnce_Raw_NoBase64()
    {
        using var ms = SaveToStream(SampleDoc());
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);

        var imageEntries = zip.Entries.Where(e => e.FullName.StartsWith("images/", StringComparison.Ordinal)).ToList();
        var entry = Assert.Single(imageEntries); // duplicate bytes -> one entry
        Assert.Equal(FakeJpeg().Length, entry.Length);
        Assert.Equal(entry.Length, entry.CompressedLength); // Stored, not deflated

        var docEntry = zip.GetEntry("document.json")!;
        using var reader = new StreamReader(docEntry.Open());
        string json = reader.ReadToEnd();
        Assert.DoesNotContain(Convert.ToBase64String(FakeJpeg()), json); // bytes live in the entry only
        Assert.Contains("\"ImageRef\"", json);
    }

    [Fact]
    public void Load_GarbageStream_ReturnsEmptyDocument()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var doc = DocumentPackage.Load(ms);
        Assert.NotNull(doc);
        Assert.Empty(doc.Blocks);
    }

    [Fact]
    public void Package_IsSmallerThanJson_ForImageDocuments()
    {
        // The point of .flow: dropping the ~33% base64 overhead on image bytes.
        var doc = new FlowDocument();
        var big = new byte[30_000];
        new Random(1).NextBytes(big);
        big[0] = 0xFF; big[1] = 0xD8; // jpeg magic so the mime sniffs consistently
        var ib = new ImageBlock { Width = 10, Height = 10 };
        ib.SetImageData(big, "image/jpeg");
        doc.Blocks.Add(ib);

        using var ms = SaveToStream(doc);
        long jsonSize = System.Text.Encoding.UTF8.GetByteCount(DocumentSerializer.Serialize(doc));
        Assert.True(ms.Length < jsonSize, $"flow={ms.Length} should be < json={jsonSize}");
    }
}
