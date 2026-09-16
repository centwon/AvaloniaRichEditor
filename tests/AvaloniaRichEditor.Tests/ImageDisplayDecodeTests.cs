using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Pictures are drawn from a bitmap decoded at the size they are DRAWN, not their source size
/// (2026-09-16, from the WinUI port, which measured it: six 4000x3000 photos shown 240 px wide held ~280 MB of
/// decoded pixels there; after, the cost followed the display size and not the source).
/// <para>Here the full decode was the model's own lazy <c>Image</c> getter, which rendering called — so the
/// source-size bitmap was pinned on the document element (and, through Clone, on every undo snapshot).</para></summary>
public class ImageDisplayDecodeTests
{
    private const double W = 600, H = 800;

    // A solid-colour, bottom-up 24-bit BMP: trivially built by hand, decoded like any photo.
    internal static byte[] SolidBmp(int w, int h, byte r, byte g, byte b)
    {
        int stride = (w * 3 + 3) & ~3, size = 54 + stride * h;
        var bytes = new byte[size];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BitConverter.GetBytes(size).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(w).CopyTo(bytes, 18);
        BitConverter.GetBytes(h).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(stride * h).CopyTo(bytes, 34);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = 54 + y * stride + x * 3;
                bytes[o] = b; bytes[o + 1] = g; bytes[o + 2] = r;
            }
        return bytes;
    }

    private static RenderTargetBitmap Render(RichEditor ed)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        var rtb = new RenderTargetBitmap(new PixelSize((int)W, (int)H));
        rtb.Render(ed);
        return rtb;
    }

    [AvaloniaFact]
    public void DrawingPictures_DoesNotPinASourceSizeBitmapOnTheDocument()
    {
        var block = new ImageBlock { Width = 200, Height = 150 };
        block.SetImageData(SolidBmp(2000, 1500, 255, 0, 0), "image/bmp");
        var inline = new InlineImage { Width = 80, Height = 60 };
        inline.SetImageData(SolidBmp(1600, 1200, 0, 0, 255), "image/bmp");
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "a" });
        p.Inlines.Add(inline);
        doc.Blocks.Add(p);
        doc.Blocks.Add(block);
        var ed = new RichEditor { Document = doc };

        using var _ = Render(ed);

        Assert.Null(block.CachedBitmap);
        Assert.Null(inline.CachedBitmap);
    }
}
