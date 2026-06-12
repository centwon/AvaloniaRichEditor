using System;
using System.Collections.Generic;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Controls;

// P-milestone Phase 1: pagination core. Computes where pages begin in the editor's continuous
// layout space. Pages never cut through an indivisible atom: one text line of a paragraph
// (paragraphs split at line boundaries via the cached TextLayout's line metrics), or a whole
// image/table/divider block. The walk mirrors MeasureContentHeight's advancement exactly (same
// widths, same per-block heights) so downstream consumers (page-view gap injection, page render
// for print/PDF) slice the very same geometry the editor renders — keeping core invariant 1
// (the single TextLayout is the source of truth) intact.
public partial class RichEditor
{
    // A4 at 96 DPI.
    internal const double A4PageWidth = 794;
    internal const double A4PageHeight = 1123;

    // Document-space y positions where each page's content starts; [0] is always 0. An atom taller
    // than pageContentHeight (huge image/table) gets a page of its own and overflows it (v1 contract:
    // no intra-row table splits, no image scaling). The image branch must never touch img.Image —
    // the getter lazily decodes RawBytes (N6-2) and pagination has to stay decode-free.
    internal List<double> ComputePageBreaks(double contentWidth, double pageContentHeight)
    {
        var breaks = new List<double> { 0 };
        if (Document == null || pageContentHeight <= 0) return breaks;

        double y = 0;         // continuous doc-space offset (mirrors MeasureContentHeight)
        double pageStart = 0; // doc-space y where the current page began
        const double eps = 0.01; // float noise must not split an exactly-full page

        void PlaceAtom(double height)
        {
            if (y + height > pageStart + pageContentHeight + eps && y > pageStart)
            {
                breaks.Add(y);
                pageStart = y;
            }
            y += height;
        }

        foreach (var block in Document.Blocks)
        {
            y += block.MarginTop;
            if (block is TableBlock tb)
            {
                PlaceAtom(LayoutTable(tb, 10 + tb.Indent, y).TotalHeight);
                y += tb.MarginBottom;
            }
            else if (block is ImageBlock img)
            {
                PlaceAtom(img.Height > 0 ? img.Height : 200);
                y += img.MarginBottom;
            }
            else if (block is DividerBlock dv)
            {
                PlaceAtom(DividerHeight);
                y += dv.MarginBottom;
            }
            else if (block is Paragraph p)
            {
                if (BuildPlain(p) == "")
                {
                    PlaceAtom(!double.IsNaN(p.LineHeight) ? p.LineHeight : 20);
                }
                else
                {
                    var layout = BuildTextLayout(p, Math.Max(10, contentWidth - 20 - ParaLeft(p) - p.MarginRight));
                    double paraTop = y;
                    foreach (var line in layout.TextLines) PlaceAtom(line.Height);
                    y = paraTop + layout.Height; // reconcile line-sum drift with the measure walk
                }
                y += p.MarginBottom;
            }
        }
        return breaks;
    }
}
