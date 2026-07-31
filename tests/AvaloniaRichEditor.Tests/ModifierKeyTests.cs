using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A bare modifier press is the first half of a chord (Shift+Tab, Ctrl+B). Nothing in the key handler
// acts on one, but it used to fall through as "some other key" — dismissing the block caret and
// cancelling an image selection before the chord was finished.
public class ModifierKeyTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static T? Field<T>(RichEditor ed, string n) where T : class
        => typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed) as T;

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        foreach (var n in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(ed, new TextPointer(p, offset));
    }

    // Editor with a table carrying the block caret on its leading side.
    private static (RichEditor ed, TableBlock tb) BeforeTableCaret()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>before</p><table><tr><td>x</td></tr></table><p>after</p>");
        var tb = ed.Document!.Blocks.OfType<TableBlock>().Single();
        PlaceCaret(ed, tb.Cells[0][0].Para, 0);
        Press(ed, Key.Left); // -> before-table block caret
        Assert.Same(tb, Field<Block>(ed, "_caretBlock")); // precondition
        return (ed, tb);
    }

    [AvaloniaTheory]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.LeftAlt)]
    public void ABareModifierPress_KeepsTheBlockCaret(Key modifier)
    {
        var (ed, tb) = BeforeTableCaret();
        var caretBefore = (TextPointer)typeof(RichEditor)
            .GetField("_caretPosition", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed)!;

        Press(ed, modifier);

        Assert.Same(tb, Field<Block>(ed, "_caretBlock"));
        var caretAfter = (TextPointer)typeof(RichEditor)
            .GetField("_caretPosition", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ed)!;
        Assert.Same(caretBefore.Paragraph, caretAfter.Paragraph);
    }

    // The whole point: the chord still has to work after its modifier half arrives.
    [AvaloniaFact]
    public void ShiftThenTab_StillOutdentsTheTable()
    {
        var (ed, tb) = BeforeTableCaret();
        tb.Indent = 40;

        Press(ed, Key.LeftShift, KeyModifiers.Shift); // the modifier half of the chord
        Press(ed, Key.Tab, KeyModifiers.Shift);       // ...then Tab

        Assert.Equal(20, tb.Indent);
    }

    [AvaloniaFact]
    public void ShiftThenSpace_StillIndentsTheTable()
    {
        var (ed, tb) = BeforeTableCaret();
        double indent = tb.Indent;

        Press(ed, Key.LeftShift, KeyModifiers.Shift);
        Press(ed, Key.Space, KeyModifiers.Shift);

        Assert.True(tb.Indent > indent);
    }

    // Same class: an image selection must survive the modifier half of Ctrl+C.
    [AvaloniaFact]
    public void ABareModifierPress_KeepsAnImageSelection()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>a</p>");
        var img = new ImageBlock { Width = 50, Height = 50 };
        ed.Document!.Blocks.Add(img);
        typeof(RichEditor).GetField("_selectedBlock", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ed, img);

        Press(ed, Key.LeftShift);

        Assert.Same(img, Field<Block>(ed, "_selectedBlock"));
    }

    // Guard: a real content key must still dismiss the block caret.
    [AvaloniaFact]
    public void AContentKey_StillDismissesTheBlockCaret()
    {
        var (ed, _) = BeforeTableCaret();

        Press(ed, Key.A);

        Assert.Null(Field<Block>(ed, "_caretBlock"));
    }
}
