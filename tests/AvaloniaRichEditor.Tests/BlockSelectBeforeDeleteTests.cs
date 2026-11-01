using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Backspace / Delete next to a picture or a table selects it first; the next press deletes it (user decision, 2026-11-01).
// One press used to delete a whole table at once.
public class BlockSelectBeforeDeleteTests
{
    private static void Press(RichEditor ed, Key key)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    private static void PlaceCaret(RichEditor ed, Paragraph p, int offset)
    {
        var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(RichEditor).GetField("_caretPosition", f)!.SetValue(ed, new TextPointer(p, offset));
        typeof(RichEditor).GetField("_selectionStart", f)!.SetValue(ed, new TextPointer(p, offset));
        typeof(RichEditor).GetField("_selectionEnd", f)!.SetValue(ed, new TextPointer(p, offset));
    }

    private static (RichEditor ed, Paragraph before, Block block, Paragraph after) Doc(Block block)
    {
        var before = TestHelpers.Para(new Run { Text = "A" });
        var after = TestHelpers.Para(new Run { Text = "B" });
        var ed = new RichEditor { Document = TestHelpers.Doc(before, block, after) };
        return (ed, before, block, after);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Backspace_AfterABlock_SelectsItFirst_ThenDeletesIt(bool table)
    {
        var (ed, _, block, after) = Doc(table ? new TableBlock(1, 1) : new ImageBlock());
        PlaceCaret(ed, after, 0);

        Press(ed, Key.Back);
        Assert.Contains(block, ed.Document!.Blocks);
        Assert.False(ed.CanUndo);
        Assert.False(ed.IsModified);

        Press(ed, Key.Back);
        Assert.DoesNotContain(block, ed.Document!.Blocks);
        Assert.True(ed.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Delete_BeforeABlock_SelectsItFirst_ThenDeletesIt(bool table)
    {
        var (ed, before, block, _) = Doc(table ? new TableBlock(1, 1) : new ImageBlock());
        PlaceCaret(ed, before, 1);

        Press(ed, Key.Delete);
        Assert.Contains(block, ed.Document!.Blocks);

        Press(ed, Key.Delete);
        Assert.DoesNotContain(block, ed.Document!.Blocks);
    }

    // Any other key in between drops the selection: the next Backspace selects again rather than deleting.
    [AvaloniaFact]
    public void ATypedKeyInBetween_CancelsTheSelection()
    {
        var (ed, _, block, after) = Doc(new TableBlock(1, 1));
        PlaceCaret(ed, after, 0);

        Press(ed, Key.Back);
        Press(ed, Key.Right);
        PlaceCaret(ed, after, 0);
        Press(ed, Key.Back);
        Assert.Contains(block, ed.Document!.Blocks);
    }
}
