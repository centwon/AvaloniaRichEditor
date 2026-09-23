using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.HitTesting.cs (2026-09-23).
public class HitTestingAuditTests
{
    private static bool Inside(object? e, TableBlock tb)
    {
        for (var cur = e; cur != null; cur = (cur as TextElement)?.Parent)
            if (ReferenceEquals(cur, tb)) return true;
        return false;
    }

    // ↓ into a table checks that the point it resolved lies in that table (IsCellOf). IsCellOf walked cell
    // blocks and nested tables but not an INLINE table in a cell's paragraph, so when the caret's column met
    // one, the table was judged "not entered" and ↓ fell back to the block caret beside it.
    [AvaloniaFact]
    public void Down_IntoARowWhoseCellHoldsAnInlineTable_EntersTheTable()
    {
        var inner = new TableBlock(2, 2);
        inner.ColumnWidths[0] = inner.ColumnWidths[1] = 120;
        var outer = new TableBlock(1, 1) { MarginTop = 0 };
        outer.ColumnWidths[0] = 300;
        outer.Cells[0][0].Para.Inlines.Clear();
        outer.Cells[0][0].Para.Inlines.Add(new InlineTable { Table = inner });
        var above = new Paragraph { Inlines = { new Run { Text = "above it" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(above);
        doc.Blocks.Add(outer);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Click(new Point(5, 8));
        host.Key(Key.End); // the caret x ↓ carries: well inside the inline table's columns
        host.Render();
        // The block caret before the table — the path that asks IsCellOf (TryEnterTableRow). It keeps the
        // text caret's last x.
        typeof(RichEditor).GetField("_caretBlock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(host.Editor, outer);
        Assert.Same(outer, host.CaretBlock); // precondition

        host.Key(Key.Down);

        Assert.Null(host.CaretBlock);
        Assert.True(Inside(host.Caret.Paragraph, outer), "↓ did not enter the table");
        Assert.True(Inside(host.Caret.Paragraph, inner), "the landing point missed the inline table — the case is not exercised");
    }
}
