using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The view's Export offers PDF, as the WinUI view does, and a PDF leaves the document modified (it cannot be opened again).
// Import lists only the files it can read, and an empty file is nothing to import (2026-11-09).
public class ViewExportImportTests
{
    private static RichEditorToolbar Toolbar(string html)
    {
        var ed = new RichEditor();
        ed.LoadHtml(html);
        return new RichEditorToolbar { Target = ed };
    }

    [AvaloniaFact]
    public async Task ExportingAPdf_WritesAPdf_AndLeavesTheDocumentModified()
    {
        var tb = Toolbar("<p>hello</p>");
        tb.Target!.InsertText("x");
        Assert.True(tb.Target.IsModified);
        using var ms = new MemoryStream();
        await tb.ExportToAsync("out.pdf", ms);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(ms.ToArray(), 0, 4));
        Assert.True(tb.Target.IsModified);
    }

    [AvaloniaFact]
    public async Task ExportingJson_StillMarksSaved()
    {
        var tb = Toolbar("<p>hello</p>");
        tb.Target!.InsertText("x");
        using var ms = new MemoryStream();
        await tb.ExportToAsync("out.json", ms);
        Assert.False(tb.Target.IsModified);
        Assert.Contains("hello", System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }

    [AvaloniaFact]
    public void Import_ListsOnlyTheFilesItReads()
    {
        var patterns = RichEditorToolbar.ImportFileTypes.SelectMany(t => t.Patterns!).ToArray();
        Assert.Equal(new[] { "*.flow", "*.json", "*.html", "*.htm", "*.rtf" }, patterns);
    }

    [AvaloniaFact]
    public async Task AnEmptyFile_IsNothingToImport()
    {
        var tb = Toolbar("<p>keep me</p>");
        string before = tb.Target!.ToJson();
        int reports = 0;
        void Count(object? _, RichEditorFaultEventArgs __) => reports++;
        RichEditorDiagnostics.Fault += Count;
        try { await tb.ImportStreamAsync(new MemoryStream()); }
        finally { RichEditorDiagnostics.Fault -= Count; }
        Assert.Equal(before, tb.Target.ToJson());
        Assert.Equal(0, reports);
    }
}
