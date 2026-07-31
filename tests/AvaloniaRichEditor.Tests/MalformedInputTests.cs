using System;
using System.IO;
using System.Linq;
using System.Text;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Every importer is reachable from a paste, so all of them take input the editor did not write:
// another application's RTF, a web page's HTML, a hand-edited .flow. None may crash, hang, or allocate
// without bound. Added for the 1.0 sweep — a bad paste taking the host process down is the worst
// first impression a control can make.
public class MalformedInputTests
{
    private static void NoThrow(Action a)
    {
        var ex = Record.Exception(a);
        Assert.Null(ex);
    }

    // ---- RTF ----------------------------------------------------------------

    [Theory]
    [InlineData("")]                                   // empty
    [InlineData("not rtf at all")]                     // no header
    [InlineData(@"{\rtf1")]                            // truncated immediately
    [InlineData(@"{\rtf1\ansi{{{{{{")]                 // unbalanced open braces
    [InlineData(@"{\rtf1\ansi}}}}}}")]                 // unbalanced close braces
    [InlineData(@"{\rtf1\ansi香9999999999?}")]     // \u parameter overflows int
    [InlineData(@"{\rtf1\ansi\u-2147483648?}")]        // int.MinValue
    [InlineData(@"{\rtf1\ansi\fs-5 x}")]               // negative font size
    [InlineData(@"{\rtf1\ansi\cellx}")]                // \cellx with no parameter, no \trowd
    [InlineData(@"{\rtf1\ansi\cell\row}")]             // row structure with no definition
    [InlineData(@"{\rtf1\ansi\nestcell\nestrow}")]     // nested-table words with no table
    [InlineData(@"{\rtf1\ansi\itap9999 x\nestcell}")]  // absurd nesting depth
    [InlineData(@"{\rtf1\ansi\cf9999 x}")]             // colour index past the table
    [InlineData(@"{\rtf1\ansi\clcbpat9999\cellx100 x\cell\row}")] // shading index past the table
    [InlineData(@"{\rtf1\ansi{\*\shppict{\pict\pngblip zzzz}}}")] // non-hex picture data
    [InlineData(@"{\rtf1\ansi{\*\shppict{\pict\pngblip 00}}}")]   // hex, but not an image
    [InlineData(@"{\rtf1\ansi{\*\arinline}}")]         // our marker with no table after it
    public void Rtf_MalformedInput_DoesNotThrow(string rtf)
        => NoThrow(() => RtfDocumentFormatter.Parse(rtf));

    // A merge that claims more columns/rows than the grid has must not walk off the ends.
    [Fact]
    public void Rtf_MergeFlagsPastTheGrid_DoNotThrow()
    {
        NoThrow(() => RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi\trowd\clmgf\cellx100\clmrg\cellx200 a\cell b\cell\row}"));
        // Continuation flags with no start, and a vertical continuation on the first row.
        NoThrow(() => RtfDocumentFormatter.Parse(
            @"{\rtf1\ansi\trowd\clmrg\cellx100\clvmrg\cellx200 a\cell b\cell\row}"));
    }

    // Deeply nested groups exercise the parser's own recursion/stack use.
    [Fact]
    public void Rtf_DeeplyNestedGroups_DoNotThrow()
    {
        var sb = new StringBuilder(@"{\rtf1\ansi");
        for (int i = 0; i < 2000; i++) sb.Append('{').Append(@"\b ");
        sb.Append('x');
        for (int i = 0; i < 2000; i++) sb.Append('}');
        sb.Append('}');
        NoThrow(() => RtfDocumentFormatter.Parse(sb.ToString()));
    }

    // ---- HTML ---------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("<p>unclosed")]
    [InlineData("<table><tr><td>no closing tags")]
    [InlineData("<td>orphan cell</td>")]
    [InlineData("<table></table>")]                                  // no rows
    [InlineData("<table><tr></tr></table>")]                         // no cells
    [InlineData("<p style=\"font-weight:\">empty declaration</p>")]
    [InlineData("<p style=\"font-size:abc\">bad size</p>")]
    [InlineData("<p style=\"color:notacolour\">bad colour</p>")]
    [InlineData("<img src=\"data:image/png;base64,!!!notbase64!!!\"/>")]
    [InlineData("<img src=\"\"/>")]
    [InlineData("<a href=\"javascript:alert(1)\">script url</a>")]
    public void Html_MalformedInput_DoesNotThrow(string html)
        => NoThrow(() => HtmlDocumentFormatter.ParseHtml(html));

    // A colspan/rowspan big enough to be a memory bomb must not be honoured literally.
    [Fact]
    public void Html_AbsurdSpans_DoNotAllocateWithoutBound()
    {
        var doc = HtmlDocumentFormatter.ParseHtml(
            "<table><tr><td colspan=\"100000000\" rowspan=\"100000000\">x</td></tr></table>");
        var tb = doc.Blocks.OfType<TableBlock>().FirstOrDefault();
        if (tb != null)
        {
            Assert.InRange(tb.Columns, 0, 1000);
            Assert.InRange(tb.Rows, 0, 1000);
        }
    }

