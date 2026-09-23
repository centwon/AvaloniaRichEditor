using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.Images.cs (2026-09-23).
public class ImagesAuditTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    // A decodable 1x1 PNG.
    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static void Call(RichEditor ed, string name, params object[] args)
        => typeof(RichEditor).GetMethod(name, NP)!.Invoke(ed, args);

    // "ab" + an inline picture at the end, caret after the picture (End) — where a right-click on the picture's
    // trailing half leaves it too.
    private static (InteractionHost host, Paragraph p, InlineImage img) HostWithTrailingPicture()
    {
        var img = new InlineImage { Width = 16, Height = 16 };
        img.SetImageData(Png, "image/png");
        var p = new Paragraph { Inlines = { new Run { Text = "ab" }, img } };
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        doc.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "next" } } });
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Click(new Avalonia.Point(5, 8));
        host.Key(Key.End);
        Assert.Same(p, host.Caret.Paragraph); // precondition
        Assert.Equal(3, host.Caret.Offset);  // precondition: after the picture
        return (host, p, img);
    }

    // Deleting the picture from its menu left the caret one past the paragraph's end.
    [AvaloniaFact]
    public void DeletingAnInlinePicture_KeepsTheCaretInsideItsParagraph()
    {
        var (host, p, img) = HostWithTrailingPicture();

        Call(host.Editor, "DeleteInlineImage", p, img);
        host.Type("Z");

        Assert.Equal("abZ", string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)));
    }

    // The same when the picture is promoted to a block (글자처럼 취급 off).
    [AvaloniaFact]
    public void MakingAnInlinePictureABlock_KeepsTheCaretInsideItsParagraph()
    {
        var (host, p, img) = HostWithTrailingPicture();

        Call(host.Editor, "ConvertInlineImageToBlock", p, img);
        host.Type("Z");

        Assert.Equal("abZ", string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)));
    }

    // "Original size" on a picture already at its natural size left an undo step that undid nothing.
    [AvaloniaFact]
    public void OriginalSize_OnAPictureAtItsNaturalSize_LeavesNoUndoStep()
    {
        var img = new ImageBlock { Width = 1, Height = 1 }; // the PNG is 1x1
        img.SetImageData(Png, "image/png");
        var doc = new FlowDocument();
        doc.Blocks.Add(img);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        Assert.False(host.Editor.CanUndo); // precondition

        Call(host.Editor, "ResetImageSize", img);

        Assert.False(host.Editor.CanUndo);
    }
}
