using System.Linq;
using System.Text;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Tests;

internal static class TestHelpers
{
    // Concatenated text of all Runs in a paragraph (ignores inline images).
    public static string Text(this Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var inl in p.Inlines)
            if (inl is Run r && r.Text != null) sb.Append(r.Text);
        return sb.ToString();
    }

    public static Paragraph Para(params Run[] runs)
    {
        var p = new Paragraph();
        foreach (var r in runs) p.Inlines.Add(r);
        return p;
    }

    // Mixed-inline overload so tests can interleave InlineImage with Runs. Calls whose args are all
    // Runs still bind to the Run[] overload above (more specific), so existing call sites are unaffected.
    public static Paragraph Para(params Inline[] inlines)
    {
        var p = new Paragraph();
        foreach (var i in inlines) p.Inlines.Add(i);
        return p;
    }

    // An inline image occupies one logical character (no bitmap needed to exercise the offset model).
    public static InlineImage Img() => new InlineImage();

    // Build a FlowDocument from blocks, wiring Parent pointers the way RichEditor does so that
    // TextRange/TextPointer can walk up to the document for multi-paragraph operations.
    public static FlowDocument Doc(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks)
        {
            b.Parent = doc;
            if (b is Paragraph p)
                foreach (var inl in p.Inlines) inl.Parent = p;
            doc.Blocks.Add(b);
        }
        return doc;
    }
}
