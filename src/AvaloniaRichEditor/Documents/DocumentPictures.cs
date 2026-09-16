using System.Collections.Generic;

namespace AvaloniaRichEditor.Documents;

/// <summary>The encoded bytes of every picture in a block list — block and inline, inside table cells and inline
/// tables at any depth. The text formatters size their output by it: a picture written as base64 or hex dominates
/// whatever document holds it. (The WinUI peer keeps this on its BlockWalk.)</summary>
internal static class DocumentPictures
{
    internal static IEnumerable<byte[]> Bytes(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ImageBlock { RawBytes: { } ib }:
                    yield return ib;
                    break;
                case Paragraph p:
                    foreach (var inline in p.Inlines)
                        if (inline is InlineImage { RawBytes: { } ii }) yield return ii;
                        else if (inline is InlineTable it)
                            foreach (var row in it.Table.Cells)
                                foreach (var cell in row)
                                    foreach (var b in Bytes(cell.Blocks)) yield return b;
                    break;
                case TableBlock tb:
                    foreach (var row in tb.Cells)
                        foreach (var cell in row)
                            foreach (var b in Bytes(cell.Blocks)) yield return b;
                    break;
            }
        }
    }
}