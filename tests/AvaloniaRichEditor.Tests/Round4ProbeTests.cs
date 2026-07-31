using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Probes for the 1.0 full-sweep audit. Each targets a recursive walk that stops before
// inline-table cells (milestone B), the last place the A/B recursion was retrofitted.
public class Round4ProbeTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods = KeyModifiers.None)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    // Editor holding one paragraph whose single inline is an inline table; the table's only cell
    // gets an extra block (image) after its paragraph.
    private static (RichEditor ed, TableCell cell, ImageBlock img) InlineTableWithImageInCell()
    {
        var ed = new RichEditor();
        var doc = new FlowDocument();
        var host = new Paragraph();
        var it = new InlineTable { Table = new TableBlock(1, 1) };
        var cell = it.Table.Cells[0][0];
        cell.Para.Inlines.Add(new Run { Text = "c" });
        var img = new ImageBlock { Width = 30, Height = 30 };
        cell.Blocks.Add(img);
        host.Inlines.Add(new Run { Text = "a" });
        host.Inlines.Add(it);
        doc.Blocks.Add(host);
        ed.Document = doc;
        return (ed, cell, img);
    }

    // RemoveBlockAnywhere searches the document's top level and then recurses through *block* table
    // cells only, so a block that lives in an INLINE table's cell is never found: DeleteBlock pushes
    // an undo checkpoint, clears the selection and leaves the block on screen.
    [AvaloniaFact]
    public void DeletingABlockInsideAnInlineTableCell_RemovesIt()
    {
        var (ed, cell, img) = InlineTableWithImageInCell();
        Assert.Contains(img, cell.Blocks);

        typeof(RichEditor).GetField("_selectedBlock", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.SetValue(ed, img);
        Press(ed, Key.Delete);

        Assert.DoesNotContain(img, cell.Blocks);
    }

    // ParagraphSig folds an inline table's cell PARAGRAPHS into the host paragraph's signature, but
    // not the cell's other blocks. Resizing a block image inside such a cell changes the table's
    // measured box (and so the host line box) without changing the signature, so the cached host
    // layout is served stale.
    [AvaloniaFact]
    public void ResizingAnImageInsideAnInlineTableCell_ChangesTheHostParagraphSignature()
    {
        var (ed, _, img) = InlineTableWithImageInCell();
        var host = (Paragraph)ed.Document!.Blocks[0];

        var sig = typeof(RichEditor).GetMethod("ParagraphSig", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static)!;
        long before = (long)sig.Invoke(null, new object[] { host })!;
        img.Height = 300;
        long after = (long)sig.Invoke(null, new object[] { host })!;

        Assert.NotEqual(before, after);
    }
}
