using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>A picture's accessibility description, backported from the WinUI peer.
/// <para>Until this, an image in this editor could not carry one at all: a document arriving with
/// <c>&lt;img alt&gt;</c> lost it on import, and nothing this editor wrote had one. That is the half of
/// image accessibility a document format can actually keep, so it has to survive every format that is
/// meant to be lossless — and the wire name matches the peer's (<c>Alt</c>), because the two write the
/// same <c>.flow</c>.</para>
/// </summary>
public class ImageAltTextTests
{
    // A 1x1 PNG — enough for the formatters to treat this as a real picture.
    private static readonly byte[] TinyPng = System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // Comfortably over the importer's 64px icon threshold: an image smaller than that comes back from
    // HTML as an inline icon rather than a block, which has nothing to do with alt text but would make
    // the round-trip assertions below look like an alt-text failure.
    private static ImageBlock Block(string? alt)
    {
        var ib = new ImageBlock { Width = 400, Height = 300, AltText = alt };
        ib.SetImageData(TinyPng, "image/png", null);
        return ib;
    }

    private static InlineImage Inline(string? alt)
    {
        var im = new InlineImage { Width = 12, Height = 12, AltText = alt };
        im.SetImageData(TinyPng, "image/png", null);
        return im;
    }

    private static FlowDocument DocWithBoth(string blockAlt, string inlineAlt)
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(Block(blockAlt));
        var host = new Paragraph();
        host.Inlines.Add(new Run { Text = "before " });
        host.Inlines.Add(Inline(inlineAlt));
        doc.Blocks.Add(host);
        doc.Blocks.Add(new Paragraph());
        return doc;
    }

    private static (string? block, string? inline) AltsOf(FlowDocument doc) =>
        (doc.Blocks.OfType<ImageBlock>().FirstOrDefault()?.AltText,
         doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>().FirstOrDefault()?.AltText);

    // ---- the model -----------------------------------------------------------------------------

    // An undo state is a clone, so a description the clone drops disappears at the first Ctrl+Z.
    [Fact]
    public void CloneCarriesTheDescription()
    {
        Assert.Equal("a chart", ((ImageBlock)Block("a chart").Clone()).AltText);
        Assert.Equal("an icon", ((InlineImage)Inline("an icon").Clone()).AltText);
    }

    // ---- the lossless formats ------------------------------------------------------------------

    [Fact]
    public void JsonRoundTripsTheDescription()
    {
        var back = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(DocWithBoth("chart", "icon")));

        Assert.Equal(("chart", "icon"), AltsOf(back));
    }

    // .flow is the peer-compatible package, so the field has to travel in it under the same name.
    [Fact]
    public void FlowPackageRoundTripsTheDescription()
    {
        using var ms = new MemoryStream();
        DocumentPackage.Save(DocWithBoth("chart", "icon"), ms);
        ms.Position = 0;

        Assert.Equal(("chart", "icon"), AltsOf(DocumentPackage.Load(ms)));
    }

    // The wire name is part of the contract with the peer: both write `Alt` into the same document.
    [Fact]
    public void TheJsonFieldIsNamedAlt()
    {
        string json = DocumentSerializer.Serialize(DocWithBoth("chart", "icon"));

        Assert.Contains("\"Alt\":\"chart\"", json.Replace(" ", ""));
        Assert.Contains("\"Alt\":\"icon\"", json.Replace(" ", ""));
    }

    // A picture with no description must not gain an empty attribute or field.
    [Fact]
    public void NoDescriptionWritesNothing()
    {
        var doc = new FlowDocument();
        doc.Blocks.Add(new Paragraph());
        doc.Blocks.Add(Block(null));
        doc.Blocks.Add(new Paragraph());

        Assert.DoesNotContain("\"Alt\"", DocumentSerializer.Serialize(doc));
        Assert.DoesNotContain("alt=", HtmlDocumentFormatter.ToHtml(doc));
    }

    // ---- HTML ----------------------------------------------------------------------------------

    [Fact]
    public void HtmlWritesTheDescriptionAsAnAltAttribute()
    {
        string html = HtmlDocumentFormatter.ToHtml(DocWithBoth("a bar chart", "a warning icon"));

        Assert.Contains("alt=\"a bar chart\"", html);
        Assert.Contains("alt=\"a warning icon\"", html);
    }

    // Reading an <img> DECODES it, which needs the Avalonia platform — as a plain [Fact] the
    // decode throws, LoadImage swallows it, and the picture silently never arrives.
    [AvaloniaFact]
    public void HtmlRoundTripsTheDescription()
    {
        var back = HtmlDocumentFormatter.ParseHtml(HtmlDocumentFormatter.ToHtml(DocWithBoth("chart", "icon")));

        Assert.Equal(("chart", "icon"), AltsOf(back));
    }

    // Foreign HTML is where most descriptions come from — the import path has to read them, and has to
    // treat the "decorative" empty alt as no description rather than as an empty one.
    // Reading an <img> DECODES it, which needs the Avalonia platform — as a plain [Fact] the
    // decode throws, LoadImage swallows it, and the picture silently never arrives.
    [AvaloniaFact]
    public void ForeignHtmlWithAnAltAttribute_IsImported()
    {
        string src = "data:image/png;base64," + System.Convert.ToBase64String(TinyPng);
        var doc = HtmlDocumentFormatter.ParseHtml($"<p>text <img src=\"{src}\" width=\"12\" height=\"12\" alt=\"a &amp; b\"/></p>");

        var img = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>().FirstOrDefault();
        Assert.NotNull(img);
        Assert.Equal("a & b", img!.AltText); // entity-decoded, not the raw attribute text
    }

    // Reading an <img> DECODES it, which needs the Avalonia platform — as a plain [Fact] the
    // decode throws, LoadImage swallows it, and the picture silently never arrives.
    [AvaloniaFact]
    public void AnEmptyAltMeansDecorative_NotAnEmptyDescription()
    {
        string src = "data:image/png;base64," + System.Convert.ToBase64String(TinyPng);
        var doc = HtmlDocumentFormatter.ParseHtml($"<p>text <img src=\"{src}\" width=\"12\" height=\"12\" alt=\"\"/></p>");

        var img = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>().FirstOrDefault();
        Assert.NotNull(img);
        Assert.Null(img!.AltText);
    }

    // ---- the way a user reaches it -------------------------------------------------------------

    // The description is only useful if it can be written without an API call, and a menu item with no
    // label is the failure this repo has seen before (a missing localization key).
    // Language is process-global state, so it is restored the way LocalizationTests does it.
    [Fact]
    public void TheMenuLabelIsLocalized_NotAMissingKey()
    {
        var saved = RichEditorLocalization.Language;
        try
        {
            RichEditorLocalization.Language = "en";
            Assert.Equal("Alt Text...", RichEditorLocalization.GetString("AltText"));
            RichEditorLocalization.Language = "ko";
            Assert.Equal("대체 텍스트...", RichEditorLocalization.GetString("AltText"));
        }
        finally { RichEditorLocalization.Language = saved; }
    }
}
