using System;
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
}
