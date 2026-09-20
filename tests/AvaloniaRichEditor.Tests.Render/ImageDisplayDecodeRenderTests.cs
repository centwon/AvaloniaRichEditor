using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests.Render;

/// <summary>Pictures are decoded at the size they are DRAWN (2026-09-16, from the WinUI port, which measured
/// it: six 4000x3000 photos shown 240 px wide held ~280 MB of decoded pixels). Real codecs are needed to see a
/// decoded size — the main project's no-op backend decodes everything to 1x1 — hence this project.
/// <para>The display cache is internal and this project has no internals access, so it is read by reflection.</para></summary>
public class ImageDisplayDecodeRenderTests
{
    private const double W = 600, H = 800;
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

    // A solid-colour, bottom-up 24-bit BMP. Every test uses its OWN size so no two share bytes by accident.
    private static byte[] SolidBmp(int w, int h, byte r, byte g, byte b)
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

    private static PixelSize? DisplayBitmapSize(RichEditor ed, byte[] raw)
    {
        var cache = typeof(RichEditor).GetField("_displayImages", NP)!.GetValue(ed)!;
        var bmp = (Bitmap?)cache.GetType().GetMethod("CachedFor", NP)!.Invoke(cache, [raw]);
        return bmp?.PixelSize;
    }

    private static (RichEditor ed, byte[] raw, ImageBlock block) EditorWithPicture(int srcW, int srcH, double w, double h)
    {
        var block = new ImageBlock { Width = w, Height = h };
        block.SetImageData(SolidBmp(srcW, srcH, 255, 0, 0), "image/bmp");
        var doc = new FlowDocument();
        doc.Blocks.Add(block);
        return (new RichEditor { Document = doc }, block.RawBytes!, block);
    }

    private static void Render(Visual root, Control ed)
    {
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        using var rtb = new RenderTargetBitmap(new PixelSize((int)W, (int)H));
        rtb.Render(root);
    }

    [AvaloniaFact]
    public void APictureIsDecodedAtTheSizeItIsDrawn_NotItsSourceSize()
    {
        var (ed, raw, block) = EditorWithPicture(2000, 1500, 200, 150);

        Render(ed, ed);

        var px = DisplayBitmapSize(ed, raw)!.Value;
        Assert.InRange(px.Width, 200, 251);  // 200 drawn, plus the cache's 25% headroom
        Assert.InRange(px.Height, 150, 189);
        Assert.Null(block.CachedBitmap); // and nothing source-sized left on the document
    }

