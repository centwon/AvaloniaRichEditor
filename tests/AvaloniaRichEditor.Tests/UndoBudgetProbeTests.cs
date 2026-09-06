using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Where UndoManager's per-element constant comes from: what one checkpoint actually retains.
//
// This started as "should the WinUI peer's byte budget be backported here, and with what number?" and
// answered a different question: the peer's estimate charged 2 bytes per character, and text costs a
// checkpoint NOTHING because Clone shares the strings. The same document at 2 and at 60 characters per
// paragraph retains byte-for-byte the same. What it costs is the object graph — ~310 bytes per element
// here, stable across every shape below. Both repos now bound the history by that.
//
// The report prints the old model next to reality, because that gap is the finding. Run with:
//   dotnet test tests/AvaloniaRichEditor.Tests --filter UndoBudgetProbe --logger "console;verbosity=detailed"
public class UndoBudgetProbeTests
{
    private readonly ITestOutputHelper _out;
    public UndoBudgetProbeTests(ITestOutputHelper output) => _out = output;

    private static FlowDocument Doc(int paragraphs, int charsPerParagraph, int runsPerParagraph = 1)
    {
        var doc = new FlowDocument();
        for (int i = 0; i < paragraphs; i++)
        {
            var p = new Paragraph();
            for (int r = 0; r < runsPerParagraph; r++)
                p.Inlines.Add(new Run { Text = new string('x', charsPerParagraph) });
            doc.Blocks.Add(p);
        }
        return doc;
    }

    // Blocks + inlines, at any depth: the candidate the measurement points at.
    private static int ElementCount(FlowDocument doc)
    {
        int n = 0;
        void Walk(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                n++;
                if (b is Paragraph p)
                {
                    n += p.Inlines.Count;
                    foreach (var inl in p.Inlines)
                        if (inl is InlineTable it)
                            foreach (var row in it.Table.Cells)
                                foreach (var cell in row)
                                    Walk(cell.Blocks);
                }
                else if (b is TableBlock tb)
                {
                    foreach (var row in tb.Cells)
                        foreach (var cell in row)
                        {
                            n++;
                            Walk(cell.Blocks);
                        }
                }
            }
        }
        Walk(doc.Blocks);
        return n;
    }

    private static Paragraph First(FlowDocument d) => (Paragraph)d.Blocks[0];

    // The estimate as it stood BEFORE this was measured: 2 bytes per character. Kept here so the
    // report shows the old model next to reality — that gap is the whole finding.
    private static long OldEstimateBytes(FlowDocument doc)
    {
        long total = 0;
        void Walk(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                total += 48;
                if (b is Paragraph p)
                    foreach (var inl in p.Inlines)
                        total += inl is Run r ? 40 + (long)(r.Text?.Length ?? 0) * 2 : 24;
                else if (b is TableBlock tb)
                    foreach (var row in tb.Cells)
                        foreach (var cell in row)
                            Walk(cell.Blocks);
            }
        }
        Walk(doc.Blocks);
        return total;
    }

    private static long Heap()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [AvaloniaTheory]
    [InlineData(3000, 60, 1)]    // a long ordinary document
    [InlineData(300, 4000, 1)]   // the same text mass, concentrated in few very long runs
    [InlineData(20000, 60, 1)]   // a big one
    [InlineData(3000, 60, 5)]    // same paragraphs, five times the INLINES — the falsification case:
                                 // if cost tracks elements rather than paragraphs or text, this is ~3x
    [InlineData(3000, 2, 1)]     // same elements, almost no text: cost should barely move
    public void WhatACheckpointRetains(int paragraphs, int chars, int runs)
    {
        var doc = Doc(paragraphs, chars, runs);
        int elements = ElementCount(doc);
        var caret = new TextPointer(First(doc), 0);
        long oldEstimate = OldEstimateBytes(doc);
        long estimate = UndoManager.EstimateBytes(doc);

        long before = Heap();
        // A budget large enough that nothing is trimmed: the point is what ONE checkpoint costs, before
        // any policy applies. Ten, not fifty — fifty of the big case is 600 MB on a CI runner.
        var undo = new UndoManager(maxBytes: long.MaxValue);
        for (int i = 0; i < 10; i++) undo.PushState(doc, caret);
        long after = Heap();
        GC.KeepAlive(undo);

        double mb = (after - before) / 1024.0 / 1024.0;
        double perCheckpoint = (after - before) / 10.0;
        _out.WriteLine(
            $"{paragraphs}p x {chars}ch x {runs}run | elements {elements,7} | " +
            $"old estimate {oldEstimate / 1024.0 / 1024.0,6:F2} MB | now {estimate / 1024.0 / 1024.0,6:F2} MB | " +
            $"retained/checkpoint {perCheckpoint / 1024.0,8:F0} KB | " +
            $"bytes/element {perCheckpoint / elements,6:F0} | total {mb,6:F1} MB");
    }
}
