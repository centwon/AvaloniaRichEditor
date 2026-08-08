using System;
using System.Linq;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;

namespace AvaloniaRichEditor.Demo;

/// <summary>`--sample-check`: builds <see cref="SampleDocument"/> and reports what came out.
/// A compile only proves the code is well typed; this proves the document actually constructs, that the
/// procedurally drawn pictures really encoded to PNG, that every feature the tour claims is present, and
/// that all four formatters can write it. Cheap enough to run whenever the sample changes.</summary>
internal static class SampleDocumentCheck
{
    public static void Run()
    {
        var doc = SampleDocument.Build();

        int paragraphs = Walk(doc.Blocks).OfType<Paragraph>().Count();
        int tables = Walk(doc.Blocks).OfType<TableBlock>().Count();
        var inlines = Walk(doc.Blocks).OfType<Paragraph>().SelectMany(p => p.Inlines).ToList();
        var pictures = Walk(doc.Blocks).OfType<ImageBlock>().ToList();
        var icons = inlines.OfType<InlineImage>().ToList();

        Console.WriteLine($"blocks={doc.Blocks.Count} paragraphs={paragraphs} tables={tables}");
        Console.WriteLine($"page={doc.PageSetup?.PageSize} header='{doc.PageSetup?.Header}' " +
                          $"footer='{doc.PageSetup?.Footer}' numbers={doc.PageSetup?.ShowPageNumbers}");

        // The pictures are drawn at runtime, so "did it encode" is a real question, not a formality.
        foreach (var p in pictures)
            Console.WriteLine($"picture {p.Width}x{p.Height} bytes={p.RawBytes?.Length ?? 0} png={IsPng(p.RawBytes)}");
        foreach (var i in icons)
            Console.WriteLine($"icon    {i.Width}x{i.Height} bytes={i.RawBytes?.Length ?? 0} png={IsPng(i.RawBytes)}");

        Check("headings", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.HeadingLevel > 0));
        Check("bullet list", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.ListType == ListKind.Bullet));
        Check("ordered list", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.ListType == ListKind.Ordered));
        Check("nested list level", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.ListLevel >= 2));
        Check("alignment", Walk(doc.Blocks).OfType<Paragraph>().Select(p => p.TextAlignment).Distinct().Count() >= 4);
        Check("quote", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.IsQuote));
        Check("indent", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.Indent > 0));
        Check("paragraph background", Walk(doc.Blocks).OfType<Paragraph>().Any(p => p.Background != null));
        Check("divider", doc.Blocks.OfType<DividerBlock>().Any());
        Check("hyperlink", inlines.OfType<Run>().Any(r => !string.IsNullOrEmpty(r.NavigateUri)));
        Check("highlight", inlines.OfType<Run>().Any(r => r.Background != null));
        Check("font family", inlines.OfType<Run>().Any(r => !string.IsNullOrEmpty(r.FontFamily)));
        Check("block picture", pictures.Count >= 1);
        Check("inline icon", icons.Count >= 1);
        Check("inline table", inlines.OfType<InlineTable>().Any());
        Check("merged cell", Walk(doc.Blocks).OfType<TableBlock>()
                                 .Any(t => t.LogicalCells().Any(c => t.SpanOf(c.r, c.c) != (1, 1))));
        Check("cell background", Walk(doc.Blocks).OfType<TableBlock>()
                                     .Any(t => t.Cells.SelectMany(r => r).Any(c => c.Background != null)));
        Check("multi-block cell", Walk(doc.Blocks).OfType<TableBlock>()
                                      .Any(t => t.Cells.SelectMany(r => r).Any(c => c.Blocks.Count > 1)));
        Check("nested table", Walk(doc.Blocks).OfType<TableBlock>()
                                  .Any(t => t.Cells.SelectMany(r => r).Any(c => c.Blocks.OfType<TableBlock>().Any())));
        Check("CJK text", inlines.OfType<Run>().Any(r => r.Text?.Any(ch => ch >= 0xAC00 && ch <= 0xD7A3) == true));

        // Every export path, on the document a user will actually have open.
        Console.WriteLine($"html={HtmlDocumentFormatter.ToHtml(doc).Length}B " +
                          $"rtf={RtfDocumentFormatter.Write(doc).Length}B " +
                          $"json={DocumentSerializer.Serialize(doc).Length}B");
    }

    // Paragraphs and tables nest, so a flat walk over Blocks would miss everything inside a cell.
    private static System.Collections.Generic.IEnumerable<Block> Walk(
        System.Collections.Generic.IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
        {
            yield return b;
            if (b is TableBlock t)
                foreach (var cell in t.Cells.SelectMany(r => r))
                    foreach (var inner in Walk(cell.Blocks))
                        yield return inner;
            if (b is Paragraph p)
                foreach (var it in p.Inlines.OfType<InlineTable>())
                    foreach (var inner in Walk(new Block[] { it.Table }))
                        yield return inner;
        }
    }

    private static bool IsPng(byte[]? bytes) =>
        bytes is { Length: > 8 } && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"  {(ok ? "ok  " : "MISS")} {what}");
        if (!ok) Environment.ExitCode = 1;
    }
}
