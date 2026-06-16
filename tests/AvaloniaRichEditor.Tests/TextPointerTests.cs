using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Characterization of TextPointer.CompareTo's document-order semantics, locked before B2 merges its
// two full-document traversals into a single early-exiting pass (same ordering, fewer walks).
public class TextPointerTests
{
    private static TableBlock Table2x2(out Paragraph c00, out Paragraph c01, out Paragraph c10, out Paragraph c11)
    {
        var tb = new TableBlock(2, 2);
        c00 = tb.Cells[0][0]; c01 = tb.Cells[0][1];
        c10 = tb.Cells[1][0]; c11 = tb.Cells[1][1];
        return tb;
    }

    private static void Parent(FlowDocument doc)
    {
        // Mirror RichEditor.UpdateParents so CompareTo can walk cell -> table -> document.
        foreach (var b in doc.Blocks)
        {
            b.Parent = doc;
            if (b is Paragraph p) foreach (var i in p.Inlines) i.Parent = p;
            else if (b is TableBlock tb)
                for (int r = 0; r < tb.Rows; r++)
                    for (int c = 0; c < tb.Columns; c++)
                        tb.Cells[r][c].Parent = tb;
        }
    }

    [Fact]
    public void SameParagraph_ComparesByOffset()
    {
        var p = TestHelpers.Para(new Run { Text = "abcd" });
        Assert.True(new TextPointer(p, 1).CompareTo(new TextPointer(p, 3)) < 0);
        Assert.True(new TextPointer(p, 3).CompareTo(new TextPointer(p, 1)) > 0);
        Assert.Equal(0, new TextPointer(p, 2).CompareTo(new TextPointer(p, 2)));
    }

    [Fact]
    public void EarlierParagraph_IsLess_AndReversedIsGreater()
    {
        var p1 = TestHelpers.Para(new Run { Text = "one" });
        var p2 = TestHelpers.Para(new Run { Text = "two" });
        var doc = TestHelpers.Doc(p1, p2);
        Assert.NotNull(doc);

        Assert.True(new TextPointer(p1, 0).CompareTo(new TextPointer(p2, 0)) < 0);
        Assert.True(new TextPointer(p2, 0).CompareTo(new TextPointer(p1, 0)) > 0);
    }

    [Fact]
    public void AcrossTableCells_FollowsRowMajorDocumentOrder()
    {
        var before = TestHelpers.Para(new Run { Text = "before" });
        var tb = Table2x2(out var c00, out var c01, out var c10, out var c11);
        var after = TestHelpers.Para(new Run { Text = "after" });
        var doc = new FlowDocument();
        doc.Blocks.Add(before); doc.Blocks.Add(tb); doc.Blocks.Add(after);
        Parent(doc);

        // before < c00 < c01 < c10 < c11 < after
        Assert.True(new TextPointer(before, 0).CompareTo(new TextPointer(c00, 0)) < 0);
        Assert.True(new TextPointer(c00, 0).CompareTo(new TextPointer(c01, 0)) < 0);
        Assert.True(new TextPointer(c01, 0).CompareTo(new TextPointer(c10, 0)) < 0);
        Assert.True(new TextPointer(c10, 0).CompareTo(new TextPointer(c11, 0)) < 0);
        Assert.True(new TextPointer(c11, 0).CompareTo(new TextPointer(after, 0)) < 0);
        // and the long-range comparison is consistent
        Assert.True(new TextPointer(after, 0).CompareTo(new TextPointer(before, 0)) > 0);
    }

    [Fact]
    public void Null_ComparesGreater()
    {
        var p = TestHelpers.Para(new Run { Text = "x" });
        Assert.Equal(1, new TextPointer(p, 0).CompareTo(null));
    }
}
