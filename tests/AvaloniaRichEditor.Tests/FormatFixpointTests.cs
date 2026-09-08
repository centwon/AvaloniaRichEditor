using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round 14. Every round-trip test in this suite saves and loads ONCE. That cannot see a defect which
// only shows up as ACCUMULATION, and this project has shipped two of exactly that shape:
//
//   round 8 — an empty paragraph under a block image multiplied on every round trip
//   round 9 — RTF carried no paper size, found by re-reading a generated file four times and watching
//             PageSize change
//
// Both were caught by a person opening a file repeatedly by hand. Nothing guards them.
//
// The contract here is deliberately narrower than "nothing is lost", because the interop formats are
// asymmetric by design — RTF has no inline table, HTML flattens some structure — and demanding fidelity
// from them would just assert the design away. What is demanded instead:
//
//     pass 1 may lose whatever the format cannot represent. Pass 2 has nothing left to lose.
//
// So pass 1 is the baseline and every later pass must equal it exactly. Anything that grows, shrinks,
// drifts or alternates fails, and there is no "expected loss" excuse available to it.
//
// Cycling ACROSS formats (json -> html -> rtf -> ...) is deliberately NOT tested: the designed losses of
// four formats compound, and a difference at the end cannot be attributed to a defect rather than to the
// asymmetry each format documents.
public class FormatFixpointTests
{
    private const int Passes = 4;

    // The model state, as the persistence format sees it. Using the JSON serializer as the oracle means
    // the comparison covers everything the document model persists — text, runs, formatting, list state,
    // table geometry including merges, images, page setup — without a hand-written field list that would
    // forget the same properties a hand-written clone forgets.
    private static string Signature(FlowDocument doc) => DocumentSerializer.Serialize(doc);

