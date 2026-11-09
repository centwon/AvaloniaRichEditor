using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Several pasted paragraphs, one rule for both controls (2026-11-08): the first pasted paragraph joins the text before the
// caret and the last joins the text after it; a side that is empty takes the format of the pasted paragraph joining it; runs
// of equal format rejoin and nothing empty is left behind; the caret ends after the pasted content. The same HTML pasted at the
// same place had saved as different JSON in the two controls — a list pasted onto an empty line lost its bullets here.
public class PasteSpliceRuleTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static RichEditor Editor(params Paragraph[] paragraphs)
    {
        var doc = new FlowDocument();
        doc.Blocks.Clear();
        foreach (var p in paragraphs) doc.Blocks.Add(p);
        return new RichEditor { Document = doc };
    }

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var name in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(name, NP)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static TextPointer Caret(RichEditor ed) => (TextPointer)typeof(RichEditor).GetField("_caretPosition", NP)!.GetValue(ed)!;

    private static Paragraph[] Paras(RichEditor ed) => ed.Document!.Blocks.OfType<Paragraph>().ToArray();

    private static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    [AvaloniaFact]
    public void TwoParagraphs_InTheMiddleOfALine_JoinBothSides()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "Hello World" } } };
        var ed = Editor(p);
        PlaceCaret(ed, p, 6);
        ed.InsertHtml("<p>A1</p><p>B2</p>");

        var ps = Paras(ed);
        Assert.Equal(new[] { "Hello A1", "B2World" }, ps.Select(Text));
        Assert.All(ps, q => Assert.Single(q.Inlines)); // equal-format runs rejoined
        Assert.Same(ps[1], Caret(ed).Paragraph);
        Assert.Equal(2, Caret(ed).Offset);
    }

    [AvaloniaFact]
    public void AList_PastedOntoAnEmptyLine_StaysAList()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "" } } };
        var ed = Editor(p);
        PlaceCaret(ed, p, 0);
        ed.InsertHtml("<ul><li>a</li><li>b</li></ul>");

        var ps = Paras(ed);
        Assert.Equal(new[] { "a", "b" }, ps.Select(Text));
        Assert.All(ps, q => Assert.Equal(ListKind.Bullet, q.ListType));
    }

    [AvaloniaFact]
    public void AHeading_PastedOntoAnEmptyLine_StaysAHeading()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "" } } };
        var ed = Editor(p);
        PlaceCaret(ed, p, 0);
        ed.InsertHtml("<h1>T</h1><p>x</p>");

        var ps = Paras(ed);
        Assert.Equal(new[] { "T", "x" }, ps.Select(Text));
        Assert.Equal(1, ps[0].HeadingLevel);
        Assert.Equal(0, ps[1].HeadingLevel);
    }

    [AvaloniaFact]
    public void TwoParagraphs_AtALinesEnd_LeaveNoEmptyRunOrParagraph()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "Hello" } } };
        var ed = Editor(p);
        PlaceCaret(ed, p, 5);
        ed.InsertHtml("<p>A1</p><p>B2</p>");

        var ps = Paras(ed);
        Assert.Equal(new[] { "HelloA1", "B2" }, ps.Select(Text));
        Assert.All(ps, q => Assert.Single(q.Inlines));
        Assert.Same(ps[1], Caret(ed).Paragraph);
        Assert.Equal(2, Caret(ed).Offset);
    }
}
