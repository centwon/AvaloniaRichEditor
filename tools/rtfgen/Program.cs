using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;

namespace RtfGen;

/// <summary>Generates the documents used for EXTERNAL visual verification (Word / HWP / a browser).
///
/// The point of this tool is the one thing the test suite structurally cannot do: this project's reader is
/// lenient about this project's own writer, so a self round trip agrees with itself even when the bytes are
/// wrong for everybody else. Every section below is a shape whose OUTPUT changed in rounds 4-8; each is
/// labelled in the document itself so a human can check it against CHECKLIST.md without reading this file.
///
/// Usage:  dotnet run --project rtfgen [outputDir]
/// </summary>
internal static class Program
{
    static readonly byte[] BigPng = Png.Swatch(160, 100, 0x4F, 0x8A, 0xD8);   // block image
    static readonly byte[] SmallPng = Png.Swatch(32, 32, 0xD8, 0x6F, 0x4F);   // inline icon

    static int Main(string[] args)
    {
        // --read <file.rtf>: parse an RTF with THIS project's reader and print the table geometry it
        // built. Used to check a candidate byte format against our own reader, next to what Word does.
        if (args.Length >= 2 && args[0] == "--read")
        {
            var parsed = RtfDocumentFormatter.Parse(File.ReadAllText(args[1]));
            Console.WriteLine($"blocks={parsed.Blocks.Count}");
            foreach (var t in parsed.Blocks.OfType<TableBlock>())
            {
                Console.WriteLine($"table {t.Rows}x{t.Columns}  widths=[{string.Join(",", t.ColumnWidths)}]");
                foreach (var (r, c, cell) in t.LogicalCells())
                {
                    var (cs, rs) = t.SpanOf(r, c);
                    string text = string.Concat(cell.Para.Inlines.OfType<Run>().Select(x => x.Text));
                    Console.WriteLine($"  r{r}c{c} span={cs}x{rs} text='{text}'");
                }
            }
            return 0;
        }

        // --recycle <file.rtf> <n>: read and re-write the file n times with THIS project's reader and
        // writer, printing the page chrome each cycle. Checklist #21: round 6's footer separator was
        // collected as footer CONTENT and grew a " / " on every save. One cycle looked fine, which is
        // exactly why this runs several.
        if (args.Length >= 2 && args[0] == "--recycle")
        {
            int n = args.Length >= 3 ? int.Parse(args[2]) : 3;
            string rtf = File.ReadAllText(args[1]);
            for (int i = 1; i <= n; i++)
            {
                var d = RtfDocumentFormatter.Parse(rtf);
                var ps = d.PageSetup;
                Console.WriteLine($"cycle {i}: header='{ps?.Header}' footer='{ps?.Footer}' " +
                                  $"pageNumbers={ps?.ShowPageNumbers} size={ps?.PageSize} blocks={d.Blocks.Count}");
                rtf = RtfDocumentFormatter.Write(d);
            }
            return 0;
        }

        string outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);

        Emit(outDir, "01-kitchen-sink", KitchenSink());
        // A document with one empty paragraph, which is what a freshly opened editor holds — NOT a
        // FlowDocument with no blocks at all. The round-8 defect (the editor's own tags shown as body
        // text) needs the shape the editor actually saves.
        var empty = new FlowDocument();
        empty.Blocks.Add(new Paragraph());
        Emit(outDir, "02-empty", empty);
        Emit(outDir, "03-tables", Tables());
        Emit(outDir, "04-page-chrome", PageChrome());

