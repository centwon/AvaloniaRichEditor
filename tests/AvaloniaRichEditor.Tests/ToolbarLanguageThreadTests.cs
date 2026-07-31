using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorLocalization.LanguageChanged is a static event raised on whatever thread made the change, and
// an attached toolbar rebuilds itself in the handler — creating Avalonia controls, which are thread-
// affine. A host switching language from a worker thread therefore crashed on "the calling thread cannot
// access this object". Found while building the interaction tests: a left-over attached toolbar made the
// (non-Avalonia, other-thread) LocalizationTests take down five unrelated tests.
public class ToolbarLanguageThreadTests : IDisposable
{
    private readonly List<InteractionHost> _hosts = new();
    private readonly string _language = RichEditorLocalization.Language;

    public void Dispose()
    {
        RichEditorLocalization.Language = _language; // process-global state
        foreach (var h in _hosts) h.Dispose();
    }

    private RichEditorToolbar AttachedToolbar()
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>abc</p>");
        var (host, toolbar) = InteractionHost.CreateWithToolbar(ed);
        _hosts.Add(host);
        return toolbar;
    }

    private static string Tooltips(RichEditorToolbar toolbar)
        => string.Join("|", toolbar.GetLogicalDescendants().OfType<Button>()
            .Select(b => ToolTip.GetTip(b)?.ToString() ?? ""));

    [AvaloniaFact]
    public void ChangingTheLanguageOffTheUiThread_DoesNotThrow()
    {
        var toolbar = AttachedToolbar();
        RichEditorLocalization.Language = "en";
        Dispatcher.UIThread.RunJobs();

        // Set it from a worker, the way an app applying a saved setting during startup work would.
        Task.Run(() => RichEditorLocalization.Language = "ko").GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs(); // the rebuild is posted back here

        Assert.Equal("ko", RichEditorLocalization.Language);
        Assert.Contains("굵게", Tooltips(toolbar)); // and it really did rebuild in the new language
    }

    // ...and the strip is still a working toolbar afterwards: the rebuild that ran on the UI thread has to
    // leave buttons that drive the editor, not a half-built strip.
    [AvaloniaFact]
    public void TheToolbarStillWorksAfterAnOffThreadLanguageChange()
    {
        var toolbar = AttachedToolbar();
        var editor = toolbar.Target!;

        Task.Run(() => RichEditorLocalization.Language = "en").GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        var bold = (Button)typeof(RichEditorToolbar)
            .GetField("_boldBtn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(toolbar)!;
        bold.Command?.Execute(null);
        editor.FocusDocumentEnd();
        editor.ToggleBold(); // the command path the rebuilt button is wired to

        Assert.False(bold.Focusable); // the rebuild kept the focus guarantee too
        Assert.Contains("Bold", Tooltips(toolbar));
    }

    // On the UI thread it stays synchronous: no queueing, no waiting for a frame.
    [AvaloniaFact]
    public void ChangingTheLanguageOnTheUiThread_RebuildsImmediately()
    {
        var toolbar = AttachedToolbar();

        RichEditorLocalization.Language = "en";

        Assert.Contains("Bold", Tooltips(toolbar));
    }
}
