using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Audit of RichEditor.Input.cs (2026-09-23).
public class InputAuditTests
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly byte[] Png = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // "ab" + a selected inline picture, the caret after it (where a click on its trailing half leaves it).
    private static (InteractionHost host, Paragraph p) HostWithSelectedTrailingPicture()
    {
        var img = new InlineImage { Width = 16, Height = 16 };
        img.SetImageData(Png, "image/png");
        var p = new Paragraph { Inlines = { new Run { Text = "ab" }, img } };
        var doc = new FlowDocument();
        doc.Blocks.Add(p);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        host.Click(new Point(5, 8));
        host.Key(Key.End);
        Assert.Equal(3, host.Caret.Offset); // precondition: after the picture
        typeof(RichEditor).GetField("_selectedInline", NP)!.SetValue(host.Editor, ((Paragraph, InlineImage)?)(p, img));
        return (host, p);
    }

    private static string Text(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

    // Deleting a selected inline picture with Backspace/Delete, or cutting it with Ctrl+X, took its character out
    // but left the caret where it was — one past the paragraph's end when it sat after the picture — so the next
    // keys typed nowhere. The menu's Delete had the same defect (ImagesAuditTests).
    [AvaloniaTheory]
    [InlineData(Key.Back, RawInputModifiers.None)]
    [InlineData(Key.Delete, RawInputModifiers.None)]
    [InlineData(Key.X, RawInputModifiers.Control)]
    public void RemovingASelectedInlinePicture_KeepsTheCaretInItsParagraph(Key key, RawInputModifiers mods)
    {
        var (host, p) = HostWithSelectedTrailingPicture();

        host.Key(key, mods);
        host.Type("Z");

        Assert.DoesNotContain(p.Inlines, i => i is InlineImage); // precondition: it went
        Assert.Equal("abZ", Text(p));
    }
}
