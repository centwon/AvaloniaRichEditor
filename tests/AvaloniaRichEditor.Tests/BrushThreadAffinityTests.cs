using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// A brush or pen that is SHARED across the process must not be an AvaloniaObject. Those carry thread
// affinity — whichever thread runs the static initializer owns them — so one shared instance throws
// "the calling thread cannot access this object" the moment a second UI thread paints with it. The
// immutable forms (ImmutableSolidColorBrush / ImmutablePen) have no affinity, which is why the document
// model already uses them (CLAUDE.md rule #8).
//
// Two shapes qualify as shared, and both had real offenders: `static readonly` fields (three in the
// toolbar, one in the editor) and STYLED PROPERTY DEFAULTS, which are a single object handed to every
// instance — SelectionBrush was one, and it surfaced as a 1-in-4 flake across seven interaction tests
// rather than as anything readable. A per-instance brush is fine and is not covered here.
//
// This sweeps the whole library rather than naming the fixed sites, so a new shared brush is caught the
// day it is added instead of the day two UI threads exist.
public class BrushThreadAffinityTests
{
    private static IEnumerable<(string where, object? value)> SharedStaticBrushes()
    {
        foreach (var type in typeof(RichEditor).Assembly.GetTypes())
        {
            foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(IBrush).IsAssignableFrom(f.FieldType) && !typeof(IPen).IsAssignableFrom(f.FieldType))
                    continue;
                yield return ($"{type.Name}.{f.Name}", f.GetValue(null));
            }
        }
    }

    [AvaloniaFact]
    public void NoSharedStaticBrushIsThreadAffine()
    {
        var affine = SharedStaticBrushes()
            .Where(x => x.value is AvaloniaObject)
            .Select(x => x.where)
            .ToList();

        Assert.True(affine.Count == 0,
            "these are shared across the process and carry thread affinity; use the Immutable* form: "
            + string.Join(", ", affine));
    }

    // Guards the premise: if the sweep found nothing at all, it is not proving anything.
    [AvaloniaFact]
    public void TheSweepActuallyFindsBrushes()
        => Assert.True(SharedStaticBrushes().Count(x => x.value != null) >= 8);

    // The editor's brush-valued property DEFAULTS. Read off a fresh instance rather than through the
    // property metadata, so this keeps working across Avalonia's reflection surface changing.
    [AvaloniaFact]
    public void NoBrushPropertyDefaultIsThreadAffine()
    {
        var ed = new RichEditor();

        Assert.IsNotAssignableFrom<AvaloniaObject>(ed.SelectionBrush);
        Assert.IsNotAssignableFrom<AvaloniaObject>(ed.CaretBrush);
    }

    // ...and a brush the HOST sets is still honoured, so the defaults being immutable did not quietly
    // turn these into read-only properties.
    [AvaloniaFact]
    public void AHostSuppliedBrushStillWins()
    {
        var mine = new SolidColorBrush(Colors.Red);
        var ed = new RichEditor { SelectionBrush = mine };

        Assert.Same(mine, ed.SelectionBrush);
    }
}
