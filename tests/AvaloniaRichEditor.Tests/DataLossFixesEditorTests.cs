using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Data-loss fixes added for 1.3.0 (2026-10-15) that go through the editor.
public class DataLossFixesEditorTests
{
    private static List<Block>? CaptureBlockStructure(RichEditor ed, TextRange range) =>
        (List<Block>?)typeof(RichEditor).GetMethod("CaptureBlockStructure", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ed, new object[] { range });

    private static string TextOf(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    // A selection from the middle of one paragraph, across a table, into the middle of another captured the two end
    // paragraphs WHOLE — an in-app paste brought back their unselected text. It takes what is selected.
    [AvaloniaFact]
    public void Copy_AcrossATable_TrimsTheEndParagraphsToTheSelection()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>hello</p><table><tr><td>x</td></tr></table><p>world</p>");
        var paras = ed.Document!.Blocks.OfType<Paragraph>().ToList();
        Paragraph first = paras.First(p => TextOf(p) == "hello"), last = paras.First(p => TextOf(p) == "world");

        var blocks = CaptureBlockStructure(ed, new TextRange(new TextPointer(first, 2), new TextPointer(last, 3)))!;

        Assert.Equal("llo", TextOf((Paragraph)blocks.First()));
        Assert.Contains(blocks, b => b is TableBlock);
        Assert.Equal("wor", TextOf((Paragraph)blocks.Last()));
    }
}