    // Drawn first at 1x, then zoomed to 3x in the same editor: the small decode must be replaced by a sharper
    // one. (Starting at 3x would never exercise the upgrade — a first decode is sized right regardless.)
    [AvaloniaFact]
    public void ZoomingIn_ReplacesTheDecodeWithASharperOne()
    {
        var (ed, raw, _) = EditorWithPicture(2002, 1500, 200, 150);
        // The zoom RichEditorView applies: a LayoutTransform around the editor, inside a window.
        var zoom = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1, 1), Child = ed };
        var window = new Window { Width = W * 3, Height = H * 3, Content = zoom };
        window.Show();
        try
        {
            void Draw()
            {
                window.UpdateLayout();
                using var rtb = new RenderTargetBitmap(new PixelSize((int)W, (int)H));
                rtb.Render(ed);
            }

            Draw();
            var before = DisplayBitmapSize(ed, raw)!.Value;
            Assert.True(before.Width <= 251, $"at 1x: {before.Width}x{before.Height}");

            zoom.LayoutTransform = new ScaleTransform(3, 3);
            Draw();
            var after = DisplayBitmapSize(ed, raw)!.Value;
            Assert.True(after.Width >= 600 && after.Height >= 450, $"at 3x zoom: {after.Width}x{after.Height}");
        }
        finally { window.Close(); }
    }

    // A picture resized out of its source's proportions (an <img width height>, the edge handles) is drawn
    // STRETCHED into its rect. Decoding to fit INSIDE the rect kept the aspect and came out short on the long
    // axis — 400×100 from a 4:3 source decoded ~167 px wide — so the picture was soft along it. From the WinUI
    // port (2026-09-19), where the same decode also re-ran every frame; here the cache compares against the
    // target it asked for, so it only blurred.
    [AvaloniaTheory]
    [InlineData(400, 100)]
    [InlineData(100, 400)]
    public void AStretchedPicture_IsDecodedSharpOnBothAxes(double w, double h)
    {
        var (ed, raw, _) = EditorWithPicture(2003 + (int)w, 1500, w, h);

        Render(ed, ed);

        var px = DisplayBitmapSize(ed, raw)!.Value;
        Assert.True(px.Width >= w && px.Height >= h, $"{w}x{h} drawn from a {px.Width}x{px.Height} bitmap");
    }

    // Alternating 1-pixel red and white ROWS: the finest vertical detail a picture can have.
    private static byte[] StripedBmp(int w, int h)
    {
        var bytes = SolidBmp(w, h, 255, 255, 255);
        int stride = (w * 3 + 3) & ~3;
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x++)
            {
                int o = 54 + y * stride + x * 3;
                bytes[o] = 0; bytes[o + 1] = 0; // B, G = 0: red
            }
        return bytes;
    }

    // A squashed picture still shrinks its short axis several times when drawn. Sampling that skips source
    // rows turns fine detail into noise — in the port "SHARP" was drawn as "SHAKI'" until the draw filtered.
    // Oracle: 1px stripes shrunk ~17:1 average to one even pink (an exact 16:1 samples every row pair at the
    // same phase and passes by luck — the port's first version of this test did).
    [AvaloniaFact]
    public void ASquashedPicture_IsFiltered_NotAliased()
    {
        var block = new ImageBlock { Width = 400, Height = 47 };
        block.SetImageData(StripedBmp(401, 800), "image/bmp");
        var doc = new FlowDocument();
        doc.Blocks.Add(block);
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Size(W, double.PositiveInfinity));
        ed.Arrange(new Rect(0, 0, W, H));
        using var rtb2 = new RenderTargetBitmap(new PixelSize((int)W, (int)H));
        rtb2.Render(ed);

        int stride = (int)W * 4;
        var buf = new byte[stride * (int)H];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buf, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { rtb2.CopyPixels(new PixelRect(0, 0, (int)W, (int)H), handle.AddrOfPinnedObject(), buf.Length, stride); }
        finally { handle.Free(); }

        // Do not name a channel: CopyPixels hands back the backend's own layout, which is BGRA on Windows
        // and Linux but RGBA on macOS — reading the red stripes at a fixed index found 0 rows there while
        // the other 40 tests passed, because they only ever compare channels against each other. The
        // stripes are red: chromatic, one channel far from the other two, unlike the white stripes and the
        // background. The value tracked is the LOW channel (0 in pure red, mid in pink) — averaging raises
        // it, and that is what this test measures.
        var levels = new System.Collections.Generic.List<int>();
        for (int y = 0; y < (int)H; y++)
        {
            int o = y * stride + 200 * 4; // x = 200 is inside the 400px-wide picture
            int hi = Math.Max(buf[o], Math.Max(buf[o + 1], buf[o + 2]));
            int lo = Math.Min(buf[o], Math.Min(buf[o + 1], buf[o + 2]));
            if (buf[o + 3] > 200 && hi > 200 && lo < 250) levels.Add(lo);
        }
        Assert.True(levels.Count >= 40,
            $"found {levels.Count} picture rows — the picture was not drawn where expected. Column x=200:{Dump(buf, stride)}");
        var inner = levels.GetRange(2, levels.Count - 4);
        int min = int.MaxValue, max = int.MinValue;
        foreach (var g in inner) { min = Math.Min(min, g); max = Math.Max(max, g); }
        Assert.True(max - min < 40, $"rows range from {min} to {max}: aliased, not averaged ({string.Join(",", inner.GetRange(0, 16))})");
    }

    // Only read when the scan above finds nothing: a failure should say what WAS drawn in that column,
    // not only that the picture wasn't.
    private static string Dump(byte[] buf, int stride)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = 0; y < (int)H; y += 40)
        {
            int o = y * stride + 200 * 4;
            sb.Append($" y{y}=[{buf[o]},{buf[o + 1]},{buf[o + 2]},{buf[o + 3]}]");
        }
        return sb.ToString();
    }

    [AvaloniaFact]
    public void APictureIsNeverDecodedAboveItsSource()
    {
        var (ed, raw, _) = EditorWithPicture(64, 48, 400, 300);

        Render(ed, ed);

        Assert.Equal(new PixelSize(64, 48), DisplayBitmapSize(ed, raw));
    }

    [AvaloniaFact]
    public void AnInlinePicture_IsDecodedAtItsDrawnSizeToo()
    {
        var inline = new InlineImage { Width = 80, Height = 60 };
        inline.SetImageData(SolidBmp(1600, 1201, 0, 0, 255), "image/bmp");
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "a" });
        p.Inlines.Add(inline);
        doc.Blocks.Add(p);
        var ed = new RichEditor { Document = doc };

        Render(ed, ed);

        var px = DisplayBitmapSize(ed, inline.RawBytes!)!.Value;
        Assert.InRange(px.Width, 80, 101);
        Assert.Null(inline.CachedBitmap);
    }

    // Printing draws at print resolution — a picture printed at screen size would print soft.
    [AvaloniaFact]
    public void PrintingDecodesAtPrintResolution()
    {
        var (ed, raw, _) = EditorWithPicture(3000, 2250, 200, 150);

        using var page = ed.RenderPrintPage(0, dpi: 96);

        var px = DisplayBitmapSize(ed, raw)!.Value;
        Assert.True(px.Width >= 200 * 300 / 96, $"printed from {px.Width}x{px.Height}");
    }

    // The size presets read the source size from the header, without decoding onto the model.
    [AvaloniaFact]
    public void OriginalSize_ComesFromTheHeader_WithoutPinningABitmap()
    {
        var (ed, _, block) = EditorWithPicture(1234, 567, 100, 50);

        typeof(RichEditor).GetMethod("ResetImageSize", NP)!.Invoke(ed, [block]);

        Assert.Equal((1234.0, 567.0), (block.Width, block.Height));
        Assert.Null(block.CachedBitmap);
    }
}
