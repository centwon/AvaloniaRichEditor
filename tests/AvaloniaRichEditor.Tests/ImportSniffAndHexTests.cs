using System;
using System.Reflection;
using System.Text;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Two allocation fixes that must not change a single output byte or a single routing decision.
// Both replaced something correct-but-wasteful, so what needs pinning is the equivalence, not behaviour
// that is new: the picture's hex is written a chunk at a time instead of a string per byte, and Import
// classifies a file from its bytes instead of decoding the whole thing twice to find out what it is.
public class ImportSniffAndHexTests
{
    // ---- RTF picture hex ---------------------------------------------------

    // Values chosen to catch the ways a hand-rolled hex writer goes wrong: 0x00 and 0x0F need the
    // leading zero, 0xFF and 0xA0 need lowercase, 0x10 catches a swapped nibble.
    private static byte[] Bytes(int len)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++) b[i] = (byte)((i * 7 + (i / 251)) & 0xFF);
        byte[] corners = { 0x00, 0x0F, 0xFF, 0xA0, 0x10 };
        for (int i = 0; i < corners.Length && i < len; i++) b[i] = corners[i];
        return b;
    }

    private static string Rtf(byte[] bytes)
    {
        var doc = new FlowDocument();
        var ib = new ImageBlock { Width = 100, Height = 50 };
        ib.SetImageData(bytes, "image/png");
        doc.Blocks.Add(ib);
        return RtfDocumentFormatter.Write(doc);
    }

    // Longer than one conversion chunk, and not a multiple of it, so a boundary lands mid-picture and
    // the final partial chunk is exercised too.
    [AvaloniaFact]
    public void WritePict_EmitsExactlyTheLowercaseHexOfTheBytes()
    {
        var bytes = Bytes(1500);

        Assert.Contains(Convert.ToHexStringLower(bytes), Rtf(bytes), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void WritePict_HexIsTheSameAcrossChunkBoundaries()
    {
        foreach (int len in new[] { 1, 511, 512, 513, 1024, 1025 })
        {
            var bytes = Bytes(len);
            Assert.Contains(Convert.ToHexStringLower(bytes), Rtf(bytes), StringComparison.Ordinal);
        }
    }

    // ---- import format sniffing --------------------------------------------

    // The byte-level sniff stands in for RtfDocumentFormatter.LooksLikeRtf(string). Misclassifying here
    // is silent and total — an RTF file would be handed to the JSON loader — so the two are compared
    // directly rather than the byte one being trusted on its own.
    private static bool SniffBytes(string content, Encoding? enc = null)
    {
        var buf = (enc ?? Encoding.Latin1).GetBytes(content);
        var m = typeof(RichEditorToolbar).GetMethod("LooksLikeRtf",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)m.Invoke(null, new object[] { buf, buf.Length })!;
    }

    private static void AssertAgrees(string content, Encoding? enc = null)
        => Assert.Equal(RtfDocumentFormatter.LooksLikeRtf(content), SniffBytes(content, enc));

    [Theory]
    [InlineData("{\\rtf1\\ansi hello\\par}")]       // plain
    [InlineData("   {\\rtf1\\ansi x}")]             // leading spaces
    [InlineData("\r\n\t{\\rtf1 x}")]                // leading CRLF + tab
    [InlineData("<p>html</p>")]
    [InlineData("{\"Blocks\":[]}")]                 // JSON opens with '{' but is not the signature
    [InlineData("")]
    [InlineData("{\\rt")]                           // shorter than the signature
    [InlineData("x{\\rtf1}")]                       // signature not at the start
    public void ByteSniff_AgreesWithLooksLikeRtf(string content) => AssertAgrees(content);

    // NBSP and NEL are the two non-obvious members of the set string.TrimStart() removes below U+0100 —
    // the byte sniff has to skip exactly them and nothing else. Built from code points rather than
    // written into the source: U+0085 ENDS A LINE in C# source, and a literal U+00A0 is invisible.
    [Fact]
    public void ByteSniff_SkipsTheSameExoticWhitespace()
    {
        foreach (int cp in new[] { 0x00A0, 0x0085 })
            AssertAgrees((char)cp + "{\\rtf1 x}");
    }

    // A CJK document decodes to different CHARACTERS under Latin1 than under UTF-8, which is the whole
    // reason the importer picks an encoding per format. The signature is ASCII either way.
    [Fact]
    public void ByteSniff_IsUnaffectedByTheTextEncoding()
    {
        Assert.True(SniffBytes("{\\rtf1\\ansi 한글}", Encoding.UTF8));
        Assert.False(SniffBytes("<p>한글</p>", Encoding.UTF8));
    }

    // A BOM is not whitespace, so it does not open an RTF file — the same answer the string version
    // gives, and the reason a BOM'd file goes down the UTF-8 branch where the BOM belongs.
    [Fact]
    public void ByteSniff_BomIsNotSkipped()
    {
        string withBom = (char)0xFEFF + "{\\rtf1 x}";

        Assert.False(RtfDocumentFormatter.LooksLikeRtf(withBom)); // the premise, stated
        Assert.False(SniffBytes(withBom, Encoding.UTF8));
    }
}
