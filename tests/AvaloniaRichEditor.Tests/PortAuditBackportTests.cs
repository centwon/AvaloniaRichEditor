using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// From the WinUI port's audit of three files no test had referenced (2026-09-19, port PR #42): the toolbar's
// Import (a byte-order mark sent every file to the JSON reader) and the "draw table" mode (armed across a
// document swap and a lost capture; a table drawn in a cell as wide as the drag). Measured here before fixing.
public class PortAuditBackportTests
{
    // ---- Import and a byte-order mark ------------------------------------------------------------------

    private const string Korean = "가져오기 한글 문장";

    private static string TextOf(RichEditor ed)
        => string.Concat(ed.Document!.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().Select(r => r.Text));

    private static async System.Threading.Tasks.Task<string> Import(byte[] bytes)
    {
        var ed = new RichEditor();
        ed.LoadHtml("<p>before</p>");
        var toolbar = new RichEditorToolbar { Target = ed };
        // Filled by writing, as Import fills it by CopyToAsync — a MemoryStream wrapped around an array does not
        // expose its buffer, which the sniff reads.
        var ms = new MemoryStream();
        ms.Write(bytes);
        await toolbar.ImportStreamAsync(ms);
        return TextOf(ed);
    }

    private static string Source(string kind)
    {
        var ed = new RichEditor();
        ed.LoadHtml($"<p>{Korean}</p>");
        return kind switch { "html" => $"<p>{Korean}</p>", "json" => ed.ToJson(), _ => ed.ToRtf() };
    }

    [AvaloniaTheory]
    [InlineData("html")]
    [InlineData("json")]
    [InlineData("rtf")]
    public async System.Threading.Tasks.Task AFileWithoutABom_Imports(string kind)
        => Assert.Contains(Korean, await Import(Encoding.UTF8.GetBytes(Source(kind))));

    // Windows tools write a mark (Notepad before 1903, Visual Studio, PowerShell 5's -Encoding utf8, "Unicode"
    // = UTF-16). It is not whitespace to TrimStart, so the sniff saw neither "<" nor "{\rtf".
    [AvaloniaTheory]
    [InlineData("html", "utf-8")]
    [InlineData("json", "utf-8")]
    [InlineData("rtf", "utf-8")]
    [InlineData("html", "utf-16")]
    [InlineData("json", "utf-16")]
    public async System.Threading.Tasks.Task AFileWithAByteOrderMark_Imports(string kind, string encoding)
    {
        var enc = encoding == "utf-16" ? Encoding.Unicode : Encoding.UTF8;
        var bytes = enc.GetPreamble().Concat(enc.GetBytes(Source(kind))).ToArray();
        Assert.Contains(Korean, await Import(bytes));
    }

    // ---- "draw table" mode -----------------------------------------------------------------------------

    private static Paragraph Para(string text) => new() { Inlines = { new Run { Text = text } } };

    private static InteractionHost Host(params Block[] blocks)
    {
        var doc = new FlowDocument();
        foreach (var b in blocks) doc.Blocks.Add(b);
        var host = InteractionHost.Create(new RichEditor { Document = doc, PageSize = RichEditorPageSize.Continuous });
        host.Render();
        return host;
    }

