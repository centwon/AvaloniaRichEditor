using System.Collections.Generic;
using System.Linq;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// Capability flags on INCOMING content. Foreign content — every rich paste flavour (in-app, RTF, HTML) and
// InsertHtml — is adapted to what this editor allows before it is spliced in: with AllowImages off its
// pictures are dropped, with AllowTables off its tables are unwrapped into their cells' content (the text
// survives, only the grid goes). Both flags promise that ("image insertion and image paste", "table
// insertion"), yet the rich paste paths checked only AllowRichPaste — the in-app inline path alone dropped
// inline images — so a host that turned images or tables off still received them from any web page or
// Word document pasted in. (Backported 2026-09-12 from the WinUI port, where it was a user decision.)
// Opening a document (Load*) is not an insert and is not adapted.
public partial class RichEditor
{
    // `emptied`: the flags removed everything the content had — the caller then inserts nothing, so the
    // paste leaves no empty undo step behind.
    private FlowDocument AdaptToCapabilities(FlowDocument pd, out bool emptied)
    {
        emptied = false;
        if (AllowImages && AllowTables) return pd;
        bool had = !IsEmptyContent(pd);
        var blocks = AdaptBlocks(pd.Blocks);
        pd.Blocks.Clear();
        foreach (var b in blocks) { b.Parent = pd; pd.Blocks.Add(b); }
        emptied = had && IsEmptyContent(pd);
        return pd;
    }

    private List<Block> AdaptBlocks(IEnumerable<Block> source)
    {
        var result = new List<Block>();
        foreach (var b in source)
        {
            switch (b)
            {
                case ImageBlock when !AllowImages:
                    break;
                case TableBlock tb when !AllowTables:
                    // Unwrapped in reading order, one cell after another; an empty cell adds no blank line.
                    foreach (var (_, _, cell) in tb.LogicalCells())
                        foreach (var inner in AdaptBlocks(cell.Blocks))
                            if (!(inner is Paragraph ip && IsBlank(ip))) result.Add(inner);
                    break;
                case TableBlock tb:
                    AdaptCells(tb);
                    result.Add(tb);
                    break;
                case Paragraph p:
                    AdaptInlines(p);
                    result.Add(p);
                    break;
                default:
                    result.Add(b);
                    break;
            }
        }
        return result;
    }

    // A table that stays keeps its grid; what its cells hold is adapted, and each cell keeps its
    // never-paragraph-less invariant.
    private void AdaptCells(TableBlock tb)
    {
        foreach (var (_, _, cell) in tb.LogicalCells())
        {
            var blocks = AdaptBlocks(cell.Blocks);
            if (!blocks.Any(x => x is Paragraph)) blocks.Add(new Paragraph { Inlines = { new Run { Text = "" } } });
            cell.Blocks = blocks;
        }
    }

    private void AdaptInlines(Paragraph p)
    {
        for (int i = p.Inlines.Count - 1; i >= 0; i--)
        {
            switch (p.Inlines[i])
            {
                case InlineImage when !AllowImages:
                    p.Inlines.RemoveAt(i);
                    break;
                case InlineTable it when !AllowTables:
                    // An inline table becomes its cells' text, in place, a space between cells.
                    p.Inlines.RemoveAt(i);
                    int at = i;
                    bool first = true;
                    foreach (var (_, _, cell) in it.Table.LogicalCells())
                        foreach (var inner in AdaptBlocks(cell.Blocks))
                        {
                            if (inner is not Paragraph ip || IsBlank(ip)) continue;
                            if (!first) p.Inlines.Insert(at++, new Run { Text = " " });
                            first = false;
                            foreach (var inl in ip.Inlines.ToList()) p.Inlines.Insert(at++, inl);
                        }
                    break;
                case InlineTable it:
                    AdaptCells(it.Table);
                    break;
            }
        }
        if (p.Inlines.Count == 0) p.Inlines.Add(new Run { Text = "" });
    }

    private static bool IsBlank(Paragraph p) => p.Inlines.All(i => i is Run r && string.IsNullOrEmpty(r.Text));
    private static bool IsEmptyContent(FlowDocument pd) => pd.Blocks.All(b => b is Paragraph p && IsBlank(p));
}
