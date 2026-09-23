using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.FindReplace.cs (2026-09-23).
public class FindReplaceAuditTests
{
    private static InteractionHost Host()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "hello world hello" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Click(new Avalonia.Point(5, 8));
        return host;
    }

    // The find bar checks IsReadOnly before replacing; the public methods it calls did not, so a host driving
    // them (its own find UI) rewrote a viewer's document.
    [AvaloniaFact]
    public void ReplaceAll_DoesNothingWhenReadOnly()
    {
        var host = Host();
        host.Editor.IsReadOnly = true;

        int n = host.Editor.ReplaceAll("hello", "bye", false);

        Assert.Equal(0, n);
        Assert.Contains("hello world hello", host.Editor.GetPlainText());
    }

    [AvaloniaFact]
    public void ReplaceNext_DoesNotReplaceWhenReadOnly()
    {
        var host = Host();
        host.Editor.FindNext("hello", false); // the selection is on a match
        host.Editor.IsReadOnly = true;

        host.Editor.ReplaceNext("hello", "bye", false);

        Assert.Contains("hello world hello", host.Editor.GetPlainText());
    }

    // Replace All with nothing to replace pushed an undo step and flagged the document modified.
    [AvaloniaFact]
    public void ReplaceAll_WithNoMatch_LeavesTheDocumentUnmodified()
    {
        var host = Host();
        host.Editor.MarkSaved(); // a raw Document assignment counts as a change by design
        Assert.False(host.Editor.IsModified); // precondition

        int n = host.Editor.ReplaceAll("absent", "x", false);

        Assert.Equal(0, n);
        Assert.False(host.Editor.CanUndo);
        Assert.False(host.Editor.IsModified);
    }

    // Control: with matches it replaces them all, as one undo step.
    [AvaloniaFact]
    public void ReplaceAll_WithMatches_ReplacesThemAsOneStep()
    {
        var host = Host();

        int n = host.Editor.ReplaceAll("hello", "bye", false);

        Assert.Equal(2, n);
        Assert.Contains("bye world bye", host.Editor.GetPlainText());
        host.Editor.Undo();
        Assert.Contains("hello world hello", host.Editor.GetPlainText());
    }
}
