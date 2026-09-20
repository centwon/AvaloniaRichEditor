using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// The shortcut table is public since 1.3.0 (backlog item): a host building its own toolbar or menu could
// not read the gestures the editor acts on, so it had to write "Ctrl+B" again somewhere the editor cannot
// see — and the two drift the day a binding changes.
//
// What is worth holding here is what a host relies on and the editor could break silently: the table hands
// out no way to edit itself, every command in it has a hint, the hint agrees with the modifiers and key of
// the entry it came from, Gesture agrees with the same entry, and pressing what the table advertises does
// what it says.
public class PublicShortcutTableTests
{
    private static void Press(RichEditor ed, Key key, KeyModifiers mods)
        => ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });

    private static RichEditorShortcut Primary(RichEditorShortcutId id)
        => RichEditorShortcuts.All.First(s => s.Id == id);

    private static KeyModifiers Modifiers(RichEditorShortcut s)
    {
        var m = KeyModifiers.None;
        if (s.Ctrl) m |= KeyModifiers.Control;
        if (s.Shift) m |= KeyModifiers.Shift;
        if (s.Alt) m |= KeyModifiers.Alt;
        return m;
    }

    // Spelled out here rather than read from the product, so this is an independent oracle for Display.
    private static string KeyName(Key k) => k switch
    {
        Key.OemPeriod => ".",
        Key.OemComma => ",",
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        _ => k.ToString(),
    };

    [Fact]
    public void TheTableHandsOutNoWayToEditIt()
    {
        var all = RichEditorShortcuts.All;

        // A host that could write here would be editing what the editor's own key handler matches against.
        Assert.IsNotType<RichEditorShortcut[]>(all);
        if (all is IList<RichEditorShortcut> writable)
        {
            Assert.True(writable.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => writable[0] = default);
        }
    }

    [Fact]
    public void EveryCommandHasAHint()
    {
        foreach (RichEditorShortcutId id in Enum.GetValues<RichEditorShortcutId>())
            Assert.False(string.IsNullOrEmpty(RichEditorShortcuts.Display(id)), $"{id} has no hint");
    }

    // The hint is what a host prints next to its own button; the modifiers and key are what the editor
    // acts on. A hint that says something else is the exact drift this table exists to prevent.
    [Fact]
    public void EveryHintSpellsOutItsOwnModifiersAndKey()
    {
        foreach (var s in RichEditorShortcuts.All)
        {
            string expected = (s.Ctrl ? "Ctrl+" : "") + (s.Shift ? "Shift+" : "") + (s.Alt ? "Alt+" : "") + KeyName(s.Key);
            // An alias entry keeps the primary command's hint (Ctrl+Shift+Z shows "Ctrl+Y"), so it is the
            // primary entry of each command that has to spell itself out.
            if (ReferenceEquals(Primary(s.Id).Display, s.Display) && Primary(s.Id).Key == s.Key)
                Assert.Equal(expected, s.Display);
        }
    }

    [Fact]
    public void GestureMatchesThePrimaryEntry()
    {
        foreach (RichEditorShortcutId id in Enum.GetValues<RichEditorShortcutId>())
        {
            var s = Primary(id);
            var g = RichEditorShortcuts.Gesture(id);
            Assert.NotNull(g);
            Assert.Equal(s.Key, g!.Key);
            Assert.Equal(Modifiers(s), g.KeyModifiers);
        }
    }

    // The tie to behaviour: press exactly what the table advertises and the command runs. Without this the
    // rest only proves the table is self-consistent — it could be self-consistent and wrong.
    [AvaloniaTheory]
    [InlineData(RichEditorShortcutId.Bold)]
    [InlineData(RichEditorShortcutId.Italic)]
    [InlineData(RichEditorShortcutId.AlignCenter)]
    [InlineData(RichEditorShortcutId.Heading1)]
    public void PressingWhatTheTableAdvertises_RunsTheCommand(RichEditorShortcutId id)
    {
        var p = TestHelpers.Para(new Run { Text = "text" });
        var ed = new RichEditor { Document = TestHelpers.Doc(p) };
        ed.FocusDocumentEnd();
        Press(ed, Key.A, KeyModifiers.Control); // select it all

        var s = Primary(id);
        Press(ed, s.Key, Modifiers(s));

        switch (id)
        {
            case RichEditorShortcutId.Bold:
                Assert.Equal(FontWeight.Bold, p.Inlines.OfType<Run>().First().FontWeight);
                break;
            case RichEditorShortcutId.Italic:
                Assert.Equal(FontStyle.Italic, p.Inlines.OfType<Run>().First().FontStyle);
                break;
            case RichEditorShortcutId.AlignCenter:
                Assert.Equal(TextAlignment.Center, p.TextAlignment);
                break;
            case RichEditorShortcutId.Heading1:
                Assert.Equal(1, p.HeadingLevel);
                break;
        }
    }
}
