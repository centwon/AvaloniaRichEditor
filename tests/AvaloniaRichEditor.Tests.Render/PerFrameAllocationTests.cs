using System;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

// A repaint with nothing changed must cost what the VIEWPORT costs, not what the document does. The render walk built
// every paragraph's text before its cull check — a string per paragraph of the whole document, every frame: about 6 MB
// a frame at 4,000 paragraphs (found 2026-11-02 by the scroll probe in LayoutCacheProbeTests). Checked as a property —
// four times the document, about the same allocation — so the bound does not depend on the machine or the fonts.
public class PerFrameAllocationTests(ITestOutputHelper output)
{
    private static string Html(int paras)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paras; i++)
            sb.Append("<p>문단 ").Append(i).Append(" — <b>굵게</b> <i>기울임</i> Lorem ipsum dolor sit amet, consectetur ")
              .Append("adipiscing elit, sed do eiusmod tempor incididunt. 한글과 영문이 섞인 긴 문장으로 줄바꿈을 유발합니다.</p>");
        return sb.ToString();
    }

    // Bytes one unchanged frame allocates, in an 800x600 window whose ScrollViewer is what render culling keys on.
    private static long OneFrame(int paras)
    {
        var ed = new RichEditor { Document = HtmlDocumentFormatter.ParseHtml(Html(paras)) };
        var scroller = new Avalonia.Controls.ScrollViewer { Content = ed };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = scroller };
        window.Show();
        void Frame()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var rtb = new RenderTargetBitmap(new PixelSize(800, 600));
            rtb.Render(window);
        }
        Frame(); Frame(); // shape what is visible, then a settled frame
        long before = GC.GetAllocatedBytesForCurrentThread();
        Frame();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        window.Content = null; // never Close(): the last headless top-level takes the dispatcher with it
        return bytes;
    }

    [AvaloniaFact]
    public void AnUnchangedFrame_CostsTheViewport_NotTheDocument()
    {
        // An untemplated ScrollViewer has no viewport, so culling would see nothing (see LayoutCacheProbeTests.D).
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        try
        {
            long small = OneFrame(1000), large = OneFrame(4000);
            output.WriteLine($"one frame: 1,000 paragraphs {small / 1024} KB, 4,000 paragraphs {large / 1024} KB");
            // Before the fix: about 1.5 MB against 6 MB. A per-block walk that allocates nothing may still differ a
            // little (dictionary lookups, the window's own visuals); four times the text must not.
            Assert.True(large < small * 2 + 256 * 1024,
                $"an unchanged frame allocates with the document: {small / 1024} KB at 1,000 paragraphs, {large / 1024} KB at 4,000");
        }
        finally { Application.Current!.Styles.Remove(theme); }
    }
}