        Console.WriteLine($"\nWrote to {outDir}");
        Console.WriteLine("Open the .rtf in Word and HWP, the .html in a browser and in Word.");
        Console.WriteLine("Check against CHECKLIST.md.");
        return 0;
    }

    static void Emit(string dir, string name, FlowDocument doc)
    {
        var rtf = RtfDocumentFormatter.Write(doc);
        var html = HtmlDocumentFormatter.ToHtml(doc);
        File.WriteAllText(Path.Combine(dir, name + ".rtf"), rtf);
        File.WriteAllText(Path.Combine(dir, name + ".html"), html);
        Console.WriteLine($"{name,-18} rtf {rtf.Length,8:N0} B   html {html.Length,8:N0} B");
    }

    // ---------------------------------------------------------------- helpers

    static Paragraph P(string text, Action<Paragraph>? tweak = null)
    {
        var p = new Paragraph { Inlines = { new Run { Text = text, FontSize = 11 } } };
        tweak?.Invoke(p);
        return p;
    }

    /// <summary>A section banner, so every check in CHECKLIST.md is findable in the rendered document.</summary>
    static Paragraph Section(string title) => new()
    {
        HeadingLevel = 2,
        MarginTop = 18,
        Inlines = { new Run { Text = title, FontSize = 14, FontWeight = FontWeight.Bold } }
    };

    static Run T(string text, double size = 11) => new() { Text = text, FontSize = size };

    static ImmutableSolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));

    // ---------------------------------------------------------------- documents

    /// <summary>Inline-level and paragraph-level shapes: whitespace, link colour, lists, blank lines,
    /// spacing, images, dividers, inline tables.</summary>
    static FlowDocument KitchenSink()
    {
        var doc = new FlowDocument();
        var b = doc.Blocks;

        b.Add(new Paragraph
        {
            HeadingLevel = 1,
            Inlines = { new Run { Text = "AvaloniaRichEditor — 외부 앱 육안검증 문서", FontSize = 18, FontWeight = FontWeight.Bold } }
        });
        b.Add(P("이 파일은 라운드 4~8에서 바뀐 출력만 모은 것이다. 각 절의 번호는 CHECKLIST.md와 같다."));

        // --- 1. consecutive spaces (HTML &nbsp; runs; round 8) -------------------
        b.Add(Section("1. 연속 공백"));
        b.Add(P("사이에[    ]공백 4칸. 앞뒤 대괄호 사이 간격이 한 칸으로 줄면 실패."));
        b.Add(P("들여쓰기용    공백도    폭을 유지해야 한다."));
        b.Add(new Paragraph { Inlines = { new Run { Text = "    ", FontSize = 11 } } }); // run of only spaces
        b.Add(P("↑ 바로 위는 공백만 있는 문단이다(빈 줄로 보여야 하며 사라지면 안 된다)."));

        // --- 2. hyperlink colour (data-are-fg; round 4) --------------------------
        b.Add(Section("2. 하이퍼링크 색"));
        b.Add(new Paragraph
        {
            Inlines =
            {
                T("문서가 정한 색을 가진 링크: "),
                new Run
                {
                    Text = "이 링크는 초록색이어야 한다",
                    FontSize = 11,
                    NavigateUri = "https://github.com/centwon/AvaloniaRichEditor",
                    Foreground = Brush(0x0B, 0x7A, 0x35),
                    TextDecorations = TextDecorations.Underline
                },
                T(" ← 파란색으로 보이면 실패.")
            }
        });

        // --- 3. lists: nesting, markers, list-item-that-is-a-heading -------------
        b.Add(Section("3. 목록"));
        b.Add(P("1단계 항목", p => { p.ListType = ListKind.Bullet; p.ListLevel = 0; }));
        b.Add(P("2단계 항목 (한 칸 더 들여써야 한다)", p => { p.ListType = ListKind.Bullet; p.ListLevel = 1; }));
        b.Add(P("3단계 항목", p => { p.ListType = ListKind.Bullet; p.ListLevel = 2; }));
        b.Add(P("다시 1단계", p => { p.ListType = ListKind.Bullet; p.ListLevel = 0; }));
        b.Add(P("번호 항목 하나", p => { p.ListType = ListKind.Ordered; p.ListMarker = ListMarkerStyle.Decimal; }));
        b.Add(P("번호 항목 둘", p => { p.ListType = ListKind.Ordered; p.ListMarker = ListMarkerStyle.Decimal; }));
        b.Add(P("목록이면서 제목(레벨 3)인 항목", p =>
        {
            p.ListType = ListKind.Bullet;
            p.HeadingLevel = 3;
        }));
        b.Add(P("↑ 마커(•, 1.)가 본문 글자로 섞여 들어가면 실패. 마커 뒤 간격이 줄 끝까지 튀어도 실패."));

        // --- 4. an author's blank line (data-are-empty; round 4) -----------------
        b.Add(Section("4. 저자가 넣은 빈 줄"));
        b.Add(P("위 문단."));
        b.Add(new Paragraph());                       // deliberately empty
        b.Add(P("아래 문단. 두 문단 사이에 빈 줄이 정확히 하나 있어야 한다."));

        // --- 5. paragraph-level formatting (round 8: spacing actually written) ---
        b.Add(Section("5. 문단 서식"));
        b.Add(P("가운데 정렬", p => p.TextAlignment = TextAlignment.Center));
        b.Add(P("오른쪽 정렬", p => p.TextAlignment = TextAlignment.Right));
        b.Add(P("왼쪽 정렬 — 위가 오른쪽 정렬이라고 해서 이 줄까지 따라가면 실패(HWP)", p => p.TextAlignment = TextAlignment.Left));
        b.Add(P("들여쓴 문단 (48pt)", p => p.Indent = 48));
        b.Add(P("위아래 여백이 큰 문단 (24pt/24pt) — 앞뒤 줄과의 간격이 눈에 띄게 넓어야 한다", p =>
        {
            p.MarginTop = 24;
            p.MarginBottom = 24;
        }));
        b.Add(P("배경색이 있는 문단", p => p.Background = Brush(0xFF, 0xF3, 0xC4)));

        // --- 6. images (round 4: trailing blank line, image after table) ---------
        b.Add(Section("6. 이미지"));
        b.Add(P("아래는 자기 문단에 혼자 있는 블록 이미지다. 바로 앞 문단에 달라붙으면 실패."));
        var img = new ImageBlock { Width = 160, Height = 100 };
        img.SetImageData(BigPng, "image/png");
        b.Add(img);
        b.Add(P("↑ 이미지 아래 빈 줄이 하나도 생기면 안 된다(왕복마다 늘어나던 자리)."));
        var icon = new InlineImage { Width = 16, Height = 16 };
        icon.SetImageData(SmallPng, "image/png");
        b.Add(new Paragraph { Inlines = { T("글자 사이 인라인 아이콘 "), icon, T(" 이 줄 안에 있어야 한다.") } });

        // --- 7. divider (round 4: \brdrb had no reader) --------------------------
        b.Add(Section("7. 구분선"));
        b.Add(P("아래에 가로 구분선이 하나 있어야 한다(빈 줄이면 실패)."));
        b.Add(new DividerBlock());
        b.Add(P("구분선 아래 문단."));

        // --- 8. inline table (data-are-inline / -opens; HTML width) --------------
        b.Add(Section("8. 인라인 표"));
        var it = new InlineTable { Table = Grid2x2("가", "나", "다", "라") };
        b.Add(new Paragraph { Inlines = { T("문장 중간에 "), it, T(" 표가 글자처럼 들어간다.") } });
        var it2 = new InlineTable { Table = Grid2x2("A", "B", "C", "D") };
        b.Add(new Paragraph { Inlines = { it2, T(" ← 이 표는 문단 맨 앞에서 시작한다.") } });
        b.Add(P("↑ 브라우저·Word에서 인라인 표가 전폭(100%) 블록으로 깔리면 실패."));

        return doc;
    }

    /// <summary>Table shapes: borders, merges, cell background, cell-level paragraph properties, multiple
    /// paragraphs per cell, nesting, a picture in a cell, and a picture straight after a table.</summary>
    static FlowDocument Tables()
    {
        var doc = new FlowDocument();
        var b = doc.Blocks;

        b.Add(new Paragraph
        {
            HeadingLevel = 1,
            Inlines = { new Run { Text = "표 (Tables)", FontSize = 18, FontWeight = FontWeight.Bold } }
        });

        // --- 9. borders + merges + cell background -------------------------------
        b.Add(Section("9. 테두리 · 병합 · 셀 배경"));
        var t = new TableBlock(3, 4) { ColumnWidths = { 120, 120, 120, 120 } };
        t.Cells[0][0].Para.Inlines[0] = T("가로 병합 2칸");
        t.MergeCells(0, 0, 0, 1);
        t.Cells[1][3].Para.Inlines[0] = T("세로 병합 2칸");
        t.MergeCells(1, 3, 2, 3);
        t.Cells[0][2].Para.Inlines[0] = T("배경색");
        t.Cells[0][2].Background = Brush(0xD8, 0xEC, 0xFF);
        t.Cells[0][3].Para.Inlines[0] = T("일반");
        t.Cells[1][0].Para.Inlines[0] = T("좌");
        t.Cells[1][1].Para.Inlines[0] = T("중");
        t.Cells[1][2].Para.Inlines[0] = T("우");
        t.Cells[2][0].Para.Inlines[0] = T("아래 좌");
        t.Cells[2][1].Para.Inlines[0] = T("아래 중");
        t.Cells[2][2].Para.Inlines[0] = T("아래 우");
        b.Add(t);
        b.Add(P("↑ 모든 셀에 테두리가 보여야 한다(Word/HWP에서 테두리가 아예 없던 자리). 병합된 칸 안에는 내부 선이 없어야 한다."));

        // --- 10. paragraph properties inside a cell ------------------------------
        b.Add(Section("10. 셀 안 문단 서식"));
        var t2 = new TableBlock(1, 4) { ColumnWidths = { 130, 130, 130, 130 } };
        t2.Cells[0][0].Blocks[0] = P("가운데", p => p.TextAlignment = TextAlignment.Center);
        t2.Cells[0][1].Blocks[0] = P("오른쪽", p => p.TextAlignment = TextAlignment.Right);
        t2.Cells[0][2].Blocks[0] = P("글머리 항목", p => p.ListType = ListKind.Bullet);
        t2.Cells[0][3].Blocks[0] = P("제목 레벨 3", p => p.HeadingLevel = 3);
        b.Add(t2);
        b.Add(P("↑ 셀 안에서도 정렬·글머리·제목이 살아 있어야 한다(전부 왼쪽 평문이 되면 실패)."));

        // --- 11. multiple paragraphs in one cell ---------------------------------
        b.Add(Section("11. 셀 안 여러 문단"));
        var t3 = new TableBlock(1, 2) { ColumnWidths = { 240, 240 } };
        t3.Cells[0][0].Blocks.Clear();
        t3.Cells[0][0].Blocks.Add(P("첫 번째 문단."));
        t3.Cells[0][0].Blocks.Add(P("두 번째 문단 — 위와 합쳐져 한 줄이 되면 실패."));
        t3.Cells[0][1].Blocks.Clear();
        t3.Cells[0][1].Blocks.Add(P("셀 안 이미지:"));
        var cellImg = new ImageBlock { Width = 80, Height = 50 };
        cellImg.SetImageData(BigPng, "image/png");
        t3.Cells[0][1].Blocks.Add(cellImg);
        b.Add(t3);

        // --- 12. nested table ----------------------------------------------------
        b.Add(Section("12. 중첩 표"));
        var outer = new TableBlock(1, 2) { ColumnWidths = { 260, 260 } };
        outer.Cells[0][0].Blocks.Clear();
        outer.Cells[0][0].Blocks.Add(P("중첩 표 앞 문단"));
        outer.Cells[0][0].Blocks.Add(Grid2x2("중1", "중2", "중3", "중4"));
        outer.Cells[0][0].Blocks.Add(P("중첩 표 뒤 문단"));
        outer.Cells[0][1].Para.Inlines[0] = T("옆 셀");
        b.Add(outer);
        b.Add(P("↑ '앞 문단'이 중첩 표 첫 칸에 빨려 들어가거나 '뒤 문단'이 통째로 사라지면 실패."));

        // --- 13. image directly after a table ------------------------------------
        b.Add(Section("13. 표 바로 뒤 이미지"));
        b.Add(Grid2x2("표", "가", "먼저", "온다"));
        var after = new ImageBlock { Width = 120, Height = 75 };
        after.SetImageData(BigPng, "image/png");
        b.Add(after);
        b.Add(P("↑ 이미지가 표보다 위로 올라가면 실패."));

        return doc;
    }

    /// <summary>Header / footer / page numbers (round 6) — needs a real paper size and enough text to
    /// reach a second page, because the defect was invisible on a one-page document.</summary>
    static FlowDocument PageChrome()
    {
        var doc = new FlowDocument
        {
            PageSetup = new PageSetup
            {
                PageSize = RichEditorPageSize.A4,
                Orientation = RichEditorPageOrientation.Portrait,
                Header = "머리글 — AvaloniaRichEditor 검증",
                Footer = "바닥글 텍스트",
                ShowPageNumbers = true
            }
        };

        doc.Blocks.Add(new Paragraph
        {
            HeadingLevel = 1,
            Inlines = { new Run { Text = "머리글 / 바닥글 / 쪽번호", FontSize = 18, FontWeight = FontWeight.Bold } }
        });
        doc.Blocks.Add(P("Word/HWP에서 확인할 것: ① 머리글이 페이지 위 여백에 있고 본문 첫 문단으로 들어오지 않았는가 "
                       + "② 바닥글 왼쪽에 텍스트, 오른쪽에 쪽번호가 있는가 ③ 2페이지에도 같은 머리글/바닥글이 나오는가."));

        for (int i = 1; i <= 60; i++)
            doc.Blocks.Add(P($"{i:00} 본문 채우기 — 두 번째 페이지까지 넘어가게 하기 위한 줄이다. "
                           + "머리글과 바닥글은 페이지마다 반복되어야 한다."));

        return doc;
    }

    static TableBlock Grid2x2(string a, string b, string c, string d)
    {
        var t = new TableBlock(2, 2) { ColumnWidths = { 70, 70 } };
        t.Cells[0][0].Para.Inlines[0] = T(a, 10);
        t.Cells[0][1].Para.Inlines[0] = T(b, 10);
        t.Cells[1][0].Para.Inlines[0] = T(c, 10);
        t.Cells[1][1].Para.Inlines[0] = T(d, 10);
        return t;
    }
}
