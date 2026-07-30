using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// 1.0 gate ③: large-document performance. The real timing numbers (layout/scroll/typing latency) come
// from the demo's `--bench-text` harness on a real window — see Project_Roadmap.md for the recorded
// baseline (linear scaling, no blowup, usable to hundreds of pages). Timing assertions are too noisy
// for CI, so the only thing guarded here is the DETERMINISTIC one: managed heap must stay bounded
// (catches a memory leak or an O(n²) cache regression). The demo measured ~37 MB for 3000 paragraphs;
// the generous bound below leaves headroom while still failing on a real blowup.
public class PerformanceTests
{
    [AvaloniaFact]
    public void LargeDocument_ManagedHeapStaysBounded()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);

        var ed = new RichEditor();
        var doc = new FlowDocument();
        for (int i = 0; i < 3000; i++)
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = $"[{i}] paragraph with some shaped text content for layout", FontSize = 14 });
            p.Inlines.Add(new Run { Text = "bold tail", FontSize = 13, FontWeight = FontWeight.Bold });
            doc.Blocks.Add(p);
        }
        ed.Document = doc;
        // Force a measure pass so a TextLayout is shaped + cached for every paragraph (the real cost).
        ed.Measure(new Size(800, double.PositiveInfinity));

        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(ed); // keep the doc + caches alive across the measurement
        double mb = (after - before) / 1024.0 / 1024.0;

        Assert.True(mb < 150, $"a 3000-paragraph document + its layout caches should stay bounded, used {mb:F1} MB");
    }

    // ---- P4: nested/inline tables and the IME composition path --------------
    //
    // The demo's `--bench-table` harness carries the timings (see Project_Roadmap.md: table-heavy
    // documents stay at 60 fps with a ~0.0 ms warm re-measure, and a composition update costs
    // ~0.1 ms). Timing can't be asserted in CI, so what is guarded here is the DETERMINISTIC property
    // that keeps that number small: the per-composition eviction stays scoped to the caret's own table
    // chain instead of dropping every cached layout in the document.

    // One unit = a paragraph, a paragraph hosting an inline table, and a 2x3 table holding a nested 2x2.
    private static FlowDocument TableHeavyDoc(int units)
    {
        var doc = new FlowDocument();
        for (int i = 0; i < units; i++)
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = $"[{i}] text between the tables", FontSize = 14 });
            doc.Blocks.Add(p);

            var hostPara = new Paragraph();
            hostPara.Inlines.Add(new Run { Text = "inline: " });
            var it = new InlineTable { Table = new TableBlock(2, 2) };
            hostPara.Inlines.Add(it);
            doc.Blocks.Add(hostPara);

            var outer = new TableBlock(2, 3);
            outer.Cells[0][0].Blocks.Add(new TableBlock(2, 2));
            doc.Blocks.Add(outer);
        }
        return doc;
    }

    private static T Field<T>(RichEditor ed, string name)
        => (T)typeof(RichEditor).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ed)!;

    private static void SetPreedit(RichEditor ed, string? text)
        => typeof(RichEditor).GetMethod("SetPreedit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(ed, new object?[] { text });

    private static void PlaceCaret(RichEditor ed, Paragraph p)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, 0));
    }

    [AvaloniaFact]
    public void ComposingInACell_OnlyEvictsThatCellsTableChain()
    {
        var doc = TableHeavyDoc(20);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        ed.Measure(new Size(800, double.PositiveInfinity));

        // The cache's value tuple names an internal layout type, so count through ICollection.
        var tables = Field<System.Collections.ICollection>(ed, "_tableLayoutCache");
        int cachedBefore = tables.Count;
        Assert.True(cachedBefore > 10, $"test setup: the tables should be cached, got {cachedBefore}");

        // Compose in the first outer table's own cell: its chain is that table alone.
        var target = doc.Blocks.OfType<TableBlock>().First();
        PlaceCaret(ed, target.Cells[1][1].Para);
        SetPreedit(ed, "가나다");
        ed.Measure(new Size(800, double.PositiveInfinity));

        // Every other table's geometry must still be cached: a document-wide clear is what would make the
        // per-character cost scale with document size.
        // Measured: 60 cached tables before and after — the chain entry is evicted and immediately
        // re-cached by the measure pass, and nothing else is touched. The slack keeps the guard about the
        // scope of the eviction rather than the exact re-cache order.
        Assert.True(tables.Count >= cachedBefore - 2,
            $"composition evicted {cachedBefore - tables.Count} cached tables; it should only touch the caret's chain");
    }

    [AvaloniaFact]
    public void TableHeavyDocument_ManagedHeapStaysBounded()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);

        var ed = new RichEditor { Document = TableHeavyDoc(100), PageSize = RichEditorPageSize.Continuous };
        ed.Measure(new Size(800, double.PositiveInfinity));

        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(ed);
        double mb = (after - before) / 1024.0 / 1024.0;

        // The demo measured 6.8 MB for this shape at 100 units; the bound leaves room but still fails on
        // a cache that grows per layout pass instead of per table.
        Assert.True(mb < 60, $"100 units of nested + inline tables should stay bounded, used {mb:F1} MB");
    }
}
