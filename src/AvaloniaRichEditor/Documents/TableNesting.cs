using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaRichEditor.Documents;

// Ceiling on table-in-table depth across the document model: matches the RTF reader's 32-level cap and
// stays well inside DocumentSerializer's JsonReaderOptions/JsonWriterOptions MaxDepth so any valid
// in-memory document can be serialized and deserialized without throwing.
internal static class TableNesting
{
    internal const int Max = 32;

    // Number of enclosing TableBlocks above `at` (0 at the document top level, 1 inside a top-level table cell, etc.).
    internal static int DepthAt(TextElement? at)
    {
        int d = 0;
        for (TextElement? cur = at; cur != null; cur = cur.Parent as TextElement)
            if (cur is TableBlock) d++;
        return d;
    }

    internal static bool CanInsertAt(TextElement? at) => DepthAt(at) < Max;

    internal static bool Fits(TextElement? at, Block b) => DepthAt(at) + Height(b) <= Max;

    internal static bool Fits(TextElement? at, Inline inl)
        => inl is InlineTable it ? DepthAt(at) + Height(it.Table) <= Max : true;

    internal static int Height(Block b) => b switch
    {
        TableBlock tb => Height(tb),
        Paragraph p => p.Inlines.OfType<InlineTable>().Select(it => Height(it.Table)).DefaultIfEmpty(0).Max(),
        _ => 0,
    };

    internal static int Height(TableBlock tb)
    {
        int maxChild = 0;
        foreach (var row in tb.Cells)
            foreach (var cell in row)
            {
                int h = Height(cell.Blocks);
                if (h > maxChild) maxChild = h;
            }
        return 1 + maxChild;
    }

    internal static int Height(IEnumerable<Block> blocks)
    {
        int max = 0;
        foreach (var b in blocks)
        {
            int h = Height(b);
            if (h > max) max = h;
        }
        return max;
    }

    // Flattens any TableBlock (or InlineTable) deeper than `remainingLevels` into its cells' blocks/text
    // in place so the subtree never exceeds `remainingLevels` levels of table nesting.
    internal static void Flatten(IList<Block> blocks, int remainingLevels)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is TableBlock tb)
            {
                if (remainingLevels <= 0)
                {
                    var flat = FlattenTableToBlocks(tb);
                    blocks.RemoveAt(i);
                    for (int j = 0; j < flat.Count; j++)
                        blocks.Insert(i + j, flat[j]);
                    i += Math.Max(0, flat.Count - 1);
                }
                else
                {
                    foreach (var row in tb.Cells)
                        foreach (var cell in row)
                        {
                            Flatten(cell.Blocks, remainingLevels - 1);
                            if (cell.Blocks.Count == 0)
                                cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
                            foreach (var cb in cell.Blocks) cb.Parent = cell;
                        }
                }
            }
            else if (blocks[i] is Paragraph p)
            {
                FlattenParagraphInlines(p, remainingLevels);
            }
        }
    }

    private static void FlattenParagraphInlines(Paragraph p, int remainingLevels)
    {
        bool removedAny = false;
        for (int i = 0; i < p.Inlines.Count; i++)
        {
            if (p.Inlines[i] is InlineTable it)
            {
                if (remainingLevels <= 0)
                {
                    string text = FlattenTableToText(it.Table);
                    p.Inlines.RemoveAt(i);
                    removedAny = true;
                    if (!string.IsNullOrEmpty(text))
                        p.Inlines.Insert(i, new Run { Text = text, Parent = p });
                    else
                        i--;
                }
                else
                {
                    foreach (var row in it.Table.Cells)
                        foreach (var cell in row)
                        {
                            Flatten(cell.Blocks, remainingLevels - 1);
                            if (cell.Blocks.Count == 0)
                                cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
                            foreach (var cb in cell.Blocks) cb.Parent = cell;
                        }
                }
            }
        }
        if (removedAny && p.Inlines.Count == 0)
            p.Inlines.Add(new Run { Text = "", Parent = p });
    }

    private static List<Block> FlattenTableToBlocks(TableBlock tb)
    {
        var result = new List<Block>();
        foreach (var (_, _, cell) in tb.LogicalCells())
        {
            Flatten(cell.Blocks, 0);
            foreach (var b in cell.Blocks)
                result.Add(b);
        }
        if (result.Count == 0)
            result.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
        return result;
    }

    private static string FlattenTableToText(TableBlock tb)
    {
        var parts = new List<string>();
        foreach (var (_, _, cell) in tb.LogicalCells())
        {
            Flatten(cell.Blocks, 0);
            foreach (var p in cell.Blocks.OfType<Paragraph>())
            {
                string t = string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));
                if (!string.IsNullOrEmpty(t)) parts.Add(t);
            }
        }
        return string.Join(" ", parts);
    }
}
