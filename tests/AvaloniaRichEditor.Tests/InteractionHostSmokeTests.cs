using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Guards the harness itself. If these fail, every interaction test built on InteractionHost is
// measuring nothing, so they assert the three things the harness promises: input reaches the control,
// coordinates land where the tests think they do, and typed text edits the document.
public class InteractionHostSmokeTests
{
    private static RichEditor Editor(string html = "<p>hello world</p>")
    {
        var ed = new RichEditor { PageSize = RichEditorPageSize.Continuous };
        ed.LoadHtml(html);
        return ed;
    }

    [AvaloniaFact]
    public void AShownEditorHasFocus()
    {
        var host = InteractionHost.Create(Editor());

        Assert.True(host.EditorHasFocus);
    }

    [AvaloniaFact]
    public void ClickingMovesTheCaretToTheClickedCharacter()
    {
        var ed = Editor();
        var host = InteractionHost.Create(ed);
        var para = ed.Document!.Blocks[0];

        // Far to the right of "hello world" on its own line: the caret lands at the end of the text,
        // which is only true if the click arrived in the editor's own coordinate space.
        host.Click(new Point(600, 8));

        Assert.Same(para, host.Caret.Paragraph);
        Assert.Equal("hello world".Length, host.Caret.Offset);
    }

    [AvaloniaFact]
    public void TypedTextIsInsertedAtTheCaret()
    {
        var ed = Editor();
        var host = InteractionHost.Create(ed);

        host.Click(new Point(600, 8));
        host.Type("!");

        Assert.Equal("hello world!", ((Documents.Paragraph)ed.Document!.Blocks[0]).Text());
    }

    [AvaloniaFact]
    public void ADragSelectsTheTextItSweeps()
    {
        var ed = Editor();
        var host = InteractionHost.Create(ed);

        host.Drag(new Point(0, 8), new Point(300, 8), new Point(600, 8));

        Assert.False(host.Selection.IsEmpty);
        Assert.Equal("hello world", host.SelectedText);
    }

    [AvaloniaFact]
    public void ArrowKeysReachTheControl()
    {
        var ed = Editor();
        var host = InteractionHost.Create(ed);
        host.Click(new Point(0, 8));

        host.Key(Key.Right);
        host.Key(Key.Right);

        Assert.Equal(2, host.Caret.Offset);
    }
}
