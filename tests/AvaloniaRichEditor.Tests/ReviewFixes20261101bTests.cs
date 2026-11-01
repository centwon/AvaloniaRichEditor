using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Immutable;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Second review of src (2026-11-01): no-op edits that checkpointed, a raw '\r' from Replace, HTML colour brushes,
// and RTF \uc fallback skipping across a line break.
public class ReviewFixes20261101bTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static RichEditor Editor(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        ed.FocusDocumentEnd();
        return ed;
    }

    private static Paragraph Para(RichEditor ed, int i)
        => (Paragraph)ed.Document!.Blocks.Where(b => b is Paragraph).ElementAt(i);

    // Nothing selected: Ctrl+X pushed an undo step anyway and marked the freshly loaded document modified.
    [AvaloniaFact]
    public void CtrlX_WithNothingSelected_IsNotAnEdit()
    {
        var ed = Editor("<p>abc</p>");
        Press(ed, Key.X, KeyModifiers.Control);

        Assert.Equal("abc", Para(ed, 0).Text());
        Assert.False(ed.IsModified);
        Assert.False(ed.CanUndo);
    }

    // TypeText normalizes line endings; the replace path did not, so "\r\n" left a raw '\r' in the run.
    [AvaloniaFact]
    public void ReplaceAll_NormalizesLineEndings()
    {
        var ed = Editor("<p>abc</p>");
        Assert.Equal(1, ed.ReplaceAll("b", "x\r\ny", matchCase: true));

        Assert.DoesNotContain('\r', Para(ed, 0).Text());
        Assert.Equal("ax\nyc", Para(ed, 0).Text());
    }

    // CLAUDE.md rule 8: a reader's brushes are immutable (no thread affinity), as the JSON and RTF readers' are.
    [AvaloniaTheory]
    [InlineData("color:#ff0000")]
    [InlineData("color:rgb(255, 0, 0)")]
    public void HtmlColours_AreImmutableBrushes(string style)
    {
        var doc = HtmlDocumentFormatter.ParseHtml($"<p><span style=\"{style}\">x</span></p>");
        var run = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text == "x");
        Assert.IsType<ImmutableSolidColorBrush>(run.Foreground);
    }

    // A line break between \uN and its fallback was counted as the fallback, and the real "?" went into the text.
    [AvaloniaFact]
    public void UnicodeFallback_AfterALineBreak_IsSkipped()
    {
        var doc = RtfDocumentFormatter.Parse("{\\rtf1\\ansi a\\u1234\r\n?b\\par}");
        string text = string.Concat(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).Select(r => r.Text));
        Assert.Equal("a\u04D2b", text);
    }
}
