using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditor.IsDocumentEmpty (issue #97): a host disables its own UI while the document has no content.
// "Empty" = only paragraphs with no characters; a table, picture, divider or inline object is content, so is
// whitespace. The value follows every edit, undo/redo, document replacement and NotifyDocumentChanged — on an
// editor never rendered too (TextChanged is flushed from Render; this must not wait for a frame).
public class IsDocumentEmptyTests
{
    [AvaloniaFact]
    public void ANewEditor_IsEmpty_AndLoadingText_IsNot()
    {
        var ed = new RichEditor();
        Assert.True(ed.IsDocumentEmpty);

        ed.LoadHtml("<p>abc</p>");
        Assert.False(ed.IsDocumentEmpty);

        ed.Clear();
        Assert.True(ed.IsDocumentEmpty);
    }

    [AvaloniaFact]
    public void Typing_Undo_Redo_FollowTheContent_WithoutARender()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p></p>");
        Assert.True(ed.IsDocumentEmpty);

        ed.FocusDocumentEnd();
        ed.InsertText("X");
        Dispatcher.UIThread.RunJobs();
        Assert.False(ed.IsDocumentEmpty);

        ed.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.True(ed.IsDocumentEmpty);

        ed.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.False(ed.IsDocumentEmpty);
    }

    [AvaloniaFact]
    public void DeletingTheLastCharacter_MakesItEmpty()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "a" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        using var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Editor.FocusDocumentEnd();
        Assert.False(host.Editor.IsDocumentEmpty);

        host.Key(Key.Back);
        host.Pump();

        Assert.True(host.Editor.IsDocumentEmpty);
    }

    [AvaloniaFact]
    public void Whitespace_IsContent()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = " " } } }); // HTML would collapse it
        var ed = new RichEditor { Document = doc };
        Assert.False(ed.IsDocumentEmpty);
    }

    [AvaloniaTheory]
    [InlineData("<table><tr><td></td></tr></table>")]
    [InlineData("<hr>")]
    [InlineData("<p><img src=\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==\" width=\"16\" height=\"16\"></p>")]
    public void Objects_AreContent(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        Assert.False(ed.IsDocumentEmpty);
    }

    [AvaloniaFact]
    public void AHostEditOfTheModel_IsSeenAfterNotifyDocumentChanged()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        Assert.False(ed.IsDocumentEmpty);

        ((Run)((Paragraph)ed.Document!.Blocks[0]).Inlines[0]).Text = "";
        ed.NotifyDocumentChanged();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ed.IsDocumentEmpty);
    }

    [AvaloniaFact]
    public void AChange_IsRaisedAsAPropertyChange_ForBindings()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p></p>");
        var seen = new List<bool>();
        ed.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichEditor.IsDocumentEmptyProperty) seen.Add((bool)e.NewValue!);
        };

        ed.FocusDocumentEnd();
        ed.InsertText("X");
        ed.InsertText("Y");
        Dispatcher.UIThread.RunJobs();
        ed.Undo(); // each public InsertText is its own undo step: "X" is still there
        Dispatcher.UIThread.RunJobs();
        ed.Undo();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { false, true }, seen); // once per real change, not per edit
    }
}
