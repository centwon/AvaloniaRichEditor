using System;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Guards against spec/code drift: the worked example in docs/DOCUMENT_FORMAT.md (§2.6) must keep
// loading into exactly the structure the surrounding tables describe. If a field is renamed or a
// discriminator changes in code, the documented JSON stops producing the documented document and
// this test fails — a prompt to update the spec (or the code) rather than letting them silently part.
public class DocumentFormatSpecTests
{
    private static string SpecPath()
    {
        // Walk up from the test bin directory to the repo root (the folder holding docs/).
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "docs", "DOCUMENT_FORMAT.md");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Could not locate docs/DOCUMENT_FORMAT.md from " + AppContext.BaseDirectory);
    }

    // Extract the worked example: the ```json fenced block that follows the "### 2.6 예제" heading.
    private static string ExtractExampleJson(string markdown)
    {
        int heading = markdown.IndexOf("### 2.6", StringComparison.Ordinal);
        Assert.True(heading >= 0, "spec is missing the §2.6 example heading");
        var m = Regex.Match(markdown.Substring(heading), "```json\\s*\\r?\\n(.*?)```", RegexOptions.Singleline);
        Assert.True(m.Success, "spec §2.6 is missing its ```json example block");
        return m.Groups[1].Value;
    }

    [Fact]
    public void Spec_Example_LoadsIntoDocumentedStructure()
    {
        string json = ExtractExampleJson(File.ReadAllText(SpecPath()));
        var doc = DocumentSerializer.Deserialize(json);

        // §2.6 documents three top-level blocks: a heading paragraph, a block image, a 1x2 table.
        Assert.Equal(3, doc.Blocks.Count);

        var heading = Assert.IsType<Paragraph>(doc.Blocks[0]);
        Assert.Equal(1, heading.HeadingLevel);
        var run = Assert.IsType<Run>(heading.Inlines[0]);
        Assert.Equal("제목", run.Text);
        Assert.Equal(FontWeight.Bold, run.FontWeight);
        Assert.Equal(20, run.FontSize);

        // Image block resolves its ImageRef against the document-level Images pool (schema v2).
        var img = Assert.IsType<ImageBlock>(doc.Blocks[1]);
        Assert.NotNull(img.RawBytes);
        Assert.Equal("image/png", img.MimeType);

        var table = Assert.IsType<TableBlock>(doc.Blocks[2]);
        Assert.Equal(1, table.Rows);
        Assert.Equal(2, table.Columns);
        Assert.Equal("셀1", table.Cells[0][0].Text());
    }
}
