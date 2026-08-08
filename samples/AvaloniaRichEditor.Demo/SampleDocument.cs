using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Demo;

/// <summary>The document the demo opens with. It is deliberately a tour rather than a "hello world":
/// every feature the README claims is on screen, in the order someone evaluating the control would look
/// for it, and each section says what it is demonstrating. That makes it the thing to screenshot and the
/// first thing to try editing.
/// <para>Everything here is built on the UI thread. Model objects hold Avalonia thread-affine values
/// (brushes, decorations), so building a document on a background thread crashes the first render.</para>
/// </summary>
internal static class SampleDocument
{
    // Palette. Kept here so the document reads as one design rather than a spray of named colours.
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x0B, 0x62, 0xC4));
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromRgb(0x60, 0x6A, 0x76));
    private static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x35));
    private static readonly IBrush Highlight = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xC4));
    private static readonly IBrush HeadCell = new SolidColorBrush(Color.FromRgb(0xEC, 0xF2, 0xFA));
    private static readonly IBrush NoteBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));

    public static FlowDocument Build()
    {
        var doc = new FlowDocument
        {
            // A page view with real chrome: the header/footer and page numbers are document state, so
            // they survive save/load and show up in print and PDF.
            PageSetup = new PageSetup
            {
                PageSize = RichEditorPageSize.A4,
                Orientation = RichEditorPageOrientation.Portrait,
                ShowPageBoundaries = true,
                Header = "AvaloniaRichEditor — feature tour",
                Footer = "samples/AvaloniaRichEditor.Demo",
                ShowPageNumbers = true,
            },
        };
        var b = doc.Blocks;

        // ---- title -------------------------------------------------------------------------------
        b.Add(Head("AvaloniaRichEditor", 1, 26));
        var lede = new Paragraph { MarginBottom = 4 };
        lede.Inlines.Add(new Run { Text = "A from-scratch rich text editor for Avalonia — ", FontSize = 12, Foreground = Muted });
        lede.Inlines.Add(new Run { Text = "everything below is live. Click in and edit it.", FontSize = 12, Foreground = Muted, FontStyle = FontStyle.Italic });
        b.Add(lede);
        b.Add(new DividerBlock { MarginBottom = 14 });

        // ---- 1. inline formatting ----------------------------------------------------------------
        b.Add(Head("1. Inline formatting", 2, 16));
        var fmt = new Paragraph();
        fmt.Inlines.Add(Text("Bold", FontWeight.Bold));
        fmt.Inlines.Add(Text(", "));
        fmt.Inlines.Add(new Run { Text = "italic", FontSize = 11, Foreground = Ink, FontStyle = FontStyle.Italic });
        fmt.Inlines.Add(Text(", "));
        fmt.Inlines.Add(new Run { Text = "underline", FontSize = 11, Foreground = Ink, TextDecorations = TextDecorations.Underline });
        fmt.Inlines.Add(Text(", "));
        fmt.Inlines.Add(new Run { Text = "strikethrough", FontSize = 11, Foreground = Ink, TextDecorations = TextDecorations.Strikethrough });
        fmt.Inlines.Add(Text(", "));
        fmt.Inlines.Add(new Run { Text = "colour", FontSize = 11, Foreground = Good });
        fmt.Inlines.Add(Text(", "));
        fmt.Inlines.Add(new Run { Text = " highlight ", FontSize = 11, Foreground = Ink, Background = Highlight });
        fmt.Inlines.Add(Text(", "));
        fmt.Inlines.Add(new Run { Text = "another font", FontSize = 11, Foreground = Ink, FontFamily = "Consolas" });
        fmt.Inlines.Add(Text(", bigger ", FontWeight.Normal, 16));
        fmt.Inlines.Add(Text("and smaller", FontWeight.Normal, 8));
        fmt.Inlines.Add(Text(", and a "));
        fmt.Inlines.Add(new Run
        {
            Text = "hyperlink",
            FontSize = 11,
            Foreground = Accent,
            TextDecorations = TextDecorations.Underline,
            NavigateUri = "https://github.com/centwon/AvaloniaRichEditor",
        });
        fmt.Inlines.Add(Text(" — all in one paragraph."));
        b.Add(fmt);

        // Korean, so CJK shaping and the IME story are visible at a glance.
        b.Add(Body("한글도 같은 문단 안에서 자유롭게 섞입니다. 조합 중인 글자는 인라인 preedit으로 그려집니다."));

        // ---- 2. paragraphs -----------------------------------------------------------------------
        b.Add(Head("2. Paragraphs", 2, 16));
        b.Add(Body("Left aligned — the default.", p => p.TextAlignment = TextAlignment.Left));
        b.Add(Body("Centred.", p => p.TextAlignment = TextAlignment.Center));
        b.Add(Body("Right aligned.", p => p.TextAlignment = TextAlignment.Right));
        b.Add(Body("Justified text spreads to both margins, which is easiest to see over a couple of lines "
                 + "of running text like this one — the layout engine measures every line itself.",
                   p => p.TextAlignment = TextAlignment.Justify));
        b.Add(Body("Indented, with a wider line spacing.", p => { p.Indent = 36; p.LineSpacing = 1.6; }));
        b.Add(Body("A quote block for pulled-out text.", p => { p.IsQuote = true; p.Foreground(Muted); }));
        b.Add(Body("A paragraph with its own background.", p => p.Background = NoteBg));

        // ---- 3. lists ----------------------------------------------------------------------------
        b.Add(Head("3. Lists", 2, 16));
        b.Add(Item("Bullets nest to any depth", ListKind.Bullet, 0));
        b.Add(Item("Second level", ListKind.Bullet, 1));
        b.Add(Item("Third level", ListKind.Bullet, 2));
        b.Add(Item("Back to the first", ListKind.Bullet, 0));
        b.Add(Item("Numbered lists renumber as you edit", ListKind.Ordered, 0, ListMarkerStyle.Decimal));
        b.Add(Item("…so inserting here shifts everything below", ListKind.Ordered, 0, ListMarkerStyle.Decimal));
        b.Add(Item("Letters and roman numerals too", ListKind.Ordered, 1, ListMarkerStyle.LowerAlpha));

        // ---- 4. images ---------------------------------------------------------------------------
        b.Add(Head("4. Images", 2, 16));
        var withIcon = new Paragraph();
        withIcon.Inlines.Add(Text("A small image sits inline, like a character "));
        withIcon.Inlines.Add(Icon());
        withIcon.Inlines.Add(Text(" — the caret steps over it as one. A larger one becomes its own block:"));
        b.Add(withIcon);
        // The caption goes ABOVE the picture. Below it, a page break can land between the two and strand
        // the caption at the top of the next page, away from what it describes.
        b.Add(Caption("Drag the corner handles to resize; right-click to replace or save it."));
        b.Add(Picture());

        // ---- 5. tables ---------------------------------------------------------------------------
        b.Add(Head("5. Tables", 2, 16));
        b.Add(Body("Cells are full block containers, so a cell can hold several paragraphs, a list, a "
                 + "picture, a divider — or another table."));
        b.Add(FeatureTable());
        b.Add(Caption("Row 1 is a merged, shaded header. The last cell holds a nested table."));

        // ---- 6. inline table ---------------------------------------------------------------------
        b.Add(Head("6. Inline tables", 2, 16));
        var inlineHost = new Paragraph();
        inlineHost.Inlines.Add(Text("A table can also flow inside a line of text "));
        inlineHost.Inlines.Add(new InlineTable { Table = MiniTable() });
        inlineHost.Inlines.Add(Text(" like this — HWP's \"treat as character\". It stays fully editable: "
                                  + "click into a cell and type."));
        b.Add(inlineHost);

        // ---- 7. page layout ----------------------------------------------------------------------
        b.Add(Head("7. Page layout, print and PDF", 2, 16));
        b.Add(Body("This document is set to A4 with a header, a footer and page numbers — look at the top "
                 + "and bottom of the page. Paper size and orientation are on the toolbar, and the page "
                 + "setup is saved with the document. Print and PDF export render the same pages."));
        b.Add(Body("Try it: change the paper size on the toolbar, then use Export to write .flow, HTML, "
                 + "JSON or RTF and open the result in Word or a browser.", p => p.Foreground(Muted)));

        return doc;
    }

    // ---- building blocks -------------------------------------------------------------------------

    private static Run Text(string s, FontWeight weight = FontWeight.Normal, double size = 11) =>
        new() { Text = s, FontSize = size, FontWeight = weight, Foreground = Ink };

    private static Paragraph Head(string s, int level, double size)
    {
        var p = new Paragraph { HeadingLevel = level, MarginTop = level == 1 ? 0 : 16, MarginBottom = 6 };
        p.Inlines.Add(new Run { Text = s, FontSize = size, FontWeight = FontWeight.Bold, Foreground = Ink });
        return p;
    }

    private static Paragraph Body(string s, System.Action<Paragraph>? tweak = null)
    {
        var p = new Paragraph();
        p.Inlines.Add(Text(s));
        tweak?.Invoke(p);
        return p;
    }

    private static Paragraph Caption(string s)
    {
        var p = new Paragraph { MarginTop = 2 };
        p.Inlines.Add(new Run { Text = s, FontSize = 9, Foreground = Muted, FontStyle = FontStyle.Italic });
        return p;
    }

    private static Paragraph Item(string s, ListKind kind, int level,
                                  ListMarkerStyle marker = ListMarkerStyle.Default)
    {
        var p = Body(s);
        p.ListType = kind;
        p.ListLevel = level;
        p.ListMarker = marker;
        return p;
    }

    // Recolours every run in a paragraph — used for the muted asides.
    private static void Foreground(this Paragraph p, IBrush brush)
    {
        foreach (var inline in p.Inlines)
            if (inline is Run r) r.Foreground = brush;
    }

    // ---- tables ----------------------------------------------------------------------------------

    // `new TableBlock(r, c) { ColumnWidths = { … } }` does NOT work: that is collection-initializer
    // syntax, so it APPENDS to the widths the constructor already filled in (100 per column) and the
    // declared numbers land past the end where nothing reads them. Every column stays 100 wide — which
    // is how a nested table ended up wider than the cell holding it. Replace, don't append.
    private static TableBlock Widths(TableBlock t, params double[] widths)
    {
        t.ColumnWidths.Clear();
        t.ColumnWidths.AddRange(widths);
        return t;
    }

    private static TableBlock FeatureTable()
    {
        var t = Widths(new TableBlock(4, 3), 150, 200, 160);

        // A merged, shaded header spanning the first two columns.
        Cell(t, 0, 0, "Feature tour", bold: true);
        t.MergeCells(0, 0, 0, 1);
        Cell(t, 0, 2, "Notes", bold: true);
        t.Cells[0][0].Background = HeadCell;
        t.Cells[0][2].Background = HeadCell;

        Cell(t, 1, 0, "Merged cells");
        Cell(t, 1, 1, "colspan and rowspan, both directions");
        Cell(t, 1, 2, "Right-click a selection to merge");

        // A cell holding more than one paragraph, plus a list item.
        var multi = t.Cells[2][0];
        multi.Blocks.Clear();
        multi.Blocks.Add(Body("Blocks in a cell"));
        multi.Blocks.Add(Item("a list item", ListKind.Bullet, 0));
        multi.Blocks.Add(Body("and a second paragraph."));
        Cell(t, 2, 1, "Cells hold paragraphs, pictures, dividers and tables");
        Cell(t, 2, 2, "Tab moves cell to cell");

        Cell(t, 3, 0, "Nested table");
        Cell(t, 3, 1, "To any depth, with its own resize handles");
        // The nested table lives in the last cell, between two paragraphs of its own.
        var nest = t.Cells[3][2];
        nest.Blocks.Clear();
        nest.Blocks.Add(Caption("nested:"));
        nest.Blocks.Add(MiniTable());
        return t;
    }

    private static TableBlock MiniTable()
    {
        var t = Widths(new TableBlock(2, 2), 46, 46);
        Cell(t, 0, 0, "A", size: 9);
        Cell(t, 0, 1, "B", size: 9);
        Cell(t, 1, 0, "C", size: 9);
        Cell(t, 1, 1, "D", size: 9);
        return t;
    }

    private static void Cell(TableBlock t, int r, int c, string text, bool bold = false, double size = 10)
    {
        var p = t.Cells[r][c].Para;
        p.Inlines.Clear();
        p.Inlines.Add(new Run
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Foreground = Ink,
        });
    }

    // ---- pictures --------------------------------------------------------------------------------

    // Drawn with Avalonia rather than shipped as an asset: the demo then carries no binary blob, and the
    // picture is a real decoded PNG so resize, replace, save and export all exercise the normal path.
    private static ImageBlock Picture()
    {
        var img = new ImageBlock { Width = 320, Height = 120, MarginTop = 4 };
        img.SetImageData(RenderPng(640, 240, ctx =>
        {
            ctx.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x0B, 0x62, 0xC4), 0),
                    new GradientStop(Color.FromRgb(0x6D, 0x3B, 0xC4), 1),
                },
            }, new Rect(0, 0, 640, 240));
            for (int i = 0; i < 5; i++)
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 3),
                                new Point(520, 60), 40 + i * 26, 40 + i * 26);
        }), "image/png");
        return img;
    }

    private static InlineImage Icon()
    {
        var icon = new InlineImage { Width = 14, Height = 14 };
        icon.SetImageData(RenderPng(28, 28, ctx =>
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x35)), new Rect(0, 0, 28, 28), 6);
            ctx.DrawLine(new Pen(Brushes.White, 4), new Point(7, 15), new Point(12, 20));
            ctx.DrawLine(new Pen(Brushes.White, 4), new Point(12, 20), new Point(21, 8));
        }), "image/png");
        return icon;
    }

    private static byte[] RenderPng(int w, int h, System.Action<DrawingContext> draw)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext()) draw(ctx);
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}
