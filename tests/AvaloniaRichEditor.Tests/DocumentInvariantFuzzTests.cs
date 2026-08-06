using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Randomized edit sequences checked against the engine's structural invariants after every step.
// The per-feature suites each drive one command from a clean state; this drives long mixed sequences,
// which is where an invariant that only two commands together can break shows up. Seeded, so a failure
// reproduces exactly.
public class DocumentInvariantFuzzTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static TextPointer Caret(RichEditor ed)
        => (TextPointer)typeof(RichEditor).GetField("_caretPosition", NP)!.GetValue(ed)!;

    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string s)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s });

    // ---- invariants ---------------------------------------------------------

    // Core rule #5: the document starts and ends with a paragraph, and no two non-paragraph blocks are
    // adjacent — that is what lets the caret reach a position before and after every image/table.
    private static void AssertBlockListNormalized(IList<Block> blocks, string where)
    {
        Assert.True(blocks.Count > 0, $"{where}: block list is empty");
        Assert.True(blocks[0] is Paragraph, $"{where}: first block is {blocks[0].GetType().Name}, not a Paragraph");
        Assert.True(blocks[^1] is Paragraph, $"{where}: last block is {blocks[^1].GetType().Name}, not a Paragraph");
        for (int i = 0; i < blocks.Count - 1; i++)
            Assert.False(blocks[i] is not Paragraph && blocks[i + 1] is not Paragraph,
                $"{where}: two non-paragraph blocks are adjacent at {i}");
    }

    // Every cell holds at least one paragraph (TableCell's documented invariant), at any depth, and
    // every block's Parent points at the container that actually holds it.
    private static void AssertStructure(IList<Block> blocks, object parent, string where)
    {
        AssertBlockListNormalized(blocks, where);
        foreach (var b in blocks)
        {
            Assert.True(ReferenceEquals(b.Parent, parent),
                $"{where}: {b.GetType().Name}.Parent is not the list that holds it");
            switch (b)
            {
                case Paragraph p:
                    foreach (var inl in p.Inlines)
                    {
                        Assert.True(ReferenceEquals(inl.Parent, p), $"{where}: inline Parent is wrong");
                        if (inl is InlineTable it)
                            AssertTable(it.Table, it, $"{where} > inline table");
                    }
                    break;
                case TableBlock tb:
                    AssertTable(tb, tb.Parent!, where, expectParent: b.Parent);
                    break;
            }
        }
    }

    private static void AssertTable(TableBlock tb, object owner, string where, object? expectParent = null)
    {
        Assert.True(tb.Rows > 0 && tb.Columns > 0, $"{where}: degenerate table {tb.Rows}x{tb.Columns}");
        Assert.Equal(tb.Rows, tb.Cells.Count);
        foreach (var row in tb.Cells)
            Assert.Equal(tb.Columns, row.Count); // the grid stays rectangular

        // The merge grid, from BOTH directions. Walking only LogicalCells() checks the anchors and says
        // nothing about the covered slots, which is exactly where a merge grid goes wrong: a cell left
        // flagged covered after its anchor was shrunk or overwritten is an ORPHAN — LogicalCells() skips
        // it, so its content is unreachable to rendering, selection, every table command and every
        // formatter, and enough of them leave a table with no logical cells at all.
        Assert.NotEmpty(tb.LogicalCells());
        for (int r = 0; r < tb.Rows; r++)
            for (int c = 0; c < tb.Columns; c++)
            {
                var (cs, rs) = tb.SpanOf(r, c);
                if (tb.IsCovered(r, c))
                {
                    Assert.True(cs == 0 && rs == 0, $"{where}: covered slot ({r},{c}) reports span ({cs},{rs})");
                    var (ar, ac) = tb.AnchorOf(r, c);
                    Assert.True(ar >= 0 && ac >= 0 && ar < tb.Rows && ac < tb.Columns,
                        $"{where}: covered slot ({r},{c}) has off-grid anchor ({ar},{ac})");
                    Assert.False(ar == r && ac == c, $"{where}: covered slot ({r},{c}) is its own anchor");
                    Assert.False(tb.IsCovered(ar, ac),
                        $"{where}: covered slot ({r},{c}) resolves to ({ar},{ac}), which is itself covered");
                    var (acs, ars) = tb.SpanOf(ar, ac);
                    Assert.True(r >= ar && r < ar + ars && c >= ac && c < ac + acs,
                        $"{where}: covered slot ({r},{c}) claims anchor ({ar},{ac}), whose span ({acs},{ars}) does not reach it");
                    continue;
                }
                Assert.True(cs >= 1 && rs >= 1, $"{where}: anchor ({r},{c}) reports span ({cs},{rs})");
                Assert.True(c + cs <= tb.Columns && r + rs <= tb.Rows,
                    $"{where}: span at ({r},{c}) = ({cs},{rs}) runs past the grid");
            }

        foreach (var (r, c, cell) in tb.LogicalCells())
        {
            Assert.True(ReferenceEquals(cell.Parent, tb), $"{where}: cell({r},{c}).Parent is not its table");
            Assert.NotEmpty(cell.Blocks);
            Assert.Contains(cell.Blocks, x => x is Paragraph); // TableCell's "never paragraph-less" rule
            AssertStructure(cell.Blocks, cell, $"{where} > cell({r},{c})");
        }
    }

    // The caret must always name a paragraph the document can actually reach, at an offset inside it.
    private static void AssertCaretReachable(RichEditor ed, string where)
    {
        var caret = Caret(ed);
        if (caret.Paragraph == null) return; // nothing placed yet is legitimate
        var all = new HashSet<Paragraph>(AllParagraphs(ed.Document!.Blocks));
        if (!all.Contains(caret.Paragraph))
        {
            var p = caret.Paragraph;
            string txt = string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));
            string parent = p.Parent?.GetType().Name ?? "<null>";
            bool topLevel = ed.Document.Blocks.Contains(p);
            Assert.Fail($"{where}: the caret points at a paragraph not in the document " +
                        $"[text='{txt}', Parent={parent}, inDocumentBlocks={topLevel}, " +
                        $"list={p.ListType}, docBlocks={ed.Document.Blocks.Count}]");
        }
        int len = 0;
        foreach (var inl in caret.Paragraph.Inlines) len += inl is Run r ? (r.Text?.Length ?? 0) : 1;
        Assert.InRange(caret.Offset, 0, len);
    }

    private static IEnumerable<Paragraph> AllParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
        {
            if (b is Paragraph p)
            {
                yield return p;
                foreach (var inl in p.Inlines)
                    if (inl is InlineTable it)
                        foreach (var (_, _, cell) in it.Table.LogicalCells())
                            foreach (var q in AllParagraphs(cell.Blocks)) yield return q;
            }
            else if (b is TableBlock tb)
                foreach (var (_, _, cell) in tb.LogicalCells())
                    foreach (var q in AllParagraphs(cell.Blocks)) yield return q;
        }
    }

    private static void AssertAllInvariants(RichEditor ed, string where)
    {
        Assert.NotNull(ed.Document);
        AssertStructure(ed.Document!.Blocks, ed.Document, where);
        AssertCaretReachable(ed, where);
    }

    // ---- the sequence -------------------------------------------------------

    // The defect the fuzz found, reduced: turning a list on splits the paragraph's hard lines into new
    // paragraphs and splices them into the document. Non-Run inlines were CLONED into those, so the
    // original inline table (and its cell paragraphs) went out with the discarded source paragraph — a
    // caret inside one of its cells was left pointing into a detached subtree, where typing goes nowhere
    // visible. The inline object must be the same instance on the other side of the toggle.
    [AvaloniaFact]
    public void TogglingAListOnAHostParagraph_KeepsTheSameInlineTableInstance()
    {
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();
        Type(ed, "before");
        ed.InsertInlineTable(1, 2);

        var host = ed.Document!.Blocks.OfType<Paragraph>().Single(p => p.Inlines.OfType<InlineTable>().Any());
        var original = host.Inlines.OfType<InlineTable>().Single();
        var cellPara = original.Table.Cells[0][0].Para;

        ed.ToggleBullet();

        var after = ed.Document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<InlineTable>()).Single();
        Assert.Same(original, after);
        Assert.Contains(cellPara, AllParagraphs(ed.Document.Blocks));
    }

    // Same for an inline image: the selection chrome and resize handles track the instance.
    [AvaloniaFact]
    public void TogglingAListOnAHostParagraph_KeepsTheSameInlineImageInstance()
    {
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();
        Type(ed, "x");
        var doc = ed.Document!;
        var host = doc.Blocks.OfType<Paragraph>().First();
        var img = new InlineImage { Width = 16, Height = 16 };
        host.Inlines.Add(img);

        ed.ToggleBullet();

        var after = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineImage>()).Single();
        Assert.Same(img, after);
    }

    public static TheoryData<int> Seeds
    {
        get { var d = new TheoryData<int>(); for (int i = 1; i <= 20; i++) d.Add(i); return d; }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Seeds))]
    public void RandomEditSequence_KeepsEveryStructuralInvariant(int seed)
    {
        var rng = new Random(seed);
        var ed = new RichEditor { Document = new FlowDocument(), PageSize = RichEditorPageSize.Continuous };
        ed.FocusDocumentEnd();

        const int Steps = 300;
        var log = new List<string>();
        for (int i = 0; i < Steps; i++)
        {
            string op = Step(ed, rng);
            log.Add(op);
            // Measure between steps: layout is where a broken grid actually throws.
            ed.Measure(new Avalonia.Size(700, double.PositiveInfinity));
            AssertAllInvariants(ed, $"seed {seed}, step {i} ({op}); history: {string.Join(" -> ", log.TakeLast(8))}");
        }
    }

    // A 1x1 PNG, so image insertion exercises the real decode/measure path.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // Invokes one of the private table-structure operations against the caret's own table, the way the
    // context menu does. No-op when the caret is not in a table.
    private static string TableOp(RichEditor ed, string method, bool below)
    {
        var caret = Caret(ed);
        if (caret.Paragraph == null) return $"{method}(no caret)";
        var findCell = typeof(RichEditor).GetMethod("FindCell",
            BindingFlags.NonPublic | BindingFlags.Static)!; // static: resolves via the parent chain
        var loc = findCell.Invoke(null, new object[] { caret.Paragraph });
        if (loc == null) return $"{method}(not in a table)";

        // FindCell returns (TableBlock tb, int r, int c)? — the tuple element names exist only at
        // compile time, so reflection sees Item1/Item2/Item3.
        var t = loc.GetType();
        var tb = t.GetField("Item1")!.GetValue(loc)!;
        bool isRow = method.Contains("Row");
        int idx = (int)t.GetField(isRow ? "Item2" : "Item3")!.GetValue(loc)!;
        if (below && method.StartsWith("TableInsert")) idx++;

        var m = typeof(RichEditor).GetMethod(method, NP)!;
        m.Invoke(ed, new object?[] { tb, idx });
        return method;
    }

    private static string Step(RichEditor ed, Random rng)
    {
        switch (rng.Next(30))
        {
            case 18: ed.InsertImageBytes(Png); return "insert-image";
            // Row/column structure has no editor-level public API (only the context menu reaches it),
            // so the fuzz drives the same private entry points the menu items call.
            case 19: return TableOp(ed, "TableInsertRow", below: true);
            case 20: return TableOp(ed, "TableInsertColumn", below: true);
            case 21: return TableOp(ed, "TableDeleteRow", below: false);
            case 22: return TableOp(ed, "TableDeleteColumn", below: false);
            case 23: ed.SetHeading(rng.Next(0, 7)); return "heading";
            case 24: ed.ToggleQuote(); return "quote";
            case 25: ed.Indent(rng.Next(2) == 0 ? 20 : -20); return "indent";
            case 26: ed.SetTextAlignment((TextAlignment)rng.Next(0, 4)); return "align";
            case 27: Press(ed, Key.Home, rng.Next(2) == 0 ? KeyModifiers.None : KeyModifiers.Shift); return "home";
            case 28: Press(ed, Key.End, rng.Next(2) == 0 ? KeyModifiers.None : KeyModifiers.Shift); return "end";
            case 29: ed.RemoveList(); return "remove-list";
            case 0: Type(ed, "abc"); return "type";
            case 1: Type(ed, "한글"); return "type-cjk";
            case 2: Press(ed, Key.Enter); return "enter";
            case 3: Press(ed, Key.Back); return "backspace";
            case 4: Press(ed, Key.Delete); return "delete";
            case 5: ed.InsertTable(2, 2); return "insert-table";
            case 6: ed.InsertInlineTable(1, 2); return "insert-inline-table";
            case 7: ed.InsertDivider(); return "insert-divider";
            case 8: Press(ed, Key.Tab); return "tab";
            case 9: Press(ed, Key.Tab, KeyModifiers.Shift); return "shift-tab";
            case 10: Press(ed, Key.Left, KeyModifiers.Shift); return "shift-left";
            case 11: Press(ed, Key.Right); return "right";
            case 12: Press(ed, Key.Up); return "up";
            case 13: Press(ed, Key.Down); return "down";
            case 14: Press(ed, Key.A, KeyModifiers.Control); return "select-all";
            case 15: ed.Undo(); return "undo";
            case 16: ed.Redo(); return "redo";
            default: ed.ToggleBullet(); return "toggle-bullet";
        }
    }
}
