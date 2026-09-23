using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Without a toolbar button the default UI had no way to set a quote: the right-click item exists only with
// ShowFormattingMenu, and there is no shortcut (user decision 2026-09-23: a button beside the lists).
public class ToolbarQuoteButtonTests
{
    [AvaloniaFact]
    public void TheQuoteButton_TogglesTheCaretParagraph_AndShowsItsState()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "text" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        var host = InteractionHost.Create(ed);
        host.Render();
        host.Click(new Avalonia.Point(5, 8)); // a caret in the paragraph
        var toolbar = new RichEditorToolbar { Target = ed };

        var quote = toolbar.GetLogicalDescendants().OfType<Button>()
            .Single(b => ToolTip.GetTip(b) as string == RichEditorLocalization.GetString("Quote"));
        quote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(p.IsQuote);
        Assert.True(host.Editor.CanUndo);

        quote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(p.IsQuote);
    }
}