    // A save/open cycle as a host performs it: the document goes through the editor, which is what wires
    // parents and runs NormalizeBlocks. Leaving the editor out would test the formatters against each
    // other rather than against the product.
    // NOTE: nothing may set PageSize here. Assigning Document drives the control's page properties FROM
    // the document (SyncPageSetupOnDocumentChanged); setting PageSize afterwards runs the sync the other
    // way (CapturePageSetupToDocument) and overwrites the document's own paper with the control's. An
    // earlier version of this helper did exactly that and manufactured a "defect" where an A4 landscape
    // document came back portrait — the harness had erased the paper, not the formatter.
    private static FlowDocument Host(FlowDocument doc)
    {
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Avalonia.Size(700, double.PositiveInfinity));
        return ed.Document!;
    }

    // Round 14 left four of these cases failing-by-skip because the mechanism behind them was not
    // established. Round 15 established it and fixed all four (Project_Roadmap.md, round 15):
    //   · rtf/nested-table, rtf/kitchen-sink — the \par the writer puts before a nested table came back
    //     as CONTENT, so a cell grew one blank line per round trip.
    //   · html/kitchen-sink — no content was lost; the run LIST differed, because an import splits a run
    //     per source node and an export welds them back. Both importers now coalesce.
    //   · html/plain — the same fragmentation, and it did not merely fail: the run holding nothing but
    //     the soft break hit an Avalonia 12.0.1 text-wrapping loop that eats memory until the process
    //     dies. Coalescing keeps this suite off that shape; the engine bug itself is tracked separately.
    private static void AssertReachesFixpoint(string format, string name, FlowDocument seed, Func<FlowDocument, FlowDocument> trip)
    {
        var doc = Host(trip(seed));
        string baseline = Signature(doc);

        for (int pass = 2; pass <= Passes; pass++)
        {
            doc = Host(trip(doc));
            string now = Signature(doc);
            if (now != baseline)
                Assert.Fail($"{format}: pass {pass} differs from pass 1 — the round trip is not idempotent.\n" +
                            $"pass 1 length {baseline.Length}, pass {pass} length {now.Length}\n" +
                            $"first difference at {FirstDiff(baseline, now)}");
        }
    }

    private static string FirstDiff(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        string ctx(string s) => i >= s.Length ? "<end>" : s.Substring(i, Math.Min(90, s.Length - i));
        return $"offset {i}\n  pass1: {ctx(a)}\n  passN: {ctx(b)}";
    }

    // ---- the formats -------------------------------------------------------

    private static FlowDocument ViaJson(FlowDocument d)
        => DocumentSerializer.Deserialize(DocumentSerializer.Serialize(d));

    private static FlowDocument ViaFlow(FlowDocument d)
    {
        using var ms = new MemoryStream();
        DocumentPackage.Save(d, ms);
        ms.Position = 0;
        return DocumentPackage.Load(ms);
    }

    private static FlowDocument ViaHtml(FlowDocument d)
        => HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(d));

    private static FlowDocument ViaRtf(FlowDocument d)
        => RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(d));

    // ---- the documents -----------------------------------------------------

    public static TheoryData<string> Documents
    {
        get
        {
            var d = new TheoryData<string>();
            // ARE_FIXPOINT_DOC narrows the theory to one document, so a case that kills the test host
            // (StackOverflow and OOM are not catchable — the process just dies, taking the report with
            // it) can be identified without bisecting by hand.
            var only = Environment.GetEnvironmentVariable("ARE_FIXPOINT_DOC");
            if (!string.IsNullOrEmpty(only)) { d.Add(only); return d; }
            foreach (var name in new[]
            {
                "plain", "soft-breaks", "empty-paragraphs", "list", "headings", "styled",
                "table", "merged-table", "nested-table", "inline-table",
                "image", "divider", "page-setup", "kitchen-sink",
            }) d.Add(name);
            return d;
        }
    }

    private static FlowDocument Build(string name)
    {
        switch (name)
        {
            case "plain":
                return TestHelpers.Doc(
                    TestHelpers.Para(new Run { Text = "first" }),
                    TestHelpers.Para(new Run { Text = "second line\nwith a soft break" }));

            // Round 15's shape: an EMPTY soft line (two Shift+Enters). A format that spells a soft break
            // as a marker rather than as text tends to lose the second one, or to add one back.
            case "soft-breaks":
                return TestHelpers.Doc(
                    TestHelpers.Para(new Run { Text = "before\n\nafter" }),
                    TestHelpers.Para(new Run { Text = "\nleading break" }),
                    TestHelpers.Para(new Run { Text = "trailing break\n" }));

            // Round 8's shape: an empty paragraph is content, and a format that cannot say "empty"
            // tends to either drop it or emit one extra every time.
            case "empty-paragraphs":
                return TestHelpers.Doc(
                    TestHelpers.Para(new Run { Text = "before" }),
                    TestHelpers.Para(new Run { Text = "" }),
                    TestHelpers.Para(new Run { Text = "" }),
                    TestHelpers.Para(new Run { Text = "after  two  spaces" }));

            case "list":
            {
                var a = TestHelpers.Para(new Run { Text = "one" });
                var b = TestHelpers.Para(new Run { Text = "two" });
                var c = TestHelpers.Para(new Run { Text = "nested" });
                a.ListType = ListKind.Bullet;
                b.ListType = ListKind.Bullet;
                c.ListType = ListKind.Ordered;
                c.Indent = 40;
                return TestHelpers.Doc(a, b, c, TestHelpers.Para(new Run { Text = "tail" }));
            }

            case "headings":
            {
                var h1 = TestHelpers.Para(new Run { Text = "Title" });
                var h3 = TestHelpers.Para(new Run { Text = "Sub" });
                h1.HeadingLevel = 1;
                h3.HeadingLevel = 3;
                return TestHelpers.Doc(h1, TestHelpers.Para(new Run { Text = "body" }), h3,
                    TestHelpers.Para(new Run { Text = "more" }));
            }

            case "styled":
            {
                var p = TestHelpers.Para(
                    new Run { Text = "bold", FontWeight = Avalonia.Media.FontWeight.Bold },
                    new Run { Text = "italic", FontStyle = Avalonia.Media.FontStyle.Italic },
                    new Run { Text = "big", FontSize = 24 },
                    new Run { Text = "red", Foreground = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Colors.Red) });
                p.TextAlignment = Avalonia.Media.TextAlignment.Center;
                return TestHelpers.Doc(p, TestHelpers.Para(new Run { Text = "plain" }));
            }

            case "table":
                return TestHelpers.Doc(TestHelpers.Para(new Run { Text = "before" }),
                    Table(2, 3), TestHelpers.Para(new Run { Text = "after" }));

            // Round 9's shape: the horizontal merge that Word rendered as an empty column.
            case "merged-table":
            {
                var tb = Table(3, 3);
                tb.MergeCells(0, 0, 0, 1); // horizontal
                tb.MergeCells(1, 2, 2, 2); // vertical
                return TestHelpers.Doc(TestHelpers.Para(new Run { Text = "before" }), tb,
                    TestHelpers.Para(new Run { Text = "after" }));
            }

            case "nested-table":
            {
                var outer = Table(2, 2);
                var inner = Table(2, 2);
                outer.Cells[0][0].Blocks.Add(inner);
                return TestHelpers.Doc(TestHelpers.Para(new Run { Text = "before" }), outer,
                    TestHelpers.Para(new Run { Text = "after" }));
            }

            case "inline-table":
                return TestHelpers.Doc(
                    TestHelpers.Para(new Run { Text = "text " }, TestHelpers.Tbl("cell"), new Run { Text = " more" }),
                    TestHelpers.Para(new Run { Text = "after" }));

            case "image":
                return TestHelpers.Doc(
                    TestHelpers.Para(new Run { Text = "before" }),
                    Image(32),
                    TestHelpers.Para(new Run { Text = "with an inline " }, Inline(16)),
                    TestHelpers.Para(new Run { Text = "after" }));

            case "divider":
                return TestHelpers.Doc(TestHelpers.Para(new Run { Text = "above" }),
                    new DividerBlock(), TestHelpers.Para(new Run { Text = "below" }));

            case "page-setup":
            {
                var doc = TestHelpers.Doc(TestHelpers.Para(new Run { Text = "paged" }));
                doc.PageSetup = new PageSetup
                {
                    PageSize = RichEditorPageSize.A4,
                    Orientation = RichEditorPageOrientation.Landscape,
                    Header = "the header",
                    Footer = "the footer",
                    ShowPageNumbers = true,
                };
                return doc;
            }

            default: // kitchen-sink: the combination the axis-by-axis documents never form
            {
                var tb = Table(2, 2);
                tb.MergeCells(0, 0, 0, 1);
                tb.Cells[1][0].Blocks.Add(Table(1, 2));
                tb.Cells[1][1].Blocks.Add(TestHelpers.Para(new Run { Text = "second para in cell" }));
                var host = TestHelpers.Para(new Run { Text = "inline " }, TestHelpers.Tbl("in-line"), new Run { Text = " tail" });
                host.ListType = ListKind.Bullet;
                var doc = TestHelpers.Doc(
                    TestHelpers.Para(new Run { Text = "top" }),
                    tb, host,
                    Image(24),
                    new DividerBlock(),
                    TestHelpers.Para(new Run { Text = "" }),
                    TestHelpers.Para(new Run { Text = "end" }));
                doc.PageSetup = new PageSetup { PageSize = RichEditorPageSize.A4 };
                return doc;
            }
        }
    }

    private static TableBlock Table(int rows, int cols)
    {
        var tb = new TableBlock(rows, cols);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                tb.Cells[r][c].Para.Inlines.Add(new Run { Text = $"r{r}c{c}" });
        return tb;
    }

    private static ImageBlock Image(double px)
    {
        var b = new ImageBlock { Width = px, Height = px };
        b.SetImageData(Png, "image/png");
        return b;
    }

    private static InlineImage Inline(double px)
    {
        var i = new InlineImage { Width = px, Height = px };
        i.SetImageData(Png, "image/png");
        return i;
    }

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // ---- the tests ---------------------------------------------------------

    [AvaloniaTheory]
    [MemberData(nameof(Documents))]
    public void Json_ReachesAFixpoint(string doc) => AssertReachesFixpoint("json", doc, Build(doc), ViaJson);

    [AvaloniaTheory]
    [MemberData(nameof(Documents))]
    public void Flow_ReachesAFixpoint(string doc) => AssertReachesFixpoint("flow", doc, Build(doc), ViaFlow);

    [AvaloniaTheory]
    [MemberData(nameof(Documents))]
    public void Html_ReachesAFixpoint(string doc) => AssertReachesFixpoint("html", doc, Build(doc), ViaHtml);

    [AvaloniaTheory]
    [MemberData(nameof(Documents))]
    public void Rtf_ReachesAFixpoint(string doc) => AssertReachesFixpoint("rtf", doc, Build(doc), ViaRtf);
}
