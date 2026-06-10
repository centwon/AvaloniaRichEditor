using Avalonia.Media.Imaging;

namespace AvaloniaRichEditor.Documents;

/// <summary>A block-level image (its own line/paragraph), as opposed to a small in-line
/// <see cref="InlineImage"/>. Used for larger pictures; supports resize.</summary>
public class ImageBlock : Block
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

    /// <summary>Display width in device-independent pixels. <see cref="double.NaN"/> = natural size.</summary>
    public double Width { get; set; } = double.NaN;
    /// <summary>Display height in device-independent pixels. <see cref="double.NaN"/> = natural size.</summary>
    public double Height { get; set; } = double.NaN;

    /// <inheritdoc/>
    public override TextElement Clone()
    {
        // RawBytes and the decoded bitmap are immutable as used here, so clones (undo snapshots)
        // share the references — no extra memory per checkpoint.
        var c = new ImageBlock
        {
            Width = this.Width,
            Height = this.Height,
            Indent = this.Indent
        };
        c.RawBytes = RawBytes;
        c.MimeType = MimeType;
        c._cachedBitmap = _cachedBitmap;
        return c;
    }
}
