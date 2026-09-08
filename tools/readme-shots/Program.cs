using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

namespace ReadmeShots;

// Frames for the README's animation, rendered rather than screen-recorded.
//
// The editor runs in a headless window with REAL Skia (the pixel render tests' setup), and every step
// below goes in as actual input — mouse presses and drags on the control, text through the input method
// path — so a frame is the control's own rendering of state a user could have produced. Nothing is
// simulated at the model level to make a picture look better than the product behaves.
//
// What this cannot show, and deliberately does not fake: a mouse cursor, and the window's title bar.
// A recording made by a person has both; this has neither, and drawing them would be inventing pixels
// for something that did not happen.
internal static class Program
{
    private const int Width = 980;
    private const int Height = 620;
    private const int Fps = 10;

    private static Window _window = null!;
    private static RichEditorView _view = null!;
    private static string _outDir = "";
    private static int _frame;

    private static void Main(string[] args)
    {
        _outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "readme-shots");
        Directory.CreateDirectory(_outDir);
        foreach (var old in Directory.GetFiles(_outDir, "f*.png")) File.Delete(old);

        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .AfterSetup(b => ((Application)b.Instance!).Styles.Add(new FluentTheme()))
            .SetupWithoutStarting();

        // The demo app IS this control, so the frames show the same thing a host gets out of the box.
        AvaloniaRichEditor.RichEditorLocalization.Language = "en";
        _view = new RichEditorView();
        _window = new Window { Width = Width, Height = Height, Content = _view };
        _window.Show();
        Pump();

        // The bundled font, so the toolbar's family box says the same thing the text is drawn with —
        // otherwise the frames show the machine's Korean UI font name under English text.
        _view.Editor.DefaultFontFamily = new FontFamily("Inter");
        _view.Editor.Document = StartingDocument();
        _view.Editor.Focus();
        Pump();

        Scene1_Type();
        Scene2_Format();
        Scene3_ResizeColumn();
        Scene4_TypeInCell();
        Scene5_PageView();

