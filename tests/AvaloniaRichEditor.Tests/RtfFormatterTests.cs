using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RtfDocumentFormatter: parses the "Rich Text Format" Word/HWP put on the clipboard. Plain string
// fixtures (no real clipboard needed) cover paragraphs, character formatting, colour, and structure.
public class RtfFormatterTests
{
    private static FlowDocument Parse(string rtf) => RtfDocumentFormatter.Parse(rtf);

    [Fact]
    public void LooksLikeRtf_OnlyForRtfSignature()
    {
        Assert.True(RtfDocumentFormatter.LooksLikeRtf(@"{\rtf1\ansi hello}"));
        Assert.True(RtfDocumentFormatter.LooksLikeRtf("  {\\rtf1 x}"));
        Assert.False(RtfDocumentFormatter.LooksLikeRtf("<p>html</p>"));
        Assert.False(RtfDocumentFormatter.LooksLikeRtf(null));
    }

    [Fact]
    public void Paragraphs_SplitOnPar()
    {
        var doc = Parse(@"{\rtf1\ansi first\par second\par}");
        Assert.Equal("first", ((Paragraph)doc.Blocks[0]).Text());
        Assert.Equal("second", ((Paragraph)doc.Blocks[1]).Text());
    }

    [Fact]
    public void CharacterFormatting_BoldItalicUnderline()
    {
        var doc = Parse(@"{\rtf1\ansi normal \b bold\b0  \i italic\i0  \ul under\ulnone done\par}");
        var runs = ((Paragraph)doc.Blocks[0]).Inlines.OfType<Run>().ToList();
        string T(Run r) => r.Text ?? "";
        Assert.Contains(runs, r => T(r).Contains("bold") && r.FontWeight == FontWeight.Bold);
        Assert.Contains(runs, r => T(r).Contains("italic") && r.FontStyle == FontStyle.Italic);
        Assert.Contains(runs, r => T(r).Contains("under")
            && r.TextDecorations != null && r.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline));
        Assert.Contains(runs, r => T(r).Contains("normal")
            && r.FontWeight == FontWeight.Normal && r.FontStyle == FontStyle.Normal);
    }

    [Fact]
    public void Foreground_FromColorTable()
    {
        // colortbl: index 0 = auto, 1 = red, 2 = blue. \cf1 -> red run.
        var doc = Parse(@"{\rtf1\ansi{\colortbl;\red255\green0\blue0;\red0\green0\blue255;}\cf1 red\cf2 blue\par}");
        var runs = ((Paragraph)doc.Blocks[0]).Inlines.OfType<Run>().ToList();
        var red = runs.First(r => (r.Text ?? "").Contains("red"));
        Assert.Equal(Colors.Red, ((ISolidColorBrush)red.Foreground!).Color);
        var blue = runs.First(r => (r.Text ?? "").Contains("blue"));
        Assert.Equal(Colors.Blue, ((ISolidColorBrush)blue.Foreground!).Color);
    }

    [Fact]
    public void FontSize_HalfPoints()
    {
        var doc = Parse(@"{\rtf1\ansi\fs40 big\par}"); // \fs40 -> 20pt
        var run = ((Paragraph)doc.Blocks[0]).Inlines.OfType<Run>().First();
        Assert.Equal(20, run.FontSize);
    }

    [Fact]
    public void IgnorableGroupsAndFontTable_AreSkipped()
    {
        var doc = Parse(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}{\*\generator Foo;}hello\par}");
        Assert.Single(doc.Blocks);
        Assert.Equal("hello", ((Paragraph)doc.Blocks[0]).Text());
    }

    private const string Png1x1Base64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Pict_PngBlip_BecomesInlineImage()
    {
        string hex = System.Convert.ToHexString(System.Convert.FromBase64String(Png1x1Base64));
        string rtf = "{\\rtf1\\ansi text {\\pict\\pngblip\\picwgoal300\\pichgoal300 " + hex + "}more\\par}";
        var doc = Parse(rtf);
        int images = doc.Blocks.OfType<Paragraph>().Sum(p => p.Inlines.OfType<InlineImage>().Count());
        Assert.Equal(1, images);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Pict_ShpPictWrapper_PicksPngOverWmfFallback()
    {
        string hex = System.Convert.ToHexString(System.Convert.FromBase64String(Png1x1Base64));
        // Word emits the modern PNG in \*\shppict and a WMF fallback in \nonshppict — take the PNG only.
        string rtf = "{\\rtf1\\ansi {\\*\\shppict{\\pict\\pngblip\\picwgoal300\\pichgoal300 " + hex + "}}" +
                     "{\\nonshppict{\\pict\\wmetafile8\\picwgoal300\\pichgoal300 deadbeefdeadbeef}}\\par}";
        var doc = Parse(rtf);
        int images = doc.Blocks.OfType<Paragraph>().Sum(p => p.Inlines.OfType<InlineImage>().Count())
                   + doc.Blocks.OfType<ImageBlock>().Count();
        Assert.Equal(1, images);
    }

    [Fact]
    public void Table_RowsCellsAndTrailingParagraph()
    {
        var doc = Parse(@"{\rtf1\ansi\trowd\cellx2000\cellx4000 A\cell B\cell\row\trowd\cellx2000\cellx4000 C\cell D\cell\row\pard after\par}");
        var tb = doc.Blocks.OfType<TableBlock>().First();
        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Columns);
        Assert.Equal("A", tb.Cells[0][0].Para.Text());
        Assert.Equal("B", tb.Cells[0][1].Para.Text());
        Assert.Equal("D", tb.Cells[1][1].Para.Text());
        Assert.Contains(doc.Blocks.OfType<Paragraph>(), p => p.Text().Contains("after"));
        // \cellx2000 / \cellx4000 → 2000 twips (≈133px) then 2000 more (≈133px), not the uniform default.
        Assert.Equal(2000 / 15.0, tb.ColumnWidths[0], 1);
        Assert.Equal(2000 / 15.0, tb.ColumnWidths[1], 1);
    }

    [Fact]
    public void TextBox_ExtractsShpTxtAndSkipsShapeProps()
    {
        // \shptxt content is pulled out as text; the {\sp{\sn name}{\sv value}} property groups don't leak.
        var doc = Parse(@"{\rtf1\ansi{\shp{\*\shpinst{\sp{\sn shapeType}{\sv 202}}{\shptxt boxed text\par}}}after\par}");
        var text = string.Join("\n", doc.Blocks.OfType<Paragraph>().Select(p => p.Text()));
        Assert.Contains("boxed text", text);
        Assert.Contains("after", text);
        Assert.DoesNotContain("shapeType", text);
        Assert.DoesNotContain("202", text);
    }

    [Fact]
    public void NestedTable_FlattensIntoParentCell()
    {
        // The nested cells/rows can't nest in the model, so they flatten into the parent cell text.
        var doc = Parse("{\\rtf1\\ansi\\trowd\\cellx2000 outer \\nestcell inner1\\nestcell inner2\\nestrow\\cell\\row\\pard x\\par}");
        var tb = doc.Blocks.OfType<TableBlock>().First();
        var cell = tb.Cells[0][0].Para.Text();
        Assert.Contains("outer", cell);
        Assert.Contains("inner1", cell);
        Assert.Contains("inner2", cell);
    }

    [Fact]
    public void HexEscapes_DecodeWithAnsiCodepage_Korean()
    {
        // HWP emits Korean as \'hh byte pairs in the document code page (CP949), not \uN. Encode
        // "한글" to CP949 and feed it back as \'hh escapes — it must round-trip.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var bytes = System.Text.Encoding.GetEncoding(949).GetBytes("한글");
        var sb = new System.Text.StringBuilder("{\\rtf1\\ansi\\ansicpg949 ");
        foreach (var b in bytes) sb.Append("\\'").Append(b.ToString("x2"));
        sb.Append("\\par}");
        var doc = Parse(sb.ToString());
        Assert.Equal("한글", ((Paragraph)doc.Blocks[0]).Text());
    }

    [Fact]
    public void UnicodeEscapes_Decode()
    {
        // \uN is a decimal code point with one fallback char to skip. Build the control words from
        // pieces so the source carries no literal backslash-u escape.
        string Esc(char ch) => "\\" + "u" + ((int)ch) + "?";
        string rtf = "{\\rtf1\\ansi " + Esc('한') + Esc('글') + "\\par}";
        var doc = Parse(rtf);
        Assert.Equal("한글", ((Paragraph)doc.Blocks[0]).Text());
    }
}
