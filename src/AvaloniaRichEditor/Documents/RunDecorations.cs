using Avalonia.Media;

namespace AvaloniaRichEditor.Documents;

// A run's underline / strikethrough, read and built in one place. The readers and writers (JSON, HTML, RTF) and
// TextRange each carried their own copy of both (dedup, 2026-11-02). Model objects: Make runs on the UI thread,
// where its callers already are (CLAUDE.md rule 8).
internal static class RunDecorations
{
    internal static bool Has(TextDecorationCollection? decos, TextDecorationLocation loc)
    {
        if (decos == null) return false;
        foreach (var d in decos) if (d.Location == loc) return true;
        return false;
    }

    // Null when neither: a run with no decoration carries no collection.
    internal static TextDecorationCollection? Make(bool underline, bool strikethrough)
    {
        if (!underline && !strikethrough) return null;
        var c = new TextDecorationCollection();
        if (underline) c.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        if (strikethrough) c.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        return c;
    }
}
