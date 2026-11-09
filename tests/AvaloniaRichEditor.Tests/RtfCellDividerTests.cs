using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A divider inside a table cell went out as a bottom border with no \pard after it, so in Word the border could run on into
// the cell's next paragraphs (and this reader ignores borders in a cell anyway). No per-cell rule is written, as on the
// WinUI control (2026-11-09).
public class RtfCellDividerTests
{
    [Fact]
    public void ADividerInACell_WritesNoBorder()
    {
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "above" } } });
        cell.Blocks.Add(new DividerBlock());
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "below" } } });
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(tb);

        string rtf = RtfDocumentFormatter.Write(doc);
        Assert.DoesNotContain(@"\brdrb", rtf);
        Assert.Contains("below", rtf);
    }
}
