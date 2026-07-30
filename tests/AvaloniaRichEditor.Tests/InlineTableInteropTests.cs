using System.Linq;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// HTML has no inline table, so an InlineTable went out as a <table> and came back as a BLOCK table —
// saving and reloading a document silently split every paragraph that held one. Our own export now marks
// those tables so the import can put them back on the text line; foreign HTML is unaffected.
public class InlineTableInteropTests
{
    private static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    // "before [1x1 inline table] after" in a single paragraph.
    private static FlowDocument HostParagraph(string cellText = "x")
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "before " });
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = cellText;
        p.Inlines.Add(it);
        p.Inlines.Add(new Run { Text = " after" });
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        return doc;
    }

    private static FlowDocument RoundTrip(FlowDocument doc)
        => HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(doc));

    [Fact]
    public void AnInlineTableStaysInlineThroughHtml()
    {
        var back = RoundTrip(HostParagraph("cell"));

        var para = Assert.IsType<Paragraph>(Assert.Single(back.Blocks));
        var it = Assert.Single(para.Inlines.OfType<InlineTable>());
        Assert.Equal("cell", Text(it.Table.Cells[0][0].Para));
        Assert.Equal("before  after", Text(para)); // the surrounding text is one paragraph again
    }

    // The host paragraph's text has to keep its order around the table, not be re-joined after it.
    [Fact]
    public void TheTableComesBackBetweenTheTextThatSurroundedIt()
    {
        var back = RoundTrip(HostParagraph());

        var para = (Paragraph)back.Blocks[0];
        int tableAt = para.Inlines.ToList().FindIndex(i => i is InlineTable);
        Assert.Equal(1, tableAt); // after "before ", before " after"
        Assert.Equal(3, para.Inlines.Count);
    }

    // A plain block table must not be dragged into a paragraph by the new path.
    [Fact]
    public void ABlockTableStillRoundTripsAsABlock()
    {
        var doc = new FlowDocument();
        var tb = new TableBlock(2, 2);
        ((Run)tb.Cells[0][0].Para.Inlines[0]).Text = "A1";
        doc.Blocks.Add(tb);

        var back = RoundTrip(doc);

        var t = Assert.Single(back.Blocks.OfType<TableBlock>());
        Assert.Equal(2, t.Rows);
        Assert.Empty(back.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineTable>()));
    }

    // Foreign HTML carries no marker, so it keeps landing as a block table (Word/Excel paste behaviour).
    [Fact]
    public void ForeignHtmlTableIsStillABlockTable()
    {
        var doc = HtmlDocumentFormatter.ParseHtml("<p>text</p><table><tr><td>a</td></tr></table>");

        Assert.Single(doc.Blocks.OfType<TableBlock>());
        Assert.Empty(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineTable>()));
    }

    // An inline table inside a table cell: the marker has to survive the recursive cell emit too.
    [Fact]
    public void AnInlineTableInsideACellStaysInline()
    {
        var doc = new FlowDocument();
        var outer = new TableBlock(1, 1);
        var hostPara = outer.Cells[0][0].Para;
        ((Run)hostPara.Inlines[0]).Text = "host ";
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = "deep";
        hostPara.Inlines.Add(it);
        doc.Blocks.Add(outer);

        var back = RoundTrip(doc);

        var cell = Assert.Single(back.Blocks.OfType<TableBlock>()).Cells[0][0];
        var para = Assert.Single(cell.Blocks.OfType<Paragraph>());
        var inner = Assert.Single(para.Inlines.OfType<InlineTable>());
        Assert.Equal("deep", Text(inner.Table.Cells[0][0].Para));
    }

    // Two inline tables in one paragraph must both come back, in order.
    [Fact]
    public void TwoInlineTablesBothComeBack()
    {
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "a" });
        foreach (var t in new[] { "one", "two" })
        {
            var it = new InlineTable { Table = new TableBlock(1, 1) };
            ((Run)it.Table.Cells[0][0].Para.Inlines[0]).Text = t;
            p.Inlines.Add(it);
            p.Inlines.Add(new Run { Text = "b" });
        }
        var doc = new FlowDocument();
        doc.Blocks.Add(p);

        var back = RoundTrip(doc);

        var para = Assert.IsType<Paragraph>(Assert.Single(back.Blocks));
        var tables = para.Inlines.OfType<InlineTable>().ToList();
        Assert.Equal(2, tables.Count);
        Assert.Equal("one", Text(tables[0].Table.Cells[0][0].Para));
        Assert.Equal("two", Text(tables[1].Table.Cells[0][0].Para));
    }
}
