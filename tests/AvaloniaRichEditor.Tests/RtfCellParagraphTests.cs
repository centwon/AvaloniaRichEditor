using System.Linq;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Inside a table cell \par was read as a line break: a cell's paragraphs — from Word, HWP, or this library's own RTF —
// came back as one paragraph with newlines, losing each paragraph's format. A cell is a block container, so \par makes a
// paragraph there as in the body, as on the WinUI control (2026-11-09).
public class RtfCellParagraphTests
{
    private static TableCell FirstCell(FlowDocument d) => d.Blocks.OfType<TableBlock>().First().Cells[0][0];
    private static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    [Fact]
    public void APar_InACell_StartsAParagraph()
    {
        var doc = RtfDocumentFormatter.Parse(@"{\rtf1 \trowd\cellx3000 \pard\intbl first\par second\cell\row}");
        var paras = FirstCell(doc).Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(new[] { "first", "second" }, paras.Select(Text));
    }

    [Fact]
    public void ACellsParagraphs_SurviveARoundTrip_WithTheirFormat()
    {
        var tb = new TableBlock(1, 1);
        var cell = tb.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(new Paragraph { TextAlignment = Avalonia.Media.TextAlignment.Center, Inlines = { new Run { Text = "one" } } });
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "two" } } });
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(tb);

        var back = FirstCell(RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(doc))).Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(new[] { "one", "two" }, back.Select(Text));
        Assert.Equal(Avalonia.Media.TextAlignment.Center, back[0].TextAlignment);
    }
}
