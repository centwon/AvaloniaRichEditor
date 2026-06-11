using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Formatters;

/// <summary>Reads and writes the <c>.ardx</c> package format (roadmap N6-7): a ZIP container with
/// <c>document.json</c> (the library's JSON schema, its image pool referencing zip entries instead
/// of embedding base64) and <c>images/&lt;sha256&gt;</c> entries holding the original encoded bytes
/// stored uncompressed — they are already JPEG/PNG-compressed, so the win over plain JSON is
/// dropping the ~33% base64 overhead, not deflate. The JSON string contract
/// (<see cref="DocumentSerializer"/>) is unchanged; this is an additional layer for file-based
/// interchange.</summary>
public static class DocumentPackage
{
    /// <summary>Writes <paramref name="document"/> to <paramref name="destination"/> as an .ardx
    /// package. The stream is left open.</summary>
    public static void Save(FlowDocument document, Stream destination)
    {
        var images = new Dictionary<string, (byte[] Bytes, string Mime)>();
        var dto = DocumentSerializer.ToDto(document, images);
        if (images.Count > 0)
        {
            // Pool entries carry only the mime type; the bytes live in zip entries with the same key.
            dto.Images = new Dictionary<string, ImagePoolDto>();
            foreach (var (key, img) in images)
                dto.Images[key] = new ImagePoolDto { MimeType = img.Mime };
        }

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var docEntry = zip.CreateEntry("document.json", CompressionLevel.Optimal); // text deflates well
        using (var s = docEntry.Open())
            JsonSerializer.Serialize(s, dto, DocumentJsonContext.Default.FlowDocumentDto);
        foreach (var (key, img) in images)
        {
            // Already-compressed image bytes: store as-is (deflate would cost CPU for ~0% gain).
            var entry = zip.CreateEntry("images/" + key, CompressionLevel.NoCompression);
            using var s = entry.Open();
            s.Write(img.Bytes, 0, img.Bytes.Length);
        }
    }

    /// <summary>Reads an .ardx package from <paramref name="source"/>. Returns an empty document on
    /// malformed input, mirroring <see cref="DocumentSerializer.Deserialize"/>. Image decoding is
    /// deferred to first render. The stream is left open.</summary>
    public static FlowDocument Load(Stream source)
    {
        try
        {
            using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var docEntry = zip.GetEntry("document.json");
            if (docEntry == null) return new FlowDocument();
            FlowDocumentDto? dto;
            using (var s = docEntry.Open())
                dto = JsonSerializer.Deserialize(s, DocumentJsonContext.Default.FlowDocumentDto);

            // Blocks referencing the same hash share one byte[] instance, like the JSON pool path.
            var pool = new Dictionary<string, (byte[] Bytes, string Mime)>();
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("images/", StringComparison.Ordinal)) continue;
                string key = entry.FullName.Substring("images/".Length);
                if (key.Length == 0) continue;
                using var es = entry.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                var bytes = ms.ToArray();
                string mime = dto?.Images != null && dto.Images.TryGetValue(key, out var meta) && meta.MimeType != null
                    ? meta.MimeType
                    : ImageMime.Detect(bytes);
                pool[key] = (bytes, mime);
            }
            return DocumentSerializer.FromDto(dto, pool);
        }
        catch (InvalidDataException) { return new FlowDocument(); }
        catch (JsonException) { return new FlowDocument(); }
    }
}
