using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AvaloniaRichTextBoxPort.Formatters;

// Phase 0 round-trip harness: for every *.html in inDir, run ParseHtml -> ToHtml and report which
// rich-text features survived. Heuristic, not a strict DOM diff — it counts feature "tokens" in the
// input vs the regenerated output so we can see exactly what ToHtml/ParseHtml currently drops.
public static class RoundTripHarness
{
    // (label, regex over HTML) pairs describing the features we care about for Jodit parity.
    private static readonly (string Label, Regex Rx)[] Features =
    {
        ("bold",            Rx("<b\\b|<strong\\b|font-weight\\s*:\\s*(bold|[6-9]00)")),
        ("italic",          Rx("<i\\b|<em\\b|font-style\\s*:\\s*italic")),
        ("underline",       Rx("<u\\b|text-decoration[^;\"']*underline")),
        ("strikethrough",   Rx("<s\\b|<strike\\b|line-through")),
        ("color",           Rx("(?<!background-)color\\s*:")),
        ("background",      Rx("background(-color)?\\s*:")),
        ("font-family",     Rx("font-family\\s*:")),
        ("font-size",       Rx("font-size\\s*:")),
        ("link",            Rx("<a\\b")),
        ("ul",              Rx("<ul\\b")),
        ("ol",              Rx("<ol\\b")),
        ("heading",         Rx("<h[1-6]\\b")),
        ("blockquote",      Rx("<blockquote\\b")),
        ("hr",              Rx("<hr\\b")),
        ("table",           Rx("<table\\b")),
        ("img",             Rx("<img\\b")),
        ("align",           Rx("text-align\\s*:")),
    };

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Run(string inDir, string outDir)
    {
        if (!Directory.Exists(inDir))
        {
            Console.WriteLine($"[roundtrip] input dir not found: {inDir}");
            return;
        }
        Directory.CreateDirectory(outDir);

        var files = Directory.GetFiles(inDir, "*.html", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var report = new StringBuilder();
        report.AppendLine($"# Round-trip report ({DateTime.Now:yyyy-MM-dd HH:mm})");
        report.AppendLine($"files: {files.Length}");
        report.AppendLine();

        foreach (var file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string input;
            try { input = File.ReadAllText(file); }
            catch (Exception ex) { report.AppendLine($"## {name}: READ ERROR {ex.Message}"); continue; }

            string output;
            try
            {
                var doc = HtmlDocumentFormatter.ParseHtml(input);
                output = HtmlDocumentFormatter.ToHtml(doc);
            }
            catch (Exception ex)
            {
                report.AppendLine($"## {name}: PARSE/EMIT EXCEPTION {ex.GetType().Name}: {ex.Message}");
                report.AppendLine();
                continue;
            }

            // Skip output files starting with the harness suffix to avoid re-reading our own output.
            File.WriteAllText(Path.Combine(outDir, name + ".out.html"), output);

            report.AppendLine($"## {name}");
            var losses = new List<string>();
            foreach (var (label, rx) in Features)
            {
                int cin = rx.Matches(input).Count;
                int cout = rx.Matches(output).Count;
                if (cin == 0 && cout == 0) continue;
                string mark = cout >= cin ? "ok " : "LOSS";
                if (cout < cin) losses.Add($"{label} ({cin}->{cout})");
                report.AppendLine($"  [{mark}] {label,-14} in={cin} out={cout}");
            }
            report.AppendLine(losses.Count == 0
                ? "  => OK (no feature drops detected)"
                : "  => LOSSES: " + string.Join(", ", losses));
            report.AppendLine();
        }

        string reportPath = Path.Combine(outDir, "report.txt");
        File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(report.ToString());
        Console.WriteLine($"[roundtrip] report written: {reportPath}");
    }
}