    private static int Tables(RichEditor ed) => ed.Document!.Blocks.OfType<TableBlock>().Count();
    private static bool Armed(RichEditor ed)
        => typeof(RichEditor).GetField("_pendingTableDraw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ed) != null;

    private static readonly Point InText = new(40, 12);

    // Control: a drag inserts a table sized to it.
    [AvaloniaFact]
    public void ADrag_InsertsATable()
    {
        var host = Host(Para("before"), Para("a"), Para("b"), Para("c"), Para("d"), Para("after"));
        host.Editor.BeginTableDraw(2, 3);
        host.Drag(InText, InText + new Point(300, 80));
        Assert.Equal(1, Tables(host.Editor));
        Assert.False(Armed(host.Editor));
    }

    // Armed, then a document opened: the first click in it inserted a table into a file just opened.
    [AvaloniaFact]
    public void OpeningADocument_DisarmsTheMode()
    {
        var host = Host(Para("old"));
        host.Editor.BeginTableDraw(2, 2);
        host.Editor.LoadHtml("<p>new file</p><p>second</p>");
        host.Render();
        Assert.False(Armed(host.Editor), "still armed after a document was opened");
        host.Click(InText);
        Assert.Equal(0, Tables(host.Editor));
        Assert.False(host.Editor.IsModified);
    }

    [AvaloniaFact]
    public void Undo_DisarmsTheMode()
    {
        var host = Host(Para("text"));
        host.Click(InText);
        host.Type("x");
        host.Editor.BeginTableDraw(2, 2);
        host.Editor.Undo();
        Assert.False(Armed(host.Editor), "still armed after an undo");
    }

    // A lost capture is not a release: the drag is abandoned, not completed with the rectangle it had reached.
    [AvaloniaFact]
    public void LosingThePointerMidDrag_AbandonsIt()
    {
        var host = Host(Para("before"), Para("a"), Para("b"), Para("after"));
        IPointer? pointer = null;
        host.Editor.AddHandler(InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) => pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.Editor.BeginTableDraw(2, 2);
        host.Press(InText);
        host.Move(InText + new Point(200, 60), RawInputModifiers.LeftMouseButton);
        pointer!.Capture(null);
        host.Pump();
        Assert.False(Armed(host.Editor), "still armed after the capture was lost");
        host.Release(InText + new Point(200, 60));
        Assert.Equal(0, Tables(host.Editor));
    }

    // Drawn from inside a table cell the new table nests there, and the drag was only clamped to the editor —
    // a nested table as wide as the drag, over its neighbours.
    [AvaloniaFact]
    public void DrawnInACell_TheTableFitsTheCell()
    {
        var outer = new TableBlock(1, 2);
        outer.ColumnWidths[0] = 200; outer.ColumnWidths[1] = 200;
        var host = Host(Para("top"), outer, Para("end"));
        var boundary = host.ColumnHandles.First(h => h.colIndex == 0).rect;
        var inCell = new Point(boundary.Center.X - 150, boundary.Center.Y);

        host.Editor.BeginTableDraw(1, 2);
        host.Drag(inCell, inCell + new Point(500, 40));

        var cell = host.Editor.Document!.Blocks.OfType<TableBlock>().Single().Cells[0][0];
        var nested = cell.Blocks.OfType<TableBlock>().SingleOrDefault();
        Assert.NotNull(nested);
        Assert.True(nested!.ColumnWidths.Sum() <= 200, $"a table drawn in a 200px cell is {nested.ColumnWidths.Sum()}px wide");
    }

    // ---- Ctrl+K ----------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void CtrlK_IsInTheShortcutTable()
    {
        Assert.True(RichEditorShortcuts.TryMatch(true, false, false, Key.K, out var id));
        Assert.Equal(RichEditorShortcutId.InsertLink, id);
        Assert.Equal("Ctrl+K", RichEditorShortcuts.Display(RichEditorShortcutId.InsertLink));
    }

    // End to end: the key opens the link dialog on the link the caret is in, and OK re-links that word.
    [AvaloniaFact]
    public void CtrlK_OpensTheLinkDialog_OnTheCaretsLink()
    {
        var p = new Paragraph { Inlines = { new Run { Text = "see " }, new Run { Text = "here", NavigateUri = "https://old.example/" }, new Run { Text = " now" } } };
        var host = Host(p);
        var para = host.Editor.Document!.Blocks.OfType<Paragraph>().Single();
        typeof(RichEditor).GetField("_caretPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(host.Editor, new TextPointer(para, 6)); // inside "here"
        Assert.Equal("https://old.example/", host.Editor.CaretLinkUri());

        Avalonia.Controls.Window? dialog = null;
        using var watch = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Avalonia.Controls.Window>((w, _) =>
        {
            if (!ReferenceEquals(w, Avalonia.Controls.TopLevel.GetTopLevel(host.Editor))) dialog = w;
        });
        host.Key(Key.K, RawInputModifiers.Control);
        host.Pump();

        Assert.NotNull(dialog);
        var panel = (Avalonia.Controls.StackPanel)dialog!.Content!;
        var box = (Avalonia.Controls.TextBox)panel.Children[0];
        Assert.Equal("https://old.example/", box.Text);
        box.Text = "https://new.example/";
        var ok = (Avalonia.Controls.Button)((Avalonia.Controls.StackPanel)panel.Children[1]).Children[0];
        ok.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        host.Pump();

        var linked = host.Editor.Document!.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>()
            .Where(r => r.NavigateUri == "https://new.example/").Select(r => r.Text);
        Assert.Equal("here", string.Concat(linked));
    }
}
