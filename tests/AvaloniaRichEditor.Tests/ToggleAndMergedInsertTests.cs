using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Two user-reported defects:
// - Ctrl+B over "normal BOLD normal" flipped each run on its own → "BOLD normal BOLD". Word decides
//   the direction from the selection as a whole (off only when all of it already has the format).
// - "Insert row below" on a vertically merged cell inserted at anchor+1, i.e. INSIDE the merge: the
//   merge grew over the new row and only the neighbouring columns showed a new row.
public class ToggleAndMergedInsertTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static (RichEditor ed, Paragraph p) MixedBold()
    {
        var p = TestHelpers.Para(
            new Run { Text = "aaa" },
            new Run { Text = "BBB", FontWeight = FontWeight.Bold },
            new Run { Text = "ccc" });
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        ed.FocusDocumentEnd();
        Press(ed, Key.A, KeyModifiers.Control);
        return (ed, p);
    }

    [AvaloniaFact]
    public void CtrlB_OnMixedSelection_MakesAllBold()
    {
        var (ed, p) = MixedBold();

        Press(ed, Key.B, KeyModifiers.Control);

        Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Bold, r.FontWeight));
        Assert.Equal("aaaBBBccc", p.Text());
    }

    [AvaloniaFact]
    public void CtrlB_OnAllBoldSelection_ClearsBold()
    {
        var (ed, p) = MixedBold();

        Press(ed, Key.B, KeyModifiers.Control);
        Press(ed, Key.B, KeyModifiers.Control);

        Assert.All(p.Inlines.OfType<Run>(), r => Assert.Equal(FontWeight.Normal, r.FontWeight));
    }

    [AvaloniaFact]
    public void Underline_OnMixedSelection_UnderlinesAll()
    {
        var p = TestHelpers.Para(
            new Run { Text = "aaa" },
            new Run { Text = "BBB", TextDecorations = TextDecorations.Underline },
            new Run { Text = "ccc" });
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        ed.FocusDocumentEnd();
        Press(ed, Key.A, KeyModifiers.Control);

        ed.ToggleUnderline();

        Assert.All(p.Inlines.OfType<Run>(), r =>
            Assert.Contains(r.TextDecorations!, d => d.Location == TextDecorationLocation.Underline));
    }

    [AvaloniaFact]
    public void InsertRowBelow_OnVerticallyMergedCell_InsertsAfterTheMerge()
    {
        var tb = new TableBlock(3, 2);
        tb.MergeCells(0, 0, 1, 0); // column 0: rows 0–1 merged
        var doc = new FlowDocument();
        doc.Blocks.Add(tb);
        var ed = new RichEditor { Document = doc };
        ed.FocusDocumentEnd();
        var cellPara = tb.Cells[0][0].Blocks.OfType<Paragraph>().First();

        var items = new List<Control>();
        typeof(RichEditor).GetMethod("AddTableStructureItems", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object?[] { items, tb, cellPara, false });
        var below = items.OfType<MenuItem>()
            .Single(m => m.Header is "Insert Row Below" or "아래에 행 삽입");
        below.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(4, tb.Rows);
        Assert.Equal((1, 2), tb.SpanOf(0, 0)); // the merge did not grow over the new row
        Assert.False(tb.IsCovered(2, 0));      // the new row has its own cell under the merge
        Assert.False(tb.IsCovered(2, 1));
    }
}