        Console.WriteLine($"{_frame} frames -> {_outDir}");
    }

    // ---- the story ---------------------------------------------------------

    // Typing, one small chunk per frame: the plainest possible statement that this is an editor and not
    // a renderer of documents someone else produced.
    private static void Scene1_Type()
    {
        // Click into the empty paragraph between the title and the table. Its position comes from the
        // table's own laid-out top rather than from a guess: a keyboard walk (Ctrl+Home, Down) landed
        // inside the heading instead, and typing went into the title.
        var start = ParagraphAboveTable();
        _window.MouseDown(start, MouseButton.Left);
        _window.MouseUp(start, MouseButton.Left);
        Hold(4);
        foreach (var chunk in Chunks("A rich text editor, written from scratch on Avalonia's TextLayout.", 2))
        {
            _window.KeyTextInput(chunk);
            Frame();
        }
        Hold(6);
    }

    // Select the last words and make them bold + coloured, through the same commands the toolbar calls.
    private static void Scene2_Format()
    {
        for (int i = 0; i < 25; i++) _window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, "");
        Frame(2);
        _view.Editor.ToggleBold();
        Frame(3);
        _view.Editor.SetForeground(new SolidColorBrush(Color.FromRgb(0x1E, 0x5B, 0xC6)));
        Hold(8);
    }

    // A real drag on the column handle: press, move, release, exactly where the renderer put the
    // affordance. This is the part a still picture cannot make: the table is editable geometry.
    private static void Scene3_ResizeColumn()
    {
        // The handle sits on the column edge; the table starts below the two paragraphs.
        var (x, y) = ColumnHandlePoint();
        _window.MouseDown(new Point(x, y), MouseButton.Left);
        Frame(2);
        for (int i = 1; i <= 9; i++)
        {
            _window.MouseMove(new Point(x + i * 7, y), RawInputModifiers.LeftMouseButton);
            Frame();
        }
        _window.MouseUp(new Point(x + 63, y), MouseButton.Left);
        Hold(8);
    }

    // Click into a cell and type: cells are block containers, and the row grows to fit.
    private static void Scene4_TypeInCell()
    {
        var p = CellPoint();
        _window.MouseDown(p, MouseButton.Left);
        _window.MouseUp(p, MouseButton.Left);
        Frame(3);
        foreach (var chunk in Chunks("header, footer, page numbers", 2))
        {
            _window.KeyTextInput(chunk);
            Frame();
        }
        Hold(8);
    }

    // Switch to paper: the same document on an A4 page with margins, header and page numbers.
    private static void Scene5_PageView()
    {
        _view.Editor.PageSize = RichEditorPageSize.A4;
        Frame(3);
        Hold(14);
    }

    // ---- the document ------------------------------------------------------

    private static FlowDocument StartingDocument()
    {
        var doc = new FlowDocument();

        var title = new Paragraph { HeadingLevel = 1 };
        title.Inlines.Add(new Run { Text = "AvaloniaRichEditor" });
        doc.Blocks.Add(title);

        doc.Blocks.Add(new Paragraph());

        var table = new TableBlock(4, 3);
        table.ColumnWidths[0] = 150;
        table.ColumnWidths[1] = 150;
        table.ColumnWidths[2] = 150;
        string[,] cells =
        {
            { "Feature", "Where", "Notes" },
            { "Tables", "Blocks and cells", "merge, nest" },
            { "Images", "Block or inline", "resize" },
            { "Page view", "A4, Letter", "" },   // left blank: the typing scene fills it
        };
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 3; c++)
            {
                var cellPara = table.Cells[r][c].Para;
                ((Run)cellPara.Inlines[0]).Text = cells[r, c];
                if (r == 0)
                {
                    ((Run)cellPara.Inlines[0]).FontWeight = FontWeight.Bold;
                    table.Cells[r][c].Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFA));
                }
            }
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph());
        return doc;
    }

    // ---- geometry ----------------------------------------------------------

    // Window coordinates of the first inner column edge, and of a cell to click into. Both are derived
    // from where the renderer actually recorded the affordances, not guessed: the handles list is the
    // same one OnPointerPressed hit-tests against.
    private static (double x, double y) ColumnHandlePoint()
    {
        var handles = ColumnHandles();
        if (handles.Count == 0) throw new InvalidOperationException("no column handles were recorded");
        var r = handles[0];
        var inView = _view.Editor.TranslatePoint(new Point(r.Center.X, r.Y + r.Height / 2), _window)
                     ?? new Point(r.Center.X, r.Center.Y);
        return (inView.X, inView.Y);
    }

    // Just above the table's top edge: the empty paragraph the seed puts between the title and the table.
    private static Point ParagraphAboveTable()
    {
        var r = ColumnHandles()[0];
        var p = new Point(60, r.Y - 12);
        return _view.Editor.TranslatePoint(p, _window) ?? p;
    }

    // The empty cell in the last row's last column — the seed leaves it blank precisely so the typing
    // scene starts from nothing instead of splicing into a word.
    private static Point CellPoint()
    {
        var handles = ColumnHandles();
        var last = handles[^1];                       // the right-most inner column edge
        var p = new Point(last.Center.X + 40, last.Y + last.Height * 0.86);
        return _view.Editor.TranslatePoint(p, _window) ?? p;
    }

    private static List<Rect> ColumnHandles()
    {
        var f = typeof(RichEditor).GetField("_columnBoundaries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (System.Collections.IEnumerable)f.GetValue(_view.Editor)!;
        var result = new List<Rect>();
        foreach (var item in list)
        {
            var rect = item!.GetType().GetField("Item1")!.GetValue(item)!;
            result.Add((Rect)rect);
        }
        return result;
    }

    // ---- frames ------------------------------------------------------------

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Frame(int repeat = 1)
    {
        Pump();
        var bmp = _window.CaptureRenderedFrame();
        if (bmp == null) throw new InvalidOperationException("the headless window rendered no frame");
        string first = Path.Combine(_outDir, $"f{_frame:D4}.png");
        bmp.Save(first, PngBitmapEncoderOptions.Default);
        _frame++;
        // A held frame is written out again rather than encoded as a longer delay: the assembler treats
        // every file as one tick, which keeps the timing readable in one place (Fps).
        for (int i = 1; i < repeat; i++)
        {
            File.Copy(first, Path.Combine(_outDir, $"f{_frame:D4}.png"));
            _frame++;
        }
    }

    // A pause on the current state, so the eye can land on what just changed.
    private static void Hold(int frames) => Frame(frames);

    private static IEnumerable<string> Chunks(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
