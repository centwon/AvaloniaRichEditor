using System.Linq;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using System.Text.Json;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Data-loss and crash fixes added for 1.3.0 (2026-10-15), each with the case that showed it.
public class DataLossFixesTests
{
    // A number past the range of a double read as infinity, which JSON cannot spell — the document
    // opened, and every save of it threw. Refused on read now, like any other value of the wrong type.
    [Theory]
    [InlineData("{\"Version\":\"1.0\",\"Blocks\":[{\"Inlines\":[{\"Text\":\"a\",\"FontSize\":1e400}]}]}")]
    [InlineData("{\"Version\":\"1.0\",\"Blocks\":[{\"Indent\":-1e400,\"Inlines\":[{\"Text\":\"a\"}]}]}")]
    [InlineData("{\"Version\":1e400,\"Blocks\":[]}")]
    public void ANumberPastTheRangeOfADouble_IsRefusedOnRead(string json)
        => Assert.ThrowsAny<JsonException>(() => DocumentSerializer.Deserialize(json));

    [Fact]
    public void ALargeButFiniteNumber_StillReadsAndSaves()
    {
        var doc = DocumentSerializer.Deserialize("{\"Version\":\"1.0\",\"Blocks\":[{\"Indent\":1e300,\"Inlines\":[{\"Text\":\"a\"}]}]}");
        Assert.Contains("1E+300", DocumentSerializer.Serialize(doc));
    }

    // ---- HTML with an inline table inside a formatted paragraph ----

    private static FlowDocument Cycle(FlowDocument d) => HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(d));

    private static InlineTable CellTable()
    {
        // A cell paragraph with its own formatting goes out as a <p> element: block content INSIDE the inline table.
        var t = new TableBlock(1, 1);
        var cp = new Paragraph { TextAlignment = TextAlignment.Right };
        cp.Inlines.Add(new Run { Text = "x" });
        t.Cells[0][0].Blocks.Add(cp);
        return new InlineTable { Table = t };
    }

    // An <h1> (or any formatted <p>) holding one of our inline tables was walked as a container of blocks — the table,
    // and the <p> in its cell, counted as block children — so the paragraph's heading level and alignment were dropped
    // and the next cycle wrote it as a plain <p> (fuzz seeds 1008 and 4857).
    [Fact]
    public void AHeadingHoldingAnInlineTable_KeepsItsParagraphFormatting()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        var h = new Paragraph { HeadingLevel = 1, TextAlignment = TextAlignment.Center };
        h.Inlines.Add(new Run { Text = "Title" });
        h.Inlines.Add(CellTable());
        doc.Blocks.Add(h);

        foreach (var back in new[] { Cycle(doc), Cycle(Cycle(doc)) })
        {
            var p = Assert.Single(back.Blocks.OfType<Paragraph>());
            Assert.Equal((1, TextAlignment.Center), (p.HeadingLevel, p.TextAlignment));
            Assert.Single(p.Inlines.OfType<InlineTable>());
        }
    }

    // An empty run ahead of the table writes nothing, so the table still OPENS its paragraph; counting the run left the
    // table unmarked and the reader moved it up into the paragraph above (fuzz seed 1008).
    [Fact]
    public void AnInlineTableAfterAnEmptyRun_StaysInItsOwnParagraph()
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        var above = new Paragraph();
        above.Inlines.Add(new Run { Text = "above" });
        doc.Blocks.Add(above);
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "" });
        host.Inlines.Add(CellTable());
        doc.Blocks.Add(host);

        var paras = Cycle(doc).Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(2, paras.Count);
        Assert.Empty(paras[0].Inlines.OfType<InlineTable>());
        Assert.Single(paras[1].Inlines.OfType<InlineTable>());
    }
}
