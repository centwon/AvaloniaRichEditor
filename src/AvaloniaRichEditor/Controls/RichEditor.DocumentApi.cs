using System.Linq;
using System.Threading.Tasks;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Document-level I/O API: HTML/JSON/.flow load-save (sync + async snapshot variants), Clear,
// plain-text extraction and undo availability. Part of RichEditor (split out of the main file
// for readability).
public partial class RichEditor
{
    /// <summary>Serializes the document to HTML.</summary>
    public string ToHtml() => Document != null ? Formatters.HtmlDocumentFormatter.ToHtml(Document) : "";

    /// <summary>Replaces the document with one parsed from HTML (empty document if null/empty).</summary>
    public void LoadHtml(string? html)
        => LoadDocument(string.IsNullOrEmpty(html) ? new FlowDocument() : Formatters.HtmlDocumentFormatter.ParseHtml(html, AllowLocalFileImages));

    /// <summary>Replaces the document with one parsed from HTML, downloading remote (<c>http</c>)
    /// images off the UI thread first so a slow network can't freeze the UI. Await from the UI thread.</summary>
    public async Task LoadHtmlAsync(string? html)
        => LoadDocument(string.IsNullOrEmpty(html)
            ? new FlowDocument()
            : await Formatters.HtmlDocumentFormatter.ParseHtmlAsync(html, AllowLocalFileImages));

    /// <summary>Serializes the document to RTF (Rich Text Format) — readable by Word, WordPad, LibreOffice,
    /// and HWP. Covers paragraphs, runs (bold/italic/underline/strike, size, colour, font), alignment,
    /// headings, lists (as markers), tables, and embedded PNG/JPEG images.</summary>
    public string ToRtf() => Document != null ? Formatters.RtfDocumentFormatter.Write(Document) : "";

    /// <summary>Replaces the document with one parsed from RTF (empty document if null/empty or not RTF).</summary>
    public void LoadRtf(string? rtf)
        => LoadDocument(string.IsNullOrEmpty(rtf) || !Formatters.RtfDocumentFormatter.LooksLikeRtf(rtf)
            ? new FlowDocument()
            : Formatters.RtfDocumentFormatter.Parse(rtf));

    /// <summary>Serializes the document to the library's JSON format.</summary>
    public string ToJson() => Document != null ? Formatters.DocumentSerializer.Serialize(Document) : "";

    /// <summary>Replaces the document with one loaded from the library's JSON format.</summary>
    public void LoadJson(string json) => LoadDocument(Formatters.DocumentSerializer.Deserialize(json));

    /// <summary>Serializes the document to JSON on a background thread, keeping the UI responsive
    /// for large documents. The DTO (which reads thread-affine brush colors) is built on the calling
    /// thread; only JSON encoding runs in the background. The built DTO is a value snapshot, so edits
    /// made while encoding runs can't tear the output. Call from the UI thread.</summary>
    public Task<string> ToJsonAsync()
    {
        if (Document == null) return Task.FromResult("");
        var (dto, images) = Formatters.DocumentSerializer.BuildDto(Document);
        return Task.Run(() => Formatters.DocumentSerializer.SerializeDto(dto, images));
    }

    /// <summary>Parses JSON into a document on a background thread (image decoding is already
    /// deferred to first render), then swaps it in. Call (and await) from the UI thread.</summary>
    public async Task LoadJsonAsync(string json)
    {
        // Background: parsing + base64 only. Model objects (brushes, decorations) are Avalonia
        // thread-affine and must be created here on the UI thread, or the compositor crashes on
        // first render ("the calling thread cannot access this object").
        var (dto, pool) = await Task.Run(() => Formatters.DocumentSerializer.ParseJson(json));
        LoadDocument(Formatters.DocumentSerializer.FromDto(dto, pool));
    }

    /// <summary>Writes the document to <paramref name="destination"/> as a <c>.flow</c> package
    /// (ZIP: document.json + raw image entries, no base64 overhead) on a background thread. The DTO
    /// is built on the calling thread (snapshot + thread-affine brush reads), like <see cref="ToJsonAsync"/>;
    /// only the zip writing runs in the background.</summary>
    public Task SavePackageAsync(System.IO.Stream destination)
    {
        var (dto, images) = Formatters.DocumentSerializer.BuildDto(Document ?? new FlowDocument());
        return Task.Run(() => Formatters.DocumentPackage.WriteDto(dto, images, destination));
    }

    /// <summary>Reads a <c>.flow</c> package from <paramref name="source"/> on a background thread
    /// (image decoding is already deferred to first render), then swaps the document in. Call (and
    /// await) from the UI thread.</summary>
    public async Task LoadPackageAsync(System.IO.Stream source)
    {
        // Background: zip/JSON parsing + byte extraction only; model built on the UI thread
        // (model brushes/decorations are thread-affine — see LoadJsonAsync).
        var (dto, pool) = await Task.Run(() => Formatters.DocumentPackage.ReadPackage(source));
        LoadDocument(Formatters.DocumentSerializer.FromDto(dto, pool));
    }

    /// <summary>Clears the document to a single empty paragraph.</summary>
    public void Clear() => LoadDocument(new FlowDocument());

    /// <summary>The document's text content as plain text (paragraphs/cells separated by the platform
    /// newline — CRLF on Windows — so the result shows real line breaks when written to a file or pasted
    /// into a native text control; soft '\n' breaks inside a paragraph are normalized too).
    /// Used for accessibility and quick text extraction.</summary>
    public string GetPlainText()
    {
        if (Document == null) return "";
        var sb = new System.Text.StringBuilder();
        // `first` (not sb.Length): a leading empty paragraph must still place a separator before the
        // next one — checking the buffer length conflated "first paragraph" with "buffer empty" and
        // dropped leading blank lines.
        bool first = true;
        void AddPara(Paragraph p) { if (!first) sb.Append('\n'); first = false; sb.Append(BuildPlain(p)); }
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p) AddPara(p);
            else if (block is TableBlock tb)
                foreach (var (_, _, cell) in tb.LogicalCells()) AddPara(cell.Para);
        }
        // LF-only renders as a single line in many Windows consumers; normalize every break (paragraph
        // separators above + soft '\n' from BuildPlain) to the platform newline.
        return sb.ToString().ReplaceLineEndings();
    }

    /// <summary>True if there is an edit to undo.</summary>
    public bool CanUndo => _undoManager.CanUndo;

    /// <summary>True if there is an undone edit to redo.</summary>
    public bool CanRedo => _undoManager.CanRedo;

    // Swaps in a new document and resets caret/selection/undo to a clean state. Shared by Load*/Clear.
    private void LoadDocument(FlowDocument doc)
    {
        Document = doc; // OnPropertyChanged runs UpdateParents/NormalizeBlocks + raises DocumentChanged
        var first = GetAllParagraphsInOrder().FirstOrDefault();
        _caretPosition = new TextPointer(first, 0);
        CollapseSelectionToCaret();
        _undoManager = new UndoManager();
        InvalidateVisual();
    }

    /// <summary>Parses an HTML fragment and inserts the resulting blocks at the caret position.</summary>
    public void InsertHtml(string html)
    {
        if (Document == null || IsReadOnly || string.IsNullOrEmpty(html)) return;
        var parsed = Formatters.HtmlDocumentFormatter.ParseHtml(html, AllowLocalFileImages);
        if (parsed.Blocks.Count == 0) return;
        PushUndo();
        InsertParsedDocument(parsed);
        InvalidateVisual();
    }
}
