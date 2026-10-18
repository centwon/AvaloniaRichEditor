using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A selection with an endpoint inside an inline table (WinUI port's pre-publish audit, 2026-10-18; measured here too).
// The document order lists an inline table's cells right AFTER the paragraph holding it, and positions were ordered by
// it: in "ab[table]cd" every cell position sorted after "cd". One Shift+Left from just after the table (into the end of
// its last cell) was read as "from after the table to that cell" — every OTHER cell — and Delete emptied them.
// PositionOrder puts a cell position, for its host, between the table's offset and the next.
public class SelectionPositionOrderTests(ITestOutputHelper output)
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type Ed = typeof(RichEditor);

    private static object? Call(RichEditor ed, string name, params object[] args)
        => Ed.GetMethod(name, NP, args.Select(a => a.GetType()).ToArray())!.Invoke(ed, args);

    private static void SetSelection(RichEditor ed, TextPointer anchor, TextPointer active)
    {
        Ed.GetField("_caretPosition", NP)!.SetValue(ed, active);
        Ed.GetField("_selectionStart", NP)!.SetValue(ed, anchor);
        Ed.GetField("_selectionEnd", NP)!.SetValue(ed, active);
    }

    private static (RichEditor ed, Paragraph host, TableBlock t) InlineTableLine()
    {
        var t = new TableBlock(2, 2);
        int n = 1;
        foreach (var row in t.Cells) foreach (var c in row) c.Para.Inlines.Add(new Run { Text = "cell" + n++ });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "ab" }, new InlineTable { Table = t }, new Run { Text = "cd" } } });
        var ed = new RichEditor { Document = doc };
        var host = (Paragraph)ed.Document!.Blocks[0];
        return (ed, host, host.Inlines.OfType<InlineTable>().Single().Table);
    }

    private static string Cells(TableBlock t)
        => string.Join(",", t.Cells.SelectMany(r => r).Select(c => string.Concat(c.Para.Inlines.OfType<Run>().Select(r => r.Text))));

    [AvaloniaFact]
    public void ShiftLeftIntoAnInlineTable_ThenDelete_KeepsTheOtherCells()
    {
        var (ed, host, t) = InlineTableLine();
        using var ih = InteractionHost.Create(ed);
        SetSelection(ed, new TextPointer(host, 3), new TextPointer(host, 3)); // after "ab" and the table
        ih.Key(Key.Left, RawInputModifiers.Shift);
        ih.Key(Key.Delete);
        Assert.Equal("cell1,cell2,cell3,cell4", Cells(t));
        Assert.Equal("abcd", string.Concat(host.Inlines.OfType<Run>().Select(r => r.Text)));
    }

    [AvaloniaFact]
    public void ShiftLeftTwiceIntoAnInlineTable_ThenDelete_TakesOneCharacter()
    {
        var (ed, host, t) = InlineTableLine();
        using var ih = InteractionHost.Create(ed);
        SetSelection(ed, new TextPointer(host, 3), new TextPointer(host, 3));
        ih.Key(Key.Left, RawInputModifiers.Shift);
        ih.Key(Key.Left, RawInputModifiers.Shift);
        ih.Key(Key.Delete);
        Assert.Equal("cell1,cell2,cell3,cell", Cells(t));
    }

    [AvaloniaFact]
    public void ACellPosition_SortsWhereItsTableIsOnTheHostLine()
    {
        var (_, host, t) = InlineTableLine();
        var cell = new TextPointer(t.Cells[1][1].Para, 2);
        foreach (var (hostOffset, sign) in new[] { (0, 1), (2, 1), (3, -1), (5, -1) })
            Assert.Equal(sign, Math.Sign(cell.CompareTo(new TextPointer(host, hostOffset))));
        Assert.True(new TextPointer(t.Cells[0][0].Para, 5).CompareTo(cell) < 0);
    }

    // An empty paragraph inside the range covers no character but is a line of it: the first PositionOrder draft dropped
    // it, and a blank line between two selected lines vanished from the copied text (the port's AOT sweep, 2026-10-18).
    [AvaloniaFact]
    public void ABlankLineInsideTheRange_StaysInTheTextAndTheCopy()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "ab" } } });
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "cd" } } });
        var ed = new RichEditor { Document = doc };
        var ps = ed.Document!.Blocks.OfType<Paragraph>().ToList();
        var range = new TextRange(new TextPointer(ps[0], 1), new TextPointer(ps[2], 1));
        Assert.Equal("b\n\nc", range.GetText());
        Assert.Equal(2, range.GetRichInlines().Count(i => i is Run { Text: "\n" }));
        SetSelection(ed, new TextPointer(ps[0], 1), new TextPointer(ps[2], 1));
        var html = (FlowDocument)Ed.GetMethod("BuildSelectionDocument", NP)!.Invoke(ed, null)!;
        Assert.Equal(3, html.Blocks.Count);
    }

    // A range wholly inside one cell of an inline table is no line of the paragraph holding the table: with the empty-line
    // rule above, the first draft counted the host as an empty line of the selection too.
    [AvaloniaFact]
    public void ARangeInsideAnInlineTablesCell_IsNoLineOfItsHost()
    {
        var t = new TableBlock(1, 1);
        var cell = t.Cells[0][0];
        cell.Blocks.Clear();
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "first" } } });
        cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "second" } } });
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "a" }, new InlineTable { Table = t }, new Run { Text = "b" } } });
        var ed = new RichEditor { Document = doc };
        var host = (Paragraph)ed.Document!.Blocks[0];
        var paras = host.Inlines.OfType<InlineTable>().Single().Table.Cells[0][0].Blocks.OfType<Paragraph>().ToList();
        var range = new TextRange(new TextPointer(paras[0], 2), new TextPointer(paras[1], 3));
        Assert.Equal("rst\nsec", range.GetText());
        Assert.DoesNotContain(range.Covered(), c => ReferenceEquals(c.Paragraph, host));
    }

    // ---- what Copy takes is what Delete removes, for random selections -------------------------------------

    private static readonly int Seeds =
        int.TryParse(Environment.GetEnvironmentVariable("RICHEDITOR_FUZZ_SEEDS"), out int s) && s > 0 ? s : 400;

    private const int RangesPerDocument = 4;

    // Paragraphs, block tables and inline tables at random depths: cells with several paragraphs, tables nested in
    // cells, inline tables in cell paragraphs — every place a caret can be that the order has to get right.
    private static FlowDocument RandomDocument(Random rng)
    {
        int word = 0;
        string Text() => string.Concat(Enumerable.Range(0, rng.Next(0, 4)).Select(_ => (char)('a' + rng.Next(26)) + (word++ % 10).ToString()));
        Paragraph Para(int depth)
        {
            var p = new Paragraph();
            int parts = rng.Next(1, 4);
            for (int i = 0; i < parts; i++)
            {
                if (depth < 3 && rng.Next(5) == 0) p.Inlines.Add(new InlineTable { Table = Table(depth + 1) });
                else p.Inlines.Add(new Run { Text = Text() });
            }
            return p;
        }
        TableBlock Table(int depth)
        {
            var t = new TableBlock(1 + rng.Next(2), 1 + rng.Next(2));
            foreach (var row in t.Cells)
                foreach (var cell in row)
                {
                    cell.Blocks.Clear();
                    int n = 1 + rng.Next(2);
                    for (int i = 0; i < n; i++)
                        cell.Blocks.Add(depth < 3 && rng.Next(6) == 0 ? Table(depth + 1) : Para(depth));
                    if (cell.Blocks[^1] is not Paragraph) cell.Blocks.Add(new Paragraph { Inlines = { new Run { Text = Text() } } });
                }
            return t;
        }
        var doc = new FlowDocument();
        int blocks = 2 + rng.Next(4);
        for (int i = 0; i < blocks; i++) doc.Blocks.Add(rng.Next(4) == 0 ? Table(1) : Para(0));
        return doc;
    }

    private static Dictionary<char, int> Letters(IEnumerable<Block> blocks)
    {
        var bag = new Dictionary<char, int>();
        void Add(string? t) { if (t != null) foreach (char c in t) if (!char.IsWhiteSpace(c) && c != '￼') bag[c] = bag.GetValueOrDefault(c) + 1; }
        void Inl(Inline i) { if (i is Run r) Add(r.Text); else if (i is InlineTable it) Blk(it.Table); }
        void Blk(Block b)
        {
            if (b is Paragraph p) foreach (var i in p.Inlines) Inl(i);
            else if (b is TableBlock t) foreach (var row in t.Cells) foreach (var c in row) foreach (var cb in c.Blocks) Blk(cb);
        }
        foreach (var b in blocks) Blk(b);
        return bag;
    }

    private static Dictionary<char, int> LettersOfInlines(IEnumerable<Inline> inlines)
    {
        var p = new Paragraph();
        foreach (var i in inlines) p.Inlines.Add(i);
        return Letters(new Block[] { p });
    }

    // What Copy puts in the internal clipboard (CopySelectionToClipboard): a whole selected table, a cell block's
    // rectangle, the captured blocks, or the range's inlines.
    private static Dictionary<char, int> CopiedLetters(RichEditor ed, TextRange range)
    {
        if (Ed.GetMethod("SelectedWholeTable", NP)!.Invoke(ed, null) is TableBlock whole) return Letters(new Block[] { whole });
        if (Ed.GetMethod("CellBlockTable", NP)!.Invoke(ed, null) is TableBlock cbt
            && Ed.GetMethod("SelectedCellRange", NP)!.Invoke(ed, new object[] { cbt }) is { } rg)
        {
            var block = (TableBlock)Ed.GetMethod("CellBlockAsTable", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { cbt, rg })!;
            return Letters(new Block[] { block });
        }
        if (Ed.GetMethod("CaptureBlockStructure", NP)!.Invoke(ed, new object[] { range }) is List<Block> blocks) return Letters(blocks);
        return LettersOfInlines(range.GetRichInlines());
    }

    private static string Describe(TextPointer tp)
    {
        var sb = new StringBuilder();
        for (object? cur = tp.Paragraph?.Parent; cur is TextElement te; cur = te.Parent)
            sb.Insert(0, te switch { TableCell => "cell/", TableBlock => "table/", InlineTable => "inline/", _ => "" });
        return sb + $"p@{tp.Offset}";
    }

    [AvaloniaFact]
    public void RandomSelections_DeleteExactlyWhatCopyTakes()
    {
        var failures = new List<string>();
        int ranges = 0;
        for (int seed = 0; seed < Seeds && failures.Count < 8; seed++)
        {
            var rng = new Random(seed);
            var template = RandomDocument(rng);
            for (int trial = 0; trial < RangesPerDocument && failures.Count < 8; trial++)
            {
                var ed = new RichEditor { Document = template.Clone() };
                var paras = (List<Paragraph>)Ed.GetMethod("GetAllParagraphsInOrder", NP, Type.EmptyTypes)!.Invoke(ed, null)!;
                TextPointer Pick()
                {
                    var p = paras[rng.Next(paras.Count)];
                    int len = p.Inlines.Sum(i => i is Run r ? (r.Text?.Length ?? 0) : 1);
                    return new TextPointer(p, rng.Next(len + 1));
                }
                var a = Pick(); var b = Pick();
                if (a.CompareTo(b) == 0) continue;
                string where = $"seed {seed}/{trial}: {Describe(a)} .. {Describe(b)}";
                try
                {
                    SetSelection(ed, a, b);
                    var copied = CopiedLetters(ed, new TextRange(a, b));
                    var before = Letters(ed.Document!.Blocks);
                    Ed.GetMethod("DeleteSelection", NP)!.Invoke(ed, null);
                    var after = Letters(ed.Document!.Blocks);
                    var removed = new Dictionary<char, int>();
                    foreach (var (c, n) in before) if (n - after.GetValueOrDefault(c) is int k && k != 0) removed[c] = k;
                    foreach (var (c, n) in after) if (!before.ContainsKey(c)) removed[c] = -n;
                    ranges++;
                    var diff = new StringBuilder();
                    foreach (char c in removed.Keys.Union(copied.Keys).OrderBy(c => c))
                        if (removed.GetValueOrDefault(c) != copied.GetValueOrDefault(c))
                            diff.Append($"'{c}' {removed.GetValueOrDefault(c)}/{copied.GetValueOrDefault(c)} ");
                    if (diff.Length > 0) failures.Add($"{where}: removed/copied {diff}");
                }
                catch (TargetInvocationException ex) { failures.Add($"{where}: threw {ex.InnerException}"); }
            }
        }
        output.WriteLine($"{ranges} ranges");
        foreach (var f in failures) output.WriteLine(f);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
