using System.Collections.Generic;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// RichEditorLocalization: built-in ko/en tables, per-key English fallback, and third-party
// language registration (merge semantics). Language is process-global state, so every test
// restores it in a finally block.
public class LocalizationTests
{
    [Fact]
    public void GetString_UsesActiveLanguage()
    {
        var saved = RichEditorLocalization.Language;
        try
        {
            RichEditorLocalization.Language = "ko";
            Assert.Equal("복사", RichEditorLocalization.GetString("Copy"));
            RichEditorLocalization.Language = "en";
            Assert.Equal("Copy", RichEditorLocalization.GetString("Copy"));
        }
        finally { RichEditorLocalization.Language = saved; }
    }

    [Fact]
    public void UnregisteredLanguage_FallsBackToEnglish()
    {
        var saved = RichEditorLocalization.Language;
        try
        {
            RichEditorLocalization.Language = "fr"; // not registered
            Assert.Equal("Copy", RichEditorLocalization.GetString("Copy"));
        }
        finally { RichEditorLocalization.Language = saved; }
    }

    [Fact]
    public void UnknownKey_ReturnsKeyItself()
    {
        Assert.Equal("NoSuchKey", RichEditorLocalization.GetString("NoSuchKey"));
    }

    [Fact]
    public void Register_AddsLanguage_WithPerKeyEnglishFallback()
    {
        var saved = RichEditorLocalization.Language;
        try
        {
            RichEditorLocalization.Register("xx", new Dictionary<string, string> { ["Copy"] = "Kopi" });
            RichEditorLocalization.Language = "xx";
            Assert.Equal("Kopi", RichEditorLocalization.GetString("Copy"));
            Assert.Equal("Paste", RichEditorLocalization.GetString("Paste")); // missing key -> English
        }
        finally { RichEditorLocalization.Language = saved; }
    }

    [Fact]
    public void Register_MergesIntoExistingLanguage()
    {
        var saved = RichEditorLocalization.Language;
        try
        {
            RichEditorLocalization.Register("xy", new Dictionary<string, string> { ["Copy"] = "c1" });
            RichEditorLocalization.Register("xy", new Dictionary<string, string> { ["Paste"] = "p1" });
            RichEditorLocalization.Language = "xy";
            Assert.Equal("c1", RichEditorLocalization.GetString("Copy"));
            Assert.Equal("p1", RichEditorLocalization.GetString("Paste"));
        }
        finally { RichEditorLocalization.Language = saved; }
    }

    [Fact]
    public void LanguageChange_RaisesEvent()
    {
        var saved = RichEditorLocalization.Language;
        try
        {
            int raised = 0;
            void Handler(object? s, System.EventArgs e) => raised++;
            RichEditorLocalization.LanguageChanged += Handler;
            try
            {
                RichEditorLocalization.Language = saved == "ko" ? "en" : "ko";
                Assert.Equal(1, raised);
                RichEditorLocalization.Language = RichEditorLocalization.Language; // no-op: same value
                Assert.Equal(1, raised);
            }
            finally { RichEditorLocalization.LanguageChanged -= Handler; }
        }
        finally { RichEditorLocalization.Language = saved; }
    }
}
