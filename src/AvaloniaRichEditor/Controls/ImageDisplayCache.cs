using System;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

/// <summary>The bitmaps pictures are DRAWN with: decoded at the device-pixel size they are drawn at (with
/// headroom), never above the source, and decoded again larger when a draw asks for more (zoom in, a resize).
/// <para>From the WinUI port, which measured the cost of the alternative (2026-09-16): six 4000x3000 photos
/// shown 240 px wide held ~280 MB of decoded pixels; decoded to the display size, the cost followed the display
/// and not the source. Here the renderer called the model's <c>Image</c> getter, which decodes at source size
/// and keeps the bitmap on the element — and on every undo snapshot, since Clone shares it.</para>
/// <para>Entries hang off the encoded byte array weakly, so a picture's display bitmap goes when its bytes do
/// (deleted, and out of the undo history). Replaced bitmaps are left to the GC rather than disposed: a scene
/// already handed to the compositor may still draw the old one.</para></summary>
internal sealed class ImageDisplayCache
{
    private sealed class Entry
    {
        public Bitmap? Bitmap;   // null = undecodable, remembered so the render path doesn't retry every frame
        public bool AtSourceSize; // no request can get more detail from these bytes
        // The size the decode was ASKED for. A later request is compared with this, not with what came back:
        // a decoder may round below the target, and comparing against the result re-decoded every frame.
        public int TargetWidth, TargetHeight;
    }

    private readonly ConditionalWeakTable<byte[], Entry> _entries = new();

    // Decodes aim this much above the requested size, so a slow zoom doesn't re-decode on every notch.
    internal const double Headroom = 1.25;

    /// <summary>The bitmap to draw <paramref name="rawBytes"/> with at <paramref name="pixelWidth"/> ×
    /// <paramref name="pixelHeight"/> device pixels. <paramref name="modelBitmap"/> is the element's own decoded
    /// bitmap if it already has one (read without decoding); it is used when it is sharp enough, since it costs
    /// nothing more.</summary>
    public Bitmap? Get(byte[] rawBytes, Bitmap? modelBitmap, int pixelWidth, int pixelHeight)
    {
        if (modelBitmap != null && !Smaller(modelBitmap, pixelWidth, pixelHeight)) return modelBitmap;

        if (_entries.TryGetValue(rawBytes, out var entry)
            && (entry.Bitmap == null || entry.AtSourceSize
                || (pixelWidth <= entry.TargetWidth && pixelHeight <= entry.TargetHeight)))
            return entry.Bitmap ?? modelBitmap;

        int tw = (int)Math.Ceiling(Math.Max(1, pixelWidth) * Headroom), th = (int)Math.Ceiling(Math.Max(1, pixelHeight) * Headroom);
        var decoded = Decode(rawBytes, tw, th);
        if (decoded.bitmap == null && entry?.Bitmap != null) return entry.Bitmap; // a failed upgrade keeps what it has
        _entries.AddOrUpdate(rawBytes, new Entry
        {
            Bitmap = decoded.bitmap, AtSourceSize = decoded.atSourceSize, TargetWidth = tw, TargetHeight = th,
        });
        return decoded.bitmap ?? modelBitmap;
    }

    private static bool Smaller(Bitmap bmp, int w, int h)
        => w > bmp.PixelSize.Width + 1 || h > bmp.PixelSize.Height + 1; // +1: request vs aspect-fitted rounding

    // The smallest size with the source's aspect that COVERS boxW × boxH, never enlarging. A picture is drawn
    // stretched into its rect, which need not have the source's proportions (an <img width height>, the edge
    // handles); fitting INSIDE the box left the long axis short and the picture soft along it (from the WinUI
    // port, 2026-09-19). One uniform scale also keeps requests totally ordered, so pictures sharing bytes at
    // different proportions converge on one decode. The source size comes from the header; when the header
    // isn't one ImageInfo reads, the picture is decoded whole, as it always was.
    private static (Bitmap? bitmap, bool atSourceSize) Decode(byte[] rawBytes, int boxW, int boxH)
    {
        try
        {
            var (natW, natH) = ImageInfo.GetPixelSize(rawBytes);
            using var ms = new System.IO.MemoryStream(rawBytes);
            double s = natW > 0 && natH > 0 ? Math.Max(boxW / natW, boxH / natH) : 1;
            if (s >= 1) return (new Bitmap(ms), true);
            // Ceiling: rounding down would land a pixel short of the box. The epsilon keeps an exact fit from
            // rounding up past it on floating-point noise.
            int width = Math.Min((int)natW, Math.Max(1, (int)Math.Ceiling(natW * s - 1e-9)));
            return (Bitmap.DecodeToWidth(ms, width, BitmapInterpolationMode.HighQuality), false);
        }
        catch (Exception ex)
        {
            RichEditorDiagnostics.Report(ex);
            return (null, true);
        }
    }

    // ---- test seam ----
    internal Bitmap? CachedFor(byte[] rawBytes) => _entries.TryGetValue(rawBytes, out var e) ? e.Bitmap : null;
}
