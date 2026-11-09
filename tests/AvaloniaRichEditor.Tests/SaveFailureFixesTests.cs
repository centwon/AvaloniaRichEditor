using System.Linq;
using System.Text;
using System.Text.Json;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A document the readers accept must also save, and a single bad token must not empty a whole file (2026-11-08).
public class SaveFailureFixesTests
{
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    private static readonly string Huge = new('9', 400);   // parses as infinity
    private static readonly string Big = "1" + new string('0', 308); // finite, but overflows once scaled

    public static TheoryData<string> NonFiniteHtml => new()
    {
        $"<img width=\"Infinity\" height=\"10\" src=\"{Png}\">",
        $"<img width=\"1e400\" height=\"10\" src=\"{Png}\">",
        "<p data-are-m=\"Infinity,NaN,1e400\">a</p>",
        "<p style=\"line-height:1e400%\">a</p>",
        "<p style=\"line-height:Infinity\">a</p>",
        "<p style=\"line-height:1e400px\">a</p>",
        $"<p style=\"font-size:{Huge}px\">a</p>",
        $"<p style=\"font-size:{Big}em\">a</p>",
        $"<p style=\"margin-left:{Huge}px\">a</p>",
        $"<p style=\"margin-left:{Big}pt\">a</p>",
        $"<p style=\"text-indent:-{Huge}px\">a</p>",
        "<table><colgroup><col width=\"Infinity\"></colgroup><tr><td>a</td></tr></table>",
        "<table><colgroup><col width=\"1e400\"></colgroup><tr><td>a</td></tr></table>",
    };

    // HTML's double.TryParse takes "Infinity", "NaN" and 1e400; the model kept them and every later save threw.
    [Theory]
    [MemberData(nameof(NonFiniteHtml))]
    public void HtmlWithANonFiniteNumber_StillSaves(string html)
    {
        var doc = HtmlDocumentFormatter.ParseHtml(html);
        DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
    }

    private static string NestedHtml(int levels)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < levels; i++) sb.Append("<table><tr><td>");
        sb.Append('x');
        for (int i = 0; i < levels; i++) sb.Append("</td></tr></table>");
        return sb.ToString();
    }

    private static string NestedRtf(int levels)
    {
        var sb = new StringBuilder("{\\rtf1 ");
        for (int lv = levels; lv >= 1; lv--)
        {
            sb.Append($"\\itap{lv} x");
            sb.Append(lv > 1 ? "\\nestcell{\\*\\nesttableprops\\trowd\\cellx1000\\nestrow}" : "\\cell\\trowd\\cellx1000\\row");
        }
        return sb.Append('}').ToString();
    }

    private static int TableDepth(System.Collections.Generic.IEnumerable<Block> blocks)
        => blocks.OfType<TableBlock>().Select(t => 1 + t.Cells.SelectMany(row => row).Select(c => TableDepth(c.Blocks)).DefaultIfEmpty(0).Max())
            .DefaultIfEmpty(0).Max();

    // JSON's default depth of 64 holds about 12 nested tables, while the HTML reader builds up to its DOM bound
    // and the RTF reader up to 32: such a document opened, and saving it threw.
    [Theory]
    [InlineData(13)]
    [InlineData(50)] // past the DOM bound: read as deep as that bound allows
    public void DeepNestedTablesFromHtml_SaveAndReopen(int levels)
    {
        var doc = HtmlDocumentFormatter.ParseHtml(NestedHtml(levels));
        var back = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        Assert.Equal(TableDepth(doc.Blocks), TableDepth(back.Blocks));
    }

    [Theory]
    [InlineData(13)]
    [InlineData(32)]
    public void DeepNestedTablesFromRtf_SaveAndReopen(int levels)
    {
        var doc = RtfDocumentFormatter.Parse(NestedRtf(levels));
        Assert.Equal(levels, TableDepth(doc.Blocks));
        var back = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));
        Assert.Equal(levels, TableDepth(back.Blocks));
    }

    // \sl-2147483648: Math.Abs(int.MinValue) overflowed, and the reader's catch returned an empty document.
    [Fact]
    public void RtfLineSpacingOfIntMinValue_KeepsTheText()
    {
        var doc = RtfDocumentFormatter.Parse("{\\rtf1 \\pard\\sl-2147483648 hello\\par}");
        var p = Assert.Single(doc.Blocks.OfType<Paragraph>());
        Assert.Equal("hello", string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)));
        Assert.True(double.IsFinite(p.LineHeight));
    }
}
