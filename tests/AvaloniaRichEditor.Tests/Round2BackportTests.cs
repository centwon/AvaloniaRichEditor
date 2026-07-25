using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using AvaloniaRichEditor.Formatters;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Round-2 backport of WinUIRichEditor-ahead, platform-agnostic features:
// IsModified/MarkSaved, RemoveList, AutoLinkOnType, AllowRemoteImagesOnPaste, and the find highlight-all
// query (SetFindHighlight/GetFindMatchPosition/ClearFindHighlight).
public class Round2BackportTests
{
    private static string? FirstNavigateUri(RichEditor ed) =>
        ed.Document!.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines).OfType<Run>()
            .FirstOrDefault(r => !string.IsNullOrEmpty(r.NavigateUri))?.NavigateUri;

    // ---- IsModified / MarkSaved -------------------------------------------
    [AvaloniaFact]
    public void FreshLoad_IsNotModified_ThenEditsSetIt_ThenMarkSavedClears()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        Assert.False(ed.IsModified); // a freshly loaded document is the clean baseline

        ed.FocusDocumentEnd();
        ed.InsertText("X");
        Assert.True(ed.IsModified);

        bool fired = false;
        ed.IsModifiedChanged += (_, _) => fired = true;
        ed.MarkSaved();
        Assert.False(ed.IsModified);
        Assert.True(fired);
    }

    // ---- RemoveList --------------------------------------------------------
    [AvaloniaFact]
    public void RemoveList_ClearsTheListAttribute()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<ul><li>a</li></ul>");
        ed.FocusDocumentEnd();
        Assert.Equal(ListKind.Bullet, ed.GetCaretFormat().List);

        ed.RemoveList();
        Assert.Equal(ListKind.None, ed.GetCaretFormat().List);
    }

    // ---- AutoLinkOnType ----------------------------------------------------
    [AvaloniaFact]
    public void TypingSpaceAfterUrl_AutoLinks()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.InsertText(" ");
        ed.InsertText("http://example.com");
        ed.InsertText(" ");
        Assert.Equal("http://example.com", FirstNavigateUri(ed));
    }

    [AvaloniaFact]
    public void TypingSpaceAfterWww_AutoLinksWithHttpsPrefix()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.InsertText(" ");
        ed.InsertText("www.example.com");
        ed.InsertText(" ");
        Assert.Equal("https://www.example.com", FirstNavigateUri(ed));
    }

    [AvaloniaFact]
    public void AutoLinkOnTypeFalse_DoesNotLink()
    {
        var ed = new RichEditor { AutoLinkOnType = false };
        ed.LoadHtml("<p>abc</p>");
        ed.FocusDocumentEnd();
        ed.InsertText(" ");
        ed.InsertText("http://example.com");
        ed.InsertText(" ");
        Assert.Null(FirstNavigateUri(ed));
    }

    // ---- AllowRemoteImagesOnPaste (formatter opt-out; no network) ----------
    [AvaloniaFact]
    public void ParseHtml_WithRemoteImagesBlocked_SkipsRemoteImage()
    {
        const string html = "<p><img src=\"http://example.com/x.png\"></p>";
        var doc = HtmlDocumentFormatter.ParseHtml(html, allowLocalFileImages: true, allowRemoteImages: false);
        int images = doc.Blocks.OfType<ImageBlock>().Count()
            + doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<InlineImage>().Count();
        Assert.Equal(0, images);
    }

    [AvaloniaFact]
    public void AllowRemoteImagesOnPaste_DefaultsTrue()
        => Assert.True(new RichEditor().AllowRemoteImagesOnPaste);

    // ---- find highlight-all ------------------------------------------------
    [AvaloniaFact]
    public void SetFindHighlight_CountsAllMatches_ClearResets()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>hello hello world</p>");
        ed.SetFindHighlight("hello", matchCase: false);
        Assert.Equal(2, ed.GetFindMatchPosition().total);

        ed.ClearFindHighlight();
        Assert.Equal((0, 0), ed.GetFindMatchPosition());
    }

    [AvaloniaFact]
    public void FindNext_SetsHighlight_AndSelectionIsCurrentMatch()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>alpha beta beta</p>");
        Assert.True(ed.FindNext("beta", matchCase: false));
        var (current, total) = ed.GetFindMatchPosition();
        Assert.Equal(2, total);
        Assert.Equal(1, current); // the first match is now selected
    }
}
