using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Typing while a block caret sits before/after a table. Indenting is the "space BEFORE a block"
// feature, so pressing Space on the block's trailing side pushed the gap out on its far side — the
// space appeared in front of the table, away from the caret.
public class BlockCaretTypingTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static void Type(RichEditor ed, string text)
        => ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });

    private static T? Field<T>(RichEditor ed, string n) where T : class
        => typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed) as T;

    private static Paragraph? Caret(RichEditor ed) => Field<TextPointer>(ed, "_caretPosition")?.Paragraph;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, offset));
    }

    private static (RichEditor ed, TableBlock tb, Paragraph before, Paragraph after) Doc()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>before</p><table><tr><td>x</td><td>y</td></tr></table><p>after</p>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        var paras = ed.Document!.Blocks.OfType<Paragraph>().ToList();
        return (ed, tb, paras.First(p => p.Text() == "before"), paras.First(p => p.Text() == "after"));
    }

    // Reaches the block caret on the table's trailing side (→ from the end of the last cell).
    private static (RichEditor ed, TableBlock tb, Paragraph after) AfterCaret()
    {
        var (ed, tb, _, after) = Doc();
        var last = tb.Cells[0][tb.Columns - 1].Para;
        PlaceCaret(ed, last, last.Text().Length);
        Press(ed, Key.Right);
        Assert.Same(tb, Field<Block>(ed, "_caretBlock")); // precondition
        return (ed, tb, after);
    }

    [AvaloniaFact]
    public void Space_OnTheTablesTrailingSide_DoesNotIndentTheTable()
    {
        var (ed, tb, _) = AfterCaret();
        double indent = tb.Indent;

        Press(ed, Key.Space);
        Type(ed, " ");

        Assert.Equal(indent, tb.Indent); // the gap must not open on the table's far side
    }

    [AvaloniaFact]
    public void Space_OnTheTablesTrailingSide_TypesIntoTheParagraphAfter()
    {
        var (ed, _, after) = AfterCaret();

        Press(ed, Key.Space);
        Type(ed, " ");

        Assert.Same(after, Caret(ed));
        Assert.StartsWith(" ", after.Text());
    }

    // Same class of defect: any content key dismissed the block caret but left the text caret wherever
    // it happened to be, so a letter typed after the table landed back inside its last cell.
    [AvaloniaFact]
    public void TypingALetter_OnTheTablesTrailingSide_LandsInTheParagraphAfter()
    {
        var (ed, tb, after) = AfterCaret();
        string cellBefore = tb.Cells[0][tb.Columns - 1].Para.Text();

        Press(ed, Key.A);
        Type(ed, "Z");

        Assert.Same(after, Caret(ed));
        Assert.Contains("Z", after.Text());
        Assert.Equal(cellBefore, tb.Cells[0][tb.Columns - 1].Para.Text()); // not into the cell
    }

    // Guard: from the LEADING side, Space is still the "space before a block" indent.
    [AvaloniaFact]
    public void Space_OnTheTablesLeadingSide_StillIndentsTheTable()
    {
        var (ed, tb, _, _) = Doc();
        PlaceCaret(ed, tb.Cells[0][0].Para, 0);
        Press(ed, Key.Left); // -> before-table block caret
        Assert.Same(tb, Field<Block>(ed, "_caretBlock"));
        double indent = tb.Indent;

        Press(ed, Key.Space);

        Assert.True(tb.Indent > indent, "Space before a block must still indent it");
    }

    [AvaloniaFact]
    public void ShiftTab_OnTheTablesLeadingSide_StillOutdentsTheTable()
    {
        var (ed, tb, _, _) = Doc();
        tb.Indent = 40;
        PlaceCaret(ed, tb.Cells[0][0].Para, 0);
        Press(ed, Key.Left);

        Press(ed, Key.Tab, KeyModifiers.Shift);

        Assert.Equal(20, tb.Indent);
    }

    // Typing on the leading side continues the paragraph before the table.
    [AvaloniaFact]
    public void TypingALetter_OnTheTablesLeadingSide_LandsInTheParagraphBefore()
    {
        var (ed, tb, before, _) = Doc();
        PlaceCaret(ed, tb.Cells[0][0].Para, 0);
        Press(ed, Key.Left);

        Press(ed, Key.A);
        Type(ed, "Z");

        Assert.Same(before, Caret(ed));
        Assert.EndsWith("Z", before.Text());
    }
}
