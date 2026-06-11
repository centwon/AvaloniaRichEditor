using System.Linq;
using System.Threading.Tasks;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Document-level I/O API: HTML/JSON/.ardx load-save (sync + async snapshot variants), Clear,
// plain-text extraction and undo availability. Part of RichEditor (split out of the main file
// for readability).
public partial class RichEditor
{
    /// <summary>Serializes the document to HTML.</summary>
    public string ToHtml() => Document != null ? Formatters.HtmlDocumentFormatter.ToHtml(Document) : "";

    /// <summary>Replaces the document with one parsed from HTML (empty document if null/empty).</summary>
    public void LoadHtml(string? html)
        => LoadDocument(string.IsNullOrEmpty(html) ? new FlowDocument() : Formatters.HtmlDocumentFormatter.ParseHtml(html));

    /// <summary>Serializes the document to the library's JSON format.</summary>
    public string ToJson() => Document != null ? Formatters.DocumentSerializer.Serialize(Document) : "";

    /// <summary>Replaces the document with one loaded from the library's JSON format.</summary>
    public void LoadJson(string json) => LoadDocument(Formatters.DocumentSerializer.Deserialize(json));

    /// <summary>Serializes the document to JSON on a background thread, keeping the UI responsive
    /// for large documents. The document is snapshotted (cloned) on the calling thread first, so
    /// edits made while serialization runs can't tear the output. Call from the UI thread.</summary>
    public Task<string> ToJsonAsync()
    {
        if (Document == null) return Task.FromResult("");
        var snapshot = Document.Clone(); // cheap: image bytes/bitmaps are shared by reference (N6-2)
        return Task.Run(() => Formatters.DocumentSerializer.Serialize(snapshot));
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

    /// <summary>Writes the document to <paramref name="destination"/> as an <c>.ardx</c> package
    /// (ZIP: document.json + raw image entries, no base64 overhead) on a background thread. The
    /// document is snapshotted on the calling thread first, like <see cref="ToJsonAsync"/>.</summary>
    public Task SavePackageAsync(System.IO.Stream destination)
    {
        var snapshot = Document?.Clone() ?? new FlowDocument();
        return Task.Run(() => Formatters.DocumentPackage.Save(snapshot, destination));
    }

    /// <summary>Reads an <c>.ardx</c> package from <paramref name="source"/> on a background thread
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

    /// <summary>The document's text content as plain text (paragraphs/cells separated by newlines).
    /// Used for accessibility and quick text extraction.</summary>
    public string GetPlainText()
    {
        if (Document == null) return "";
        var sb = new System.Text.StringBuilder();
        void AddPara(Paragraph p) { if (sb.Length > 0) sb.Append('\n'); sb.Append(BuildPlain(p)); }
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p) AddPara(p);
            else if (block is TableBlock tb)
                foreach (var (_, _, cell) in tb.LogicalCells()) AddPara(cell);
        }
        return sb.ToString();
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
        if (Document == null || string.IsNullOrEmpty(html)) return;
        var parsed = Formatters.HtmlDocumentFormatter.ParseHtml(html);
        if (parsed.Blocks.Count == 0) return;
        PushUndo();
        InsertParsedDocument(parsed);
        InvalidateVisual();
    }
}
