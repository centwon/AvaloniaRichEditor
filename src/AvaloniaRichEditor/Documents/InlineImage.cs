using Avalonia.Media.Imaging;

namespace AvaloniaRichEditor.Documents;

// An image that flows inline within a paragraph's text (e.g. a small logo/emoji icon).
// It occupies exactly one logical character position in the paragraph (see InlineLen),
// represented in text layouts by the object-replacement character U+FFFC.
public class InlineImage : Inline
{
    private Bitmap? _cachedBitmap;

    /// <summary>Original encoded image bytes (JPEG/PNG/...). When present this is the data source
    /// of truth: serialization stores these bytes verbatim (no re-encoding) and <see cref="Image"/>
    /// is decoded from them lazily on first access.</summary>
    public byte[]? RawBytes { get; private set; }

    /// <summary>MIME type of <see cref="RawBytes"/> (e.g. "image/jpeg").</summary>
    public string? MimeType { get; private set; }

    /// <summary>Decoded bitmap (render cache). Lazily created from <see cref="RawBytes"/> on first
    /// access. Setting a bitmap directly discards the raw bytes — serialization then falls back to
    /// PNG-encoding the bitmap.</summary>
    public Bitmap? Image
    {
        get
        {
            if (_cachedBitmap == null && RawBytes != null)
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(RawBytes);
                    _cachedBitmap = new Bitmap(ms);
                }
                catch { RawBytes = null; MimeType = null; } // undecodable bytes: don't retry every render
            }
            return _cachedBitmap;
        }
        set { _cachedBitmap = value; RawBytes = null; MimeType = null; }
    }

    /// <summary>Sets the image from its original encoded bytes. Pass <paramref name="decoded"/>
    /// when a bitmap is already in hand to seed the render cache and avoid a second decode.</summary>
    public void SetImageData(byte[] bytes, string? mimeType, Bitmap? decoded = null)
    {
        RawBytes = bytes;
        MimeType = mimeType ?? "image/png";
        _cachedBitmap = decoded;
    }

    public double Width { get; set; } = 16;
    public double Height { get; set; } = 16;

    public override TextElement Clone()
    {
        // Shares RawBytes/bitmap references — see ImageBlock.Clone.
        var c = new InlineImage
        {
            Width = this.Width,
            Height = this.Height
        };
        c.RawBytes = RawBytes;
        c.MimeType = MimeType;
        c._cachedBitmap = _cachedBitmap;
        return c;
    }
}
