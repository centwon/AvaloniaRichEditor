using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

public class TableBlockTests
{
    [Fact]
    public void Merge_SetsAnchorSpan_AndCoversOthers()
    {
        var tb = new TableBlock(2, 2);
        tb.MergeCells(0, 0, 1, 1);

        Assert.Equal((2, 2), tb.SpanOf(0, 0));
        Assert.False(tb.IsCovered(0, 0));
        Assert.True(tb.IsCovered(0, 1));
        Assert.True(tb.IsCovered(1, 0));
        Assert.True(tb.IsCovered(1, 1));
        Assert.Equal((0, 0), tb.AnchorOf(1, 1));
    }

    [Fact]
    public void Unmerge_RestoresPlainCells()
    {
        var tb = new TableBlock(2, 2);
        tb.MergeCells(0, 0, 1, 1);
        tb.UnmergeCell(0, 0);

        Assert.Equal((1, 1), tb.SpanOf(0, 0));
        Assert.False(tb.IsCovered(1, 1));
    }

    [Fact]
    public void Merge_AppendsCoveredCellText_IntoAnchor()
    {
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Para.Inlines.Clear();
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "A" });
        tb.Cells[0][1].Para.Inlines.Clear();
        tb.Cells[0][1].Para.Inlines.Add(new Run { Text = "B" });

        tb.MergeCells(0, 0, 0, 1);

        Assert.Contains("A", tb.Cells[0][0].Para.Text());
        Assert.Contains("B", tb.Cells[0][0].Para.Text());
    }

    [Fact]
    public void InsertColumn_GrowsEveryRow()
    {
        var tb = new TableBlock(2, 2);
        tb.InsertColumn(1);

        Assert.Equal(3, tb.Columns);
        Assert.All(tb.Cells, row => Assert.Equal(3, row.Count));
    }

    [Fact]
    public void DeleteRow_KeepsGridRectangular()
    {
        var tb = new TableBlock(3, 2);
        tb.DeleteRow(1);

        Assert.Equal(2, tb.Rows);
        Assert.Equal(2, tb.Cells.Count);
        Assert.All(tb.Cells, row => Assert.Equal(2, row.Count));
    }

    [Fact]
    public void DeleteRow_DoesNotDropLastRow()
    {
        var tb = new TableBlock(1, 1);
        tb.DeleteRow(0);
        Assert.Equal(1, tb.Rows); // refuses to delete the only row
    }
}
