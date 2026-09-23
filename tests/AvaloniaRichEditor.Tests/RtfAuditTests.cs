using System.Diagnostics;
using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RtfDocumentFormatter.cs (2026-09-23). RTF arrives by paste — untrusted input — so the numbers in it
// are whatever the source says, including values no word processor would write.
public class RtfAuditTests
{
    // Hostile or corrupt sizes must not wreck the editor: the document still lays out and draws, quickly.
    [AvaloniaTheory]
    [InlineData(@"{\rtf1\ansi\fs2000000000 huge}")]                  // a billion-point font
    [InlineData(@"{\rtf1\ansi\pard\sl2000000000\slmult1 tall\par}")]   // a ten-million-times line
    [InlineData(@"{\rtf1\ansi\pard\sl-240\slmult1 negative\par}")]     // a negative proportional line
    [InlineData(@"{\rtf1\ansi\pard\sl2000000000\slmult0 exact\par}")]  // a 130-million-pixel exact line
    public void AbsurdSizes_StillLayOutAndDraw(string rtf)
    {
        var doc = RtfDocumentFormatter.Parse(rtf);
        var p = doc.Blocks.OfType<Paragraph>().First(x => x.Inlines.OfType<Run>().Any(r => r.Text!.Length > 0));
        var run = p.Inlines.OfType<Run>().First();
        // Precondition: the absurd value really reached the model (measured: 1e9 pt, -1.2, 1e7, 1.3e8 px) — the
        // layout, not the reader, is what copes with it, so this guards the layout.
        Assert.True(run.FontSize > 1000 || p.LineSpacing is < 0 or > 1000 || p.LineHeight > 1_000_000, "the value never reached the model");
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });

        var sw = Stopwatch.StartNew();
        host.Render();
        host.Editor.InvalidateMeasure();
        host.Render();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5000, $"layout took {sw.ElapsedMilliseconds} ms");
        Assert.True(double.IsFinite(host.Editor.DesiredSize.Height), $"height {host.Editor.DesiredSize.Height}");
        Assert.True(host.Editor.DesiredSize.Height < 1_000_000, $"height {host.Editor.DesiredSize.Height}");
    }
}
