using System;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.Formatting.cs (2026-09-23).
public class FormattingAuditTests
{
    private static InteractionHost Host()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "hello world" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Click(new Avalonia.Point(10, 8)); // a caret: every paragraph command returns early without one
        return host;
    }

    // A paragraph command that changes nothing — outdenting at indent 0 (Shift+Tab's fallback), aligning left what is
    // left already — left an undo step that undid nothing: Ctrl+Z then seemed to do nothing.
    [AvaloniaTheory]
    [InlineData("outdent")]
    [InlineData("align")]
    [InlineData("spacing")]
    [InlineData("height")]
    [InlineData("removelist")]
    public void AParagraphCommandThatChangesNothing_LeavesNoUndoStep(string cmd)
    {
        var host = Host();
        var ed = host.Editor;
        var p = (Paragraph)ed.Document!.Blocks[0];
        Assert.False(ed.CanUndo); // precondition

        Action run = cmd switch
        {
            "outdent" => () => ed.Indent(-20),
            "align" => () => ed.SetTextAlignment(p.TextAlignment),
            "spacing" => () => ed.SetLineSpacing(p.LineSpacing),
            "height" => () => ed.SetLineHeight(p.LineHeight),
            _ => ed.RemoveList,
        };
        run();

        Assert.False(ed.CanUndo);
    }

    // Control: a command that does change the paragraph still records its step.
    [AvaloniaFact]
    public void AParagraphCommandThatChangesSomething_StillLeavesAnUndoStep()
    {
        var host = Host();
        host.Editor.SetTextAlignment(TextAlignment.Center);
        Assert.True(host.Editor.CanUndo);
    }
}
