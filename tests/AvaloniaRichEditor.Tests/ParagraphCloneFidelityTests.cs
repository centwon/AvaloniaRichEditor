using System;
using System.Linq;
using System.Reflection;
using Avalonia.Media;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

/// <summary>Clone must carry every paragraph-level property, and this checks it by REFLECTION rather
/// than against a list someone maintains.
/// <para>Why the reflection: the defect this guards is a forgotten field, so any guard written as its
/// own list of fields shares the defect — it is forgotten in exactly the same edit. Enumerating the
/// type's properties means the guard grows when the model does.</para>
/// <para>Why it matters: an undo state IS a clone. A property Clone drops behaves perfectly while the
/// user edits and then vanishes at the first Ctrl+Z, which is the worst moment to discover it. Nothing
/// in this suite covered that — the fuzz calls Undo but only checks structural invariants, and the
/// round-trip suites go through the formatters, which never touch Clone. Verified by removing one field
/// (Background) from the shared list: before this file, 764 tests stayed green.</para>
/// <para>Backported from the WinUI peer's undo audit, where Clone was found keeping a second
/// hand-written copy of <see cref="Paragraph.CopyFormatFrom"/>'s field list — the same code, character
/// for character, in both repos.</para>
/// </summary>
public class ParagraphCloneFidelityTests
{
    // The paragraph-level format surface: everything declared on Paragraph or Block that a caller can
    // set. Inlines are the CONTENT, cloned separately (and deeply), so they are not part of this list;
    // Parent is deliberately not copied — the document walk rewires it.
    private static PropertyInfo[] FormatProperties() =>
        typeof(Paragraph).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType == typeof(Paragraph) || p.DeclaringType == typeof(Block))
            .Where(p => p.GetMethod is { IsPublic: true } && p.SetMethod is { IsPublic: true })
            .Where(p => p.Name != nameof(Paragraph.Inlines))
            .OrderBy(p => p.Name)
            .ToArray();

    // A value that differs from the property's default, so a Clone that skips the field is caught by the
    // comparison rather than passing on a coincidence.
    private static object NonDefault(PropertyInfo p)
    {
        if (p.PropertyType == typeof(double)) return 17.5;
        if (p.PropertyType == typeof(int)) return 4;
        if (p.PropertyType == typeof(bool)) return true;
        if (p.PropertyType == typeof(string)) return "x";
        if (p.PropertyType == typeof(IBrush)) return Brushes.Fuchsia;
        if (typeof(IBrush).IsAssignableFrom(p.PropertyType)) return Brushes.Fuchsia;
        if (p.PropertyType.IsEnum) return Enum.GetValues(p.PropertyType).Cast<object>().Last();

        throw new NotSupportedException(
            $"{p.Name} is a {p.PropertyType.Name}, which this test does not know how to vary. " +
            "Teach NonDefault about the type — do NOT skip the property, or Clone stops being checked for it.");
    }

    [Fact]
    public void CloneKeepsEveryParagraphLevelProperty()
    {
        var props = FormatProperties();
        Assert.True(props.Length >= 13, $"only {props.Length} format properties found — the filter is too narrow");

        var source = new Paragraph();
        foreach (var p in props) p.SetValue(source, NonDefault(p));
        source.Inlines.Add(new Run { Text = "content" });

        // The values must actually differ from the defaults, or a field that Clone drops would still
        // compare equal and this whole test would be vacuous for it.
        var fresh = new Paragraph();
        foreach (var p in props)
            Assert.False(Equals(p.GetValue(source), p.GetValue(fresh)),
                $"{p.Name}'s test value equals its default — NonDefault must vary it for the check to mean anything");

        var clone = (Paragraph)source.Clone();

        foreach (var p in props)
            Assert.True(Equals(p.GetValue(source), p.GetValue(clone)),
                $"Clone dropped {p.Name}: {p.GetValue(source)} became {p.GetValue(clone)}");
    }

    // The same list backs the split/paste paths, so it is checked from that direction too — this is the
    // half that already had a documented history of dropping fields.
    [Fact]
    public void CopyFormatFromKeepsEveryParagraphLevelProperty()
    {
        var props = FormatProperties();
        var source = new Paragraph();
        foreach (var p in props) p.SetValue(source, NonDefault(p));

        var target = new Paragraph();
        target.CopyFormatFrom(source);

        foreach (var p in props)
            Assert.True(Equals(p.GetValue(source), p.GetValue(target)),
                $"CopyFormatFrom dropped {p.Name}: {p.GetValue(source)} became {p.GetValue(target)}");
    }

    // Content is cloned deeply, not shared: editing a clone's run must not reach back into the original.
    // (Clone delegating its format half to CopyFormatFrom must not quietly change this half.)
    [Fact]
    public void CloneCopiesTheInlinesDeeply()
    {
        var source = new Paragraph();
        source.Inlines.Add(new Run { Text = "original", FontSize = 22 });

        var clone = (Paragraph)source.Clone();
        ((Run)clone.Inlines[0]).Text = "changed";

        Assert.Equal("original", ((Run)source.Inlines[0]).Text);
        Assert.Equal(22, ((Run)clone.Inlines[0]).FontSize);
        Assert.Same(clone, ((Run)clone.Inlines[0]).Parent);
    }
}