    [Fact]
    public void Html_DeeplyNestedTags_DoNotThrow()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++) sb.Append("<div>");
        sb.Append("deep");
        for (int i = 0; i < 500; i++) sb.Append("</div>");
        NoThrow(() => HtmlDocumentFormatter.ParseHtml(sb.ToString()));
    }

    [Fact]
    public void Html_DeeplyNestedTables_DoNotThrow()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++) sb.Append("<table><tr><td>");
        sb.Append("deep");
        for (int i = 0; i < 100; i++) sb.Append("</td></tr></table>");
        NoThrow(() => HtmlDocumentFormatter.ParseHtml(sb.ToString()));
    }

    // ---- JSON ---------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"Blocks\":null}")]
    [InlineData("{\"Blocks\":[null]}")]
    [InlineData("{\"Blocks\":[{\"Type\":\"Nonsense\"}]}")]
    [InlineData("{\"Blocks\":[{\"Type\":\"Paragraph\",\"Inlines\":null}]}")]
    [InlineData("{\"Blocks\":[{\"Type\":\"Paragraph\",\"Inlines\":[{\"Type\":\"Run\",\"Text\":null}]}]}")]
    [InlineData("{\"Blocks\":[{\"Type\":\"Table\",\"Rows\":null}]}")]
    [InlineData("{\"Version\":{}}")]
    public void Json_MalformedInput_FailsAsAParseErrorAndNothingElse(string json)
    {
        // A JsonException is the contract; anything else (a NullReferenceException from a JSON null
        // inside "Blocks", say) is a crash the host could not have anticipated.
        var ex = Record.Exception(() => DocumentSerializer.Deserialize(json));
        Assert.True(ex is null or System.Text.Json.JsonException,
            $"unexpected exception type: {ex?.GetType().FullName}: {ex?.Message}");
    }

    // A declared table size big enough to be a memory bomb must not be allocated.
    [Fact]
    public void Json_AbsurdTableSize_DoesNotAllocateWithoutBound()
    {
        string json = "{\"Blocks\":[{\"Type\":\"Table\",\"Rows\":100000000,\"Columns\":100000000,\"Cells\":[]}]}";
        var ex = Record.Exception(() =>
        {
            var doc = DocumentSerializer.Deserialize(json);
            var tb = doc.Blocks.OfType<TableBlock>().FirstOrDefault();
            if (tb != null)
            {
                Assert.InRange(tb.Rows, 0, 10000);
                Assert.InRange(tb.Columns, 0, 10000);
            }
        });
        Assert.True(ex is null or System.Text.Json.JsonException,
            $"unexpected exception type: {ex?.GetType().FullName}: {ex?.Message}");
    }

    // ---- .flow package ------------------------------------------------------

    // Contract, settled at the 1.0 freeze (2026-07-31): a damaged document is REPORTED, never read as
    // an empty one. Both loaders used to be documented as returning an empty document; only the package
    // reader actually did, and that is the outcome that loses data — the host cannot tell "empty" from
    // "damaged", shows a blank page, and a save overwrites a recoverable file with nothing.

    [Fact]
    public void FlowPackage_NotAZip_Throws()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file"));
        Assert.Throws<InvalidDataException>(() => DocumentPackage.Load(ms));
    }

    // A package carrying no document.json is NOT damaged — it reads as an empty document.
    [Fact]
    public void FlowPackage_ZipWithoutDocument_ReturnsAnEmptyDocument()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            zip.CreateEntry("unrelated.txt");
        ms.Position = 0;
        var doc = DocumentPackage.Load(ms);
        Assert.NotNull(doc);
    }

    [Fact]
    public void Json_MalformedInput_Throws()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => DocumentSerializer.Deserialize("not json"));
        Assert.Throws<System.Text.Json.JsonException>(() => DocumentSerializer.Deserialize("{"));
        Assert.Throws<System.Text.Json.JsonException>(() => DocumentSerializer.Deserialize(""));
    }

    // A literal JSON null is valid JSON, so it is an empty document rather than an error.
    [Fact]
    public void Json_LiteralNull_IsAnEmptyDocument()
    {
        var doc = DocumentSerializer.Deserialize("null");
        Assert.NotNull(doc);
    }

    // A well-formed package still round-trips, so the stricter contract costs nothing in the good case.
    [Fact]
    public void FlowPackage_WellFormed_StillLoads()
    {
        var src = new FlowDocument();
        src.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "ok" } } });
        using var ms = new MemoryStream();
        DocumentPackage.Save(src, ms);
        ms.Position = 0;
        var back = DocumentPackage.Load(ms);
        Assert.Contains(back.Blocks.OfType<Paragraph>(), p => p.Text() == "ok");
    }
}
