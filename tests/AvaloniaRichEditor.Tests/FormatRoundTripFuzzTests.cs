using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Random documents through HTML and RTF, twice (ported in spirit from the WinUI peer's DocumentFuzzTests, 2026-11-01).
// FormatFixpointTests runs fourteen fixed documents; none of them held the characters each format escapes, so the
// escaping defects of 2026-11-01 — a link's &amp; growing by one "amp;" per HTML cycle, RTF header braces dropped —
// were outside what any round-trip test could see. Two checks, as the peer's:
//   · cycle 2 equals cycle 1 (JSON signature) — catches whatever ACCUMULATES;
//   · the link addresses equal the ORIGINAL's — idempotency cannot see a loss every cycle makes alike.
// Widen with RICHEDITOR_FUZZ_SEEDS for an audit run (no source edit).
public class FormatRoundTripFuzzTests
{
    private static readonly int Seeds =
        int.TryParse(Environment.GetEnvironmentVariable("RICHEDITOR_FUZZ_SEEDS"), out int s) && s > 0 ? s : 100;

    private static readonly (string name, Func<FlowDocument, FlowDocument> round)[] Rounds =
    {
        ("html", d => HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(d))),
        ("rtf", d => RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(d))),
    };

    // Tokens, not chars: an emoji is two UTF-16 units, and one unit at a time would make lone surrogates.
    private static readonly string[] TextTokens =
    {
        "가", "나", "a", "b", " ", "1", "\n",
        "&", "<", ">", "\"", "'", "{", "}", "\\", "\U0001F600", "é", "&amp;", "<b>", "&#128512;",
    };

    private static readonly string[] Families = { "Malgun Gothic", "Times New Roman", "Arial" };

    private static string RandomText(Random rng)
    {
        var sb = new StringBuilder();
        int n = rng.Next(1, 7);
        for (int i = 0; i < n; i++) sb.Append(TextTokens[rng.Next(TextTokens.Length)]);
        return sb.ToString();
    }

    private static string RandomLink(Random rng) => rng.Next(4) switch
    {
        0 => "https://example.com/" + rng.Next(100),
        1 => "https://example.com/?a=1&b=" + rng.Next(100),
        2 => "https://ko.wikipedia.org/wiki/한글" + rng.Next(100),
        _ => @"file:///C:\docs\" + rng.Next(100) + ".txt",
    };

    private static IBrush RandomBrush(Random rng)
        => new ImmutableSolidColorBrush(Color.FromRgb((byte)rng.Next(1, 256), (byte)rng.Next(256), (byte)rng.Next(256)));

    private static Run RandomRun(Random rng)
    {
        var r = new Run { Text = RandomText(rng) };
        if (rng.Next(3) == 0) r.FontWeight = FontWeight.Bold;
        if (rng.Next(3) == 0) r.FontStyle = FontStyle.Italic;
        if (rng.Next(4) == 0)
        {
            var decos = new TextDecorationCollection();
            if (rng.Next(2) == 0) decos.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            else decos.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            r.TextDecorations = decos;
        }
        if (rng.Next(4) == 0) r.Foreground = RandomBrush(rng);
        if (rng.Next(5) == 0) r.Background = RandomBrush(rng);
        if (rng.Next(4) == 0) r.NavigateUri = RandomLink(rng);
        if (rng.Next(4) == 0) r.FontFamily = Families[rng.Next(Families.Length)];
        if (rng.Next(4) == 0) r.FontSize = 8 + rng.Next(16);
        return r;
    }

    private static Paragraph RandomParagraph(Random rng)
    {
        var p = new Paragraph();
        int n = rng.Next(1, 5);
        for (int i = 0; i < n; i++) p.Inlines.Add(RandomRun(rng));
        if (rng.Next(5) == 0) p.HeadingLevel = rng.Next(1, 4);
        else if (rng.Next(5) == 0) p.ListType = rng.Next(2) == 0 ? ListKind.Bullet : ListKind.Ordered;
        return p;
    }

    private static FlowDocument RandomDocument(Random rng)
    {
        var blocks = new List<Block>();
        int n = rng.Next(1, 6);
        for (int i = 0; i < n; i++)
        {
            if (rng.Next(5) == 0)
            {
                var tb = new TableBlock(rng.Next(1, 3), rng.Next(1, 3));
                foreach (var row in tb.Cells)
                    foreach (var cell in row)
                    {
                        cell.Blocks.Clear();
                        cell.Blocks.Add(RandomParagraph(rng));
                    }
                blocks.Add(tb);
            }
            else blocks.Add(RandomParagraph(rng));
        }
        var doc = TestHelpers.Doc(blocks.ToArray());
        if (rng.Next(3) == 0) doc.PageSetup = new PageSetup { Header = RandomText(rng).Replace("\n", ""), Footer = RandomText(rng).Replace("\n", "") };
        return doc;
    }

    // Through the editor, as a host's open does (parents, NormalizeBlocks) — see FormatFixpointTests.Host.
    private static FlowDocument Host(FlowDocument doc)
    {
        var ed = new RichEditor { Document = doc };
        ed.Measure(new Avalonia.Size(700, double.PositiveInfinity));
        return ed.Document!;
    }

    // Distinct link addresses on runs with visible text, sorted: a set, so splitting or merging one link's runs is
    // not reported.
    private static string Links(FlowDocument doc)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is Paragraph p)
                    foreach (var inl in p.Inlines)
                    {
                        if (inl is Run r && !string.IsNullOrEmpty(r.NavigateUri) && !string.IsNullOrWhiteSpace(r.Text)) set.Add(r.NavigateUri!);
                        else if (inl is InlineTable it) foreach (var row in it.Table.Cells) foreach (var c in row) Walk(c.Blocks);
                    }
                else if (b is TableBlock tb) foreach (var row in tb.Cells) foreach (var c in row) Walk(c.Blocks);
            }
        }
        Walk(doc.Blocks);
        return string.Join(" | ", set);
    }

    [AvaloniaFact]
    public void RandomDocuments_RoundTripIdempotently_AndKeepTheirLinks()
    {
        // Collected, not thrown on the first seed: the tally says whether this is one defect or five.
        var failures = new List<string>();
        for (int seed = 0; seed < Seeds; seed++)
        {
            foreach (var (name, round) in Rounds)
            {
                var original = Host(RandomDocument(new Random(seed)));
                string once, twice, links;
                try
                {
                    var a = Host(round(original));
                    once = DocumentSerializer.Serialize(a);
                    links = Links(a);
                    twice = DocumentSerializer.Serialize(Host(round(a)));
                }
                catch (Exception ex)
                {
                    failures.Add($"seed {seed} / {name} threw: {ex}");
                    continue;
                }
                if (once != twice)
                    failures.Add($"seed {seed} / {name} is not idempotent.\n  cycle 1: {once}\n  cycle 2: {twice}");
                string before = Links(original);
                if (before != links)
                    failures.Add($"seed {seed} / {name} changed a link address.\n  before: {before}\n  after:  {links}");
            }
        }
        if (failures.Count > 0)
            throw new Xunit.Sdk.XunitException(
                $"{failures.Count} failure(s) over {Seeds} seeds:\n\n" + string.Join("\n\n", failures.Take(8)) +
                (failures.Count > 8 ? $"\n\n… and {failures.Count - 8} more" : ""));
    }
}
