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
}
