using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The file-by-file audit of 2026-11-01: what the formats dropped, what the readers let through, empty undo steps.
public class Audit4Tests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static Paragraph[] Paras(FlowDocument d) => d.Blocks.OfType<Paragraph>().ToArray();

    // ---- RTF reader: paragraph format, fonts, symbols ----------------------------------------------

    [AvaloniaFact]
    public void Rtf_ReadsAlignmentIndentAndFont_AndPardResetsThem()
    {
        var doc = RtfDocumentFormatter.Parse(
            @"{\rtf1{\fonttbl{\f0 Arial;}{\f1 Times New Roman;}}\pard\qc\li720 \f1 Hello\par\pard World\par}");
        var ps = Paras(doc).Where(p => p.Text().Length > 0).ToArray();
        Assert.Equal("Hello", ps[0].Text());
        Assert.Equal(TextAlignment.Center, ps[0].TextAlignment);
        Assert.True(ps[0].Indent > 0);
        Assert.Equal("Times New Roman", ps[0].Inlines.OfType<Run>().First().FontFamily);
        Assert.Equal("World", ps[1].Text());
        Assert.NotEqual(TextAlignment.Center, ps[1].TextAlignment);
        Assert.Equal(0.0, ps[1].Indent);
    }

    [AvaloniaFact]
    public void Rtf_ReadsSymbolControlWords()
    {
        var doc = RtfDocumentFormatter.Parse(@"{\rtf1 a\rquote b\emdash c\ldblquote d\rdblquote\par}");
        Assert.Equal("a’b—c“d”", Paras(doc).First().Text());
    }

    [AvaloniaFact]
    public void Rtf_AlignmentSurvivesARoundTrip()
    {
        var p = TestHelpers.Para(new Run { Text = "mid" });
        p.TextAlignment = TextAlignment.Center;
        var back = RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(TestHelpers.Doc(p)));
        Assert.Equal(TextAlignment.Center, Paras(back).First(x => x.Text() == "mid").TextAlignment);
    }

    // ---- pictures keep inline vs block through HTML and RTF -----------------------------------------

    private static FlowDocument BigInline()
    {
        var img = new InlineImage { Width = 100, Height = 100 };
        img.SetImageData(Png, "image/png");
        return TestHelpers.Doc(TestHelpers.Para(new Run { Text = "a" }, img, new Run { Text = "b" }));
    }

    private static FlowDocument SmallBlock()
    {
        var ib = new ImageBlock { Width = 30, Height = 30 };
        ib.SetImageData(Png, "image/png");
        return TestHelpers.Doc(TestHelpers.Para(new Run { Text = "a" }), ib, TestHelpers.Para(new Run { Text = "b" }));
    }

    public static readonly TheoryData<string> Formats = new() { "html", "rtf" };

    private static FlowDocument Round(string format, FlowDocument d) => format == "html"
        ? HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(d))
        : RtfDocumentFormatter.Parse(RtfDocumentFormatter.Write(d));

    private static bool HasInlineImage(FlowDocument d)
        => Paras(d).Any(p => p.Inlines.OfType<InlineImage>().Any());

    [AvaloniaTheory]
    [MemberData(nameof(Formats))]
    public void ALargeInlinePicture_StaysInline(string format)
    {
        var back = Round(format, BigInline());
        Assert.True(HasInlineImage(back));
        Assert.DoesNotContain(back.Blocks, b => b is ImageBlock);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Formats))]
    public void ASmallBlockPicture_StaysABlock(string format)
    {
        var back = Round(format, SmallBlock());
        Assert.Contains(back.Blocks, b => b is ImageBlock);
        Assert.False(HasInlineImage(back));
    }

    // ---- CSS colours: alpha is read -----------------------------------------------------------------

    private static Run FirstRun(string html)
        => HtmlDocumentFormatter.ParseHtml(html).Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).First(r => r.Text == "x");

    [AvaloniaFact]
    public void Css_EightDigitHex_KeepsItsAlpha()
    {
        var c = Assert.IsAssignableFrom<ISolidColorBrush>(FirstRun("<p><span style=\"color:#ff000080\">x</span></p>").Foreground).Color;
        Assert.Equal(Color.FromArgb(0x80, 0xFF, 0, 0), c);
    }

    [AvaloniaFact]
    public void Css_FullyTransparentRgba_IsNoColour()
        => Assert.Null(FirstRun("<p><span style=\"color:rgba(255,0,0,0)\">x</span></p>").Foreground);

    // ---- JSON reader: unknown enum numbers and dotless future versions ------------------------------

    [AvaloniaFact]
    public void Json_AnUndefinedEnumNumber_ReadsAsTheDefault()
    {
        var p = TestHelpers.Para(new Run { Text = "x" });
        p.ListType = ListKind.Bullet;
        var json = DocumentSerializer.Serialize(TestHelpers.Doc(p));
        var bad = Regex.Replace(json, "\"ListType\"\\s*:\\s*\"Bullet\"", "\"ListType\":\"7\"");
        Assert.NotEqual(json, bad);
        var back = DocumentSerializer.Deserialize(bad);
        Assert.Equal(ListKind.None, Paras(back).First().ListType);
    }

    [AvaloniaFact]
    public void Json_ADotlessVersionPastTheLegacyOnes_IsRefused()
    {
        var json = DocumentSerializer.Serialize(TestHelpers.Doc(TestHelpers.Para(new Run { Text = "x" })));
        var future = Regex.Replace(json, "\"Version\"\\s*:\\s*\"[^\"]*\"", "\"Version\":\"3\"");
        Assert.NotEqual(json, future);
        Assert.ThrowsAny<JsonException>(() => DocumentSerializer.DeserializeDocument(future));
        // The legacy integer forms still read.
        DocumentSerializer.DeserializeDocument(Regex.Replace(json, "\"Version\"\\s*:\\s*\"[^\"]*\"", "\"Version\":\"2\""));
    }

    // ---- commands that change nothing leave no undo step --------------------------------------------

    private static RichEditor Editor(Paragraph p)
    {
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        foreach (var name in new[] { "_caretPosition", "_selectionStart", "_selectionEnd" })
            typeof(RichEditor).GetField(name, f)!.SetValue(ed, new TextPointer(p, 0));
        return ed;
    }

    [AvaloniaFact]
    public void SetHeading_BodyOnBody_LeavesNoUndoStep()
    {
        var ed = Editor(TestHelpers.Para(new Run { Text = "x" }));
        ed.SetHeading(0);
        Assert.False(ed.CanUndo);
    }

    [AvaloniaFact]
    public void SetListStyle_TheSameStyleAgain_LeavesNoSecondUndoStep()
    {
        var ed = Editor(TestHelpers.Para(new Run { Text = "x" }));
        ed.SetListStyle(ListMarkerStyle.Square);
        Assert.True(ed.CanUndo);
        ed.SetListStyle(ListMarkerStyle.Square);
        ed.Undo();
        Assert.False(ed.CanUndo);
        Assert.Equal(ListKind.None, Paras(ed.Document!).First().ListType);
    }
}
