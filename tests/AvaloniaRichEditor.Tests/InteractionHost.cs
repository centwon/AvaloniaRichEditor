using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

namespace AvaloniaRichEditor.Tests;

// Real input plumbing for control tests. Round 3 found 24 defects; 11 of them were invisible to the
// 446 tests and only showed up by eye in the demo, and every one was pointer, focus or key-combination
// behaviour — things that only happen once an event travels through a live TopLevel to the control.
//
// Two hard constraints, both measured:
//   * The window must be Show()n. Without it input never reaches the control.
//   * Never call Close(). It tears the headless platform down and every later test in the assembly
//     fails. The window therefore stays open for the rest of the run; Dispose only detaches its content.
//     That matters for the toolbar: while attached it holds a subscription to the static
//     RichEditorLocalization.LanguageChanged, so a left-over one would rebuild itself off the UI thread
//     when a later (non-Avalonia) test changes the language, and crash on thread affinity.
internal sealed class InteractionHost : IDisposable
{
    public Window Window { get; }
    public RichEditor Editor { get; }

    private InteractionHost(Window window, RichEditor editor)
    {
        Window = window;
        Editor = editor;
    }

    // Shows `editor` filling a window and gives it focus, the state the demo is in when the user types.
    public static InteractionHost Create(RichEditor editor, double width = 800, double height = 600)
        => Show(editor, editor, width, height);

    // The editor under a real toolbar, so a click on a toolbar button goes through the same hit-testing
    // and focus path as in the demo.
    public static (InteractionHost host, RichEditorToolbar toolbar) CreateWithToolbar(
        RichEditor editor, double width = 1200, double height = 700)
    {
        var toolbar = new RichEditorToolbar { Target = editor };
        var panel = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(editor);
        return (Show(panel, editor, width, height), toolbar);
    }

    private static InteractionHost Show(Control content, RichEditor editor, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        var host = new InteractionHost(window, editor);
        host.Pump();
        editor.Focus();
        host.Pump();
        return host;
    }

    // Detaches the content from the visual tree, dropping whatever it subscribed to on attach. The
    // window itself has to stay open (see the note above).
    public void Dispose()
    {
        Window.Content = null;
        Pump();
    }

    // Runs pending dispatcher work and one layout pass, the way a frame would.
    public void Pump()
    {
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    // Paints one frame. Resize handles and cell image rects are recorded *while rendering*, so a test
    // that wants to grab one has to paint first — a shown headless window doesn't paint on its own.
    public void Render()
    {
        Pump();
        var w = (int)Math.Max(1, Editor.Bounds.Width);
        var h = (int)Math.Max(1, Editor.Bounds.Height);
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h));
        rtb.Render(Editor);
    }

    // Resize affordances as the renderer placed them, in document space: what the user aims at.
    public IReadOnlyList<(Rect rect, TableBlock tb, int colIndex)> ColumnHandles
        => Field<List<(Rect, TableBlock, int)>>("_columnBoundaries");

    public IReadOnlyList<(Rect rect, TableBlock tb, int rowIndex, double height)> RowHandles
        => Field<List<(Rect, TableBlock, int, double)>>("_rowBoundaries");

    // drawnW/drawnH are the size the image was painted at, which inside a cell is the declared size
    // scaled down to fit — the drag is measured against that, so tests need it too.
    public IReadOnlyList<(Rect rect, ImageBlock img, double drawnW, double drawnH)> ImageHandles
        => Field<List<(Rect, ImageBlock, double, double)>>("_imageHandles");

    public IReadOnlyList<(Rect rect, Paragraph p, InlineImage img)> InlineImageHandles
        => Field<List<(Rect, Paragraph, InlineImage)>>("_inlineHandles");

    // ---- coordinates --------------------------------------------------------
    //
    // Tests speak the editor's document space, the same space GetPositionFromPoint and the hit-testing
    // helpers use. Pointer events arrive in window space and the editor maps them back through
    // MapViewToDoc, so both hops have to be undone here or paginated documents land on the wrong page.
    private Point ToWindow(Point doc)
    {
        var view = Editor.MapDocToView(doc);
        return Editor.TranslatePoint(view, Window) ?? view;
    }

    // ---- pointer ------------------------------------------------------------

    public void Click(Point doc, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Press(doc, button, modifiers);
        Release(doc, button, modifiers);
    }

    public void Press(Point doc, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseDown(ToWindow(doc), button, modifiers);
        Pump();
    }

    // Clicks the middle of some other control in the window (a toolbar button), which lives outside the
    // editor's coordinate space.
    public void ClickControl(Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
                     ?? throw new InvalidOperationException($"{control} is not in the visual tree");
        Window.MouseDown(centre, MouseButton.Left);
        Pump();
        Window.MouseUp(centre, MouseButton.Left);
        Pump();
    }

    public void Move(Point doc, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseMove(ToWindow(doc), modifiers);
        Pump();
    }

    public void Release(Point doc, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseUp(ToWindow(doc), button, modifiers);
        Pump();
    }

    // Press, drag through `waypoints`, release at the last one. A real drag sends moves while the
    // button is held; the pressed modifier has to be set or the control sees a hover, not a drag.
    public void Drag(Point from, params Point[] waypoints)
    {
        Press(from);
        foreach (var p in waypoints) Move(p, RawInputModifiers.LeftMouseButton);
        Release(waypoints.Length > 0 ? waypoints[^1] : from);
    }

    // A second click at the same spot within the double-click window. The headless input source counts
    // clicks by time and distance, so the two presses must not be separated by a Move.
    public void DoubleClick(Point doc)
    {
        Click(doc);
        Click(doc);
    }

    // ---- keyboard -----------------------------------------------------------

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, string.Empty);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, string.Empty);
        Pump();
    }

    // Types text the way an OS keyboard layout delivers it: a text-input event, not a key press.
    public void Type(string text)
    {
        Window.KeyTextInput(text);
        Pump();
    }

    // ---- state --------------------------------------------------------------

    public bool EditorHasFocus => Editor.IsFocused;

    // The editor keeps caret and selection private, so read them the way the rest of the suite does.
    private T Field<T>(string name)
        => (T)typeof(RichEditor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Editor)!;

    public TextPointer Caret => Field<TextPointer>("_caretPosition");

    public Block? SelectedBlock => Field<Block?>("_selectedBlock");

    public Block? CaretBlock => Field<Block?>("_caretBlock");

    public TextRange Selection =>
        new TextRange(Field<TextPointer>("_selectionStart"), Field<TextPointer>("_selectionEnd"));

    public string SelectedText => Selection.GetText();

    // Whatever the click landed on, for tests that care where focus went instead.
    public IInputElement? FocusedElement => Window.FocusManager?.GetFocusedElement();
}
