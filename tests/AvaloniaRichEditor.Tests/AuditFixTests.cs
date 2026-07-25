using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Regressions for the four defects found in the full-source audit: the Ctrl+Shift+X / Cut shortcut
// collision, paragraph format dropped when an edit derives a new paragraph, cell content lost on
// merge, and document-order comparison that never descended into nested / inline tables.
public class AuditFixTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static RichEditor Editor(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        ed.FocusDocumentEnd();
        return ed;
    }

    private static Paragraph Para(RichEditor ed, int i)
        => (Paragraph)ed.Document!.Blocks.Where(b => b is Paragraph).ElementAt(i);

    private static bool HasStrike(Run r)
        => r.TextDecorations != null && r.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);

    // The Cut branch matched Ctrl+X regardless of Shift, so the documented Ctrl+Shift+X
    // (strikethrough) cut the selection instead — the text vanished.
    [AvaloniaFact]
    public void CtrlShiftX_Strikes_DoesNotCut()
    {
        var ed = Editor("<p>abc</p>");
        Press(ed, Key.A, KeyModifiers.Control);
        Press(ed, Key.X, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal("abc", Para(ed, 0).Text());
        Assert.All(Para(ed, 0).Inlines.OfType<Run>(), r => Assert.True(HasStrike(r)));
    }

    // Plain Ctrl+X must still cut.
    [AvaloniaFact]
    public void CtrlX_StillCuts()
    {
        var ed = Editor("<p>abc</p>");
        Press(ed, Key.A, KeyModifiers.Control);
        Press(ed, Key.X, KeyModifiers.Control);

        Assert.Equal("", Para(ed, 0).Text());
    }

    // Enter derived the new paragraph by copying a hand-picked subset of fields, so line spacing,
    // the quote bar, the right margin and the list marker style were silently reset.
    [AvaloniaFact]
    public void Enter_InheritsFullParagraphFormat()
    {
        var ed = Editor("<p>abc</p>");
        var p0 = Para(ed, 0);
        p0.LineSpacing = 1.5;
        p0.IsQuote = true;
        p0.MarginRight = 30;
        p0.ListType = ListKind.Bullet;
        p0.ListMarker = ListMarkerStyle.Square;

        Press(ed, Key.Enter);

        var p1 = Para(ed, 1);
        Assert.Equal(1.5, p1.LineSpacing);
        Assert.True(p1.IsQuote);
        Assert.Equal(30, p1.MarginRight);
        Assert.Equal(ListKind.Bullet, p1.ListType);
        Assert.Equal(ListMarkerStyle.Square, p1.ListMarker);
    }

    // ...but Enter still starts body text after a heading (core rule #3).
    [AvaloniaFact]
    public void Enter_AfterHeading_StartsBodyText()
    {
        var ed = Editor("<h1>title</h1>");
        Press(ed, Key.Enter);
        Assert.Equal(0, Para(ed, 1).HeadingLevel);
    }

    // Merging cells kept only each covered cell's FIRST paragraph; anything after it stayed in the
    // covered cell, which LogicalCells() skips — invisible, and wiped by a later unmerge.
    [AvaloniaFact]
    public void MergeCells_KeepsExtraBlocksOfCoveredCell()
    {
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "A" });
        var c1 = tb.Cells[0][1];
        ((Run)c1.Blocks.OfType<Paragraph>().First().Inlines[0]).Text = "line1";
        c1.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "line2" } } });
        c1.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "line3" } } });

        tb.MergeCells(0, 0, 0, 1);

        var visible = tb.LogicalCells()
            .SelectMany(x => x.cell.Blocks.OfType<Paragraph>())
            .Select(p => p.Text())
            .ToList();
        Assert.Contains("line2", visible);
        Assert.Contains("line3", visible);
        // Order is preserved and the leading paragraph still joins the anchor's line.
        Assert.Equal("A line1", visible[0]);
        Assert.True(visible.IndexOf("line2") < visible.IndexOf("line3"));
    }

    // The simple case must keep its old shape: two plain cells merge into one line.
    [AvaloniaFact]
    public void MergeCells_PlainCells_JoinOnOneLine()
    {
        var tb = new TableBlock(1, 2);
        tb.Cells[0][0].Para.Inlines.Add(new Run { Text = "A" });
        ((Run)tb.Cells[0][1].Para.Inlines[0]).Text = "B";

        tb.MergeCells(0, 0, 0, 1);

        Assert.Equal("A B", tb.Cells[0][0].Para.Text());
        Assert.Single(tb.Cells[0][0].Blocks);
    }

    // CompareTo walked only one table level, so a pointer inside a nested table was "not found"
    // (index -1) and always sorted first — flipping selection order.
    [AvaloniaFact]
    public void CompareTo_OrdersParagraphInNestedTable()
    {
        var outer = new TableBlock(1, 1);
        var nested = new TableBlock(1, 1);
        var deep = nested.Cells[0][0].Para;
        deep.Inlines.Add(new Run { Text = "deep" });
        outer.Cells[0][0].Blocks.Add(nested);

        var head = new Paragraph { Inlines = { new Run { Text = "head" } } };
        var tail = new Paragraph { Inlines = { new Run { Text = "tail" } } };
        var doc = new FlowDocument();
        foreach (var b in new Block[] { head, outer, tail }) { b.Parent = doc; doc.Blocks.Add(b); }
        outer.Cells[0][0].Parent = outer;
        nested.Parent = outer.Cells[0][0];
        nested.Cells[0][0].Parent = nested;
        deep.Parent = nested.Cells[0][0];

        Assert.True(new TextPointer(head, 0).CompareTo(new TextPointer(deep, 0)) < 0);
        Assert.True(new TextPointer(deep, 0).CompareTo(new TextPointer(tail, 0)) < 0);
    }

    // Same for a table living inline in a paragraph's runs (milestone B).
    [AvaloniaFact]
    public void CompareTo_OrdersParagraphInInlineTable()
    {
        var host = new Paragraph { Inlines = { new Run { Text = "host" } } };
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        var inner = it.Table.Cells[0][0].Para;
        inner.Inlines.Add(new Run { Text = "in" });
        host.Inlines.Add(it);

        var tail = new Paragraph { Inlines = { new Run { Text = "tail" } } };
        var doc = new FlowDocument();
        foreach (var b in new Block[] { host, tail }) { b.Parent = doc; doc.Blocks.Add(b); }
        foreach (var inl in host.Inlines) inl.Parent = host;
        it.Table.Parent = it;
        it.Table.Cells[0][0].Parent = it.Table;
        inner.Parent = it.Table.Cells[0][0];

        Assert.True(new TextPointer(host, 0).CompareTo(new TextPointer(inner, 0)) < 0);
        Assert.True(new TextPointer(inner, 0).CompareTo(new TextPointer(tail, 0)) < 0);
    }
}
