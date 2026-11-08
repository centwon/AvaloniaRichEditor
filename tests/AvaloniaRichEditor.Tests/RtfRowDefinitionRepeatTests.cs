using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The row definition (\trowd … \cellx) goes out before the row's cells and again right before \row, as Word writes it:
// strict readers — HWP among them — need it in scope at \row, and without the repeat HWP dropped the table and pasted its
// cells as plain lines. As on the WinUI control (2026-11-09). This reader reads the repeat as a no-op.
public class RtfRowDefinitionRepeatTests
{
    private static FlowDocument TwoByTwo()
    {
        var tb = new TableBlock(2, 2);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                tb.Cells[r][c].Para.Inlines.Add(new Run { Text = $"r{r}c{c}" });
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        doc.Blocks.Add(tb);
        return doc;
    }

    [Fact]
    public void EachRow_StatesItsDefinitionAgainBeforeRow()
    {
        string rtf = RtfDocumentFormatter.Write(TwoByTwo());
        Assert.Equal(4, Regex.Matches(rtf, @"\\trowd").Count);
        Assert.Equal(2, Regex.Matches(rtf, @"\\cellx\d+\s*\\row").Count);
    }

    [Fact]
    public void TheRepeat_ReadsBackAsTheSameTable()
    {
        var back = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(TwoByTwo())).Blocks.OfType<TableBlock>().Single();
        Assert.Equal(2, back.Rows);
        Assert.Equal(2, back.Columns);
        Assert.Equal("r1c1", string.Concat(back.Cells[1][1].Para.Inlines.OfType<Run>().Select(r => r.Text)));
    }
}
