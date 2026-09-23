using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.DragBlock.cs (2026-09-23).
public class DragBlockAuditTests
{
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static (RichEditor ed, Paragraph target) Editor(Block obj)
    {
        var target = new Paragraph { Inlines = { new Run { Text = "drop here" } } };
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "top" } } });
        doc.Blocks.Add(obj);
        doc.Blocks.Add(target);
        var ed = new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous };
        InteractionHost.Create(ed).Render();
        return (ed, target);
    }

    // Ctrl+drag makes a NEW table or picture. With AllowTables / AllowImages off the editor creates neither —
    // not by the menu, not by paste (AdaptToCapabilities) — but a drag copy did.
    [AvaloniaFact]
    public void CopyDraggingATable_WithTablesOff_AddsNoTable()
    {
        var tb = new TableBlock(1, 1);
        var (ed, target) = Editor(tb);
        ed.AllowTables = false;

        ed.DropObject(tb, new TextPointer(target, target.Inlines.OfType<Run>().First().Text!.Length), copy: true);

        Assert.Single(ed.Document!.Blocks.OfType<TableBlock>());
    }

    [AvaloniaFact]
    public void CopyDraggingAPicture_WithImagesOff_AddsNoPicture()
    {
        var img = new ImageBlock { Width = 20, Height = 20 };
        img.SetImageData(Png, "image/png");
        var (ed, target) = Editor(img);
        ed.AllowImages = false;

        ed.DropObject(img, new TextPointer(target, 9), copy: true);

        Assert.Single(ed.Document!.Blocks.OfType<ImageBlock>());
    }

    // Control: MOVING an existing table stays allowed — editing a table the document already has is not
    // creating one (the same line the row/column commands draw).
    [AvaloniaFact]
    public void MoveDraggingATable_WithTablesOff_StillMovesIt()
    {
        var tb = new TableBlock(1, 1);
        var (ed, target) = Editor(tb);
        ed.AllowTables = false;

        bool changed = ed.DropObject(tb, new TextPointer(target, 9), copy: false);

        Assert.True(changed);
        Assert.Single(ed.Document!.Blocks.OfType<TableBlock>());
    }
}
