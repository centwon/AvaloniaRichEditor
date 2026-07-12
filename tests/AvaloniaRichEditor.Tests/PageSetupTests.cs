using System.IO;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// PageSetup document persistence (ported from WinUIRichEditor). A non-default page setup round-trips
// through JSON and .flow; a default (or absent) setup is omitted so plain documents keep their format.
public class PageSetupTests
{
    private static FlowDocument SampleDoc()
    {
        var doc = new FlowDocument();
        var p = new Paragraph();
        p.Inlines.Add(new Run { Text = "Hello" });
        doc.Blocks.Add(p);
        return doc;
    }

    private static PageSetup NonDefaultSetup() => new()
    {
        PageSize = RichEditorPageSize.Letter,
        Orientation = RichEditorPageOrientation.Landscape,
        ShowPageBoundaries = false,
        Header = "My Header",
        Footer = "My Footer",
        ShowPageNumbers = true,
    };

    private static void AssertEqual(PageSetup expected, PageSetup? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.PageSize, actual!.PageSize);
        Assert.Equal(expected.Orientation, actual.Orientation);
        Assert.Equal(expected.ShowPageBoundaries, actual.ShowPageBoundaries);
        Assert.Equal(expected.Header, actual.Header);
        Assert.Equal(expected.Footer, actual.Footer);
        Assert.Equal(expected.ShowPageNumbers, actual.ShowPageNumbers);
    }

    [Fact]
    public void PageSetup_RoundTripsThroughJson()
    {
        var doc = SampleDoc();
        doc.PageSetup = NonDefaultSetup();

        var doc2 = DocumentSerializer.Deserialize(DocumentSerializer.Serialize(doc));

        AssertEqual(NonDefaultSetup(), doc2.PageSetup);
    }

    [Fact]
    public void PageSetup_RoundTripsThroughFlowPackage()
    {
        var doc = SampleDoc();
        doc.PageSetup = NonDefaultSetup();

        using var ms = new MemoryStream();
        DocumentPackage.Save(doc, ms);
        ms.Position = 0;
        var doc2 = DocumentPackage.Load(ms);

        AssertEqual(NonDefaultSetup(), doc2.PageSetup);
    }

    [Fact]
    public void PlainDocument_OmitsPageSetup()
    {
        // No PageSetup at all: unchanged format, no "PageSetup" key in the JSON.
        var json = DocumentSerializer.Serialize(SampleDoc());
        Assert.DoesNotContain("PageSetup", json);
        Assert.Null(DocumentSerializer.Deserialize(json).PageSetup);

        // A default setup (A4 portrait, boundaries on, no chrome) is likewise omitted.
        var doc = SampleDoc();
        doc.PageSetup = new PageSetup(); // all defaults
        Assert.True(doc.PageSetup.IsDefault);
        var json2 = DocumentSerializer.Serialize(doc);
        Assert.DoesNotContain("PageSetup", json2);
    }
}
