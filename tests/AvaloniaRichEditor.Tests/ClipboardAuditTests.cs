using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.Clipboard.cs (2026-09-23).
public class ClipboardAuditTests
{
    // Pasting over a selection replaces it — the text, rich and inline-picture paths all delete it first. The
    // spreadsheet (TSV -> table) path did not: the table went in after the caret's paragraph and the selected
    // text stayed.
    [AvaloniaFact]
    public async Task PastingSpreadsheetCells_OverASelection_ReplacesIt()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "keep REPLACE keep" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        var clipboard = TopLevel.GetTopLevel(host.Editor)?.Clipboard;
        Assert.NotNull(clipboard); // precondition: the headless platform has one
        await clipboard!.SetTextAsync("a\tb\nc\td");
        Assert.Equal("a\tb\nc\td", await clipboard.TryGetTextAsync()); // precondition: it round-trips

        host.Editor.FindNext("REPLACE", true); // selects the word
        await host.Editor.PasteFromClipboardAsync();

        Assert.Single(host.Editor.Document!.Blocks.OfType<TableBlock>()); // precondition: it became a table
        Assert.DoesNotContain("REPLACE", host.Editor.GetPlainText());
        host.Editor.Undo(); // one step back to the text, selection deleted and table inserted together
        Assert.Contains("keep REPLACE keep", host.Editor.GetPlainText());
        Assert.Empty(host.Editor.Document!.Blocks.OfType<TableBlock>());
    }

    // What a copy hands other applications (CF_HTML) is built from a trimmed copy of the selected paragraphs.
    // That copy took a hand-picked list of paragraph fields and missed the margins, which the HTML writer does
    // emit — so a spaced paragraph pasted into Word lost its spacing. Paragraph.CopyFormatFrom is the one list.
    [AvaloniaFact]
    public void TheCopyForOtherApps_KeepsParagraphMargins()
    {
        var p = new Paragraph { MarginTop = 20, MarginBottom = 12, Inlines = { new Run { Text = "spaced" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Editor.FindNext("spaced", true);

        var copy = (FlowDocument)typeof(RichEditor).GetMethod("BuildSelectionDocument",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(host.Editor, null)!;

        var cp = Assert.IsType<Paragraph>(Assert.Single(copy.Blocks));
        Assert.Equal((20.0, 12.0), (cp.MarginTop, cp.MarginBottom));
    }

    // A decodable 2x2 PNG — big enough (the in-app image format aside) that a clipboard picture pastes as a block.
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // The same for a picture on the clipboard (a screenshot): it went in beside the selection, which stayed.
    [AvaloniaFact]
    public async Task PastingAPicture_OverASelection_ReplacesIt()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "keep REPLACE keep" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        var clipboard = TopLevel.GetTopLevel(host.Editor)!.Clipboard!;
        var item = DataTransferItem.Create(DataFormat.CreateBytesPlatformFormat("PNG"), Png);
        var dt = new DataTransfer();
        dt.Add(item);
        await clipboard.SetDataAsync(dt);

        host.Editor.FindNext("REPLACE", true);
        await host.Editor.PasteFromClipboardAsync();

        Assert.Single(host.Editor.Document!.Blocks.OfType<ImageBlock>()); // precondition: the picture went in
        Assert.DoesNotContain("REPLACE", host.Editor.GetPlainText());
        // Where the selection was — between its two halves (user decision 2026-09-23), not below the paragraph.
        var blocks = host.Editor.Document!.Blocks;
        int ii = blocks.IndexOf(blocks.OfType<ImageBlock>().Single());
        Assert.Equal("keep ", string.Concat(((Paragraph)blocks[ii - 1]).Inlines.OfType<Run>().Select(r => r.Text)));
        Assert.Equal(" keep", string.Concat(((Paragraph)blocks[ii + 1]).Inlines.OfType<Run>().Select(r => r.Text)));
        host.Editor.Undo();
        Assert.Contains("keep REPLACE keep", host.Editor.GetPlainText());
        Assert.Empty(host.Editor.Document!.Blocks.OfType<ImageBlock>());
    }
}
