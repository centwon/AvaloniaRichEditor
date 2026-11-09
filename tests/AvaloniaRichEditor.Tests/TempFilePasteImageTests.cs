using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Word and HWP clipboard HTML reference the pictures the copy itself just wrote under %TEMP% — as file:/// or, from some
// paths, ms-clipboard-file:. With local file images off (the paste default), refusing those dropped every picture pasted
// from those applications. A paste (the public ParseHtml) takes them from %TEMP% only, as the WinUI control does; the
// editor's own LoadHtml/InsertHtml do not, and anything outside %TEMP% stays blocked (2026-11-09).
public class TempFilePasteImageTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static string TempPicture()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AvaloniaRichEditor.Tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "paste-temp-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, Png);
        return path;
    }

    private static int Pictures(FlowDocument d)
        => d.Blocks.OfType<ImageBlock>().Count() + d.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>().Count();

    private static string Html(string src) => $"<p>a</p><img src=\"{src}\" width=\"300\" height=\"300\"><p>b</p>";

    [AvaloniaTheory]
    [InlineData("file:")]
    [InlineData("ms-clipboard-file:")]
    public void APaste_TakesAPictureFromTemp_WithLocalFilesOff(string scheme)
    {
        string path = TempPicture();
        try
        {
            string src = scheme + new Uri(path).AbsoluteUri.Substring("file:".Length);
            Assert.Equal(1, Pictures(HtmlDocumentFormatter.ParseHtml(Html(src), allowLocalFileImages: false)));
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void TheEditorsInsertHtml_DoesNotTakeTheTempExemption()
    {
        string path = TempPicture();
        try
        {
            var ed = new RichEditor { AllowLocalFileImages = false };
            ed.LoadHtml("<p>x</p>");
            ed.InsertHtml(Html(new Uri(path).AbsoluteUri));
            Assert.Equal(0, Pictures(ed.Document!));
        }
        finally { File.Delete(path); }
    }
}
