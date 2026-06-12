using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaRichEditor;
using AvaloniaRichEditor.Controls;

namespace AvaloniaRichEditor.Demo.Views;

// Print preview + output (P-milestone Phase 3/4): pages rendered by the library's RenderPrintPage
// at screen DPI, stacked on a grey desk, with printer selection / print / save-as-PDF on top.
// Printing uses System.Drawing.Printing — a host-app concern; the library stays dependency-free.
internal class PrintPreviewWindow : Window
{
    private readonly RichEditor _editor;
    private readonly ComboBox _printerCombo;
    private readonly TextBlock _status;

    private static string Loc(string key) => RichEditorLocalization.GetString(key);

    public PrintPreviewWindow(RichEditor editor)
    {
        _editor = editor;
        int pages = editor.GetPrintPageCount();
        Title = $"{Loc("Demo.PrintPreview")} — {pages}p";
        Width = 900;
        Height = 1000;

        // Toolbar: printer picker + print + save-as-PDF. Printing is Windows-only (System.Drawing);
        // PDF export works everywhere (library code, no dependencies).
        _printerCombo = new ComboBox { MinWidth = 220, FontSize = 12 };
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            try
            {
                var printers = System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().ToList();
                _printerCombo.ItemsSource = printers;
                string def = new System.Drawing.Printing.PrinterSettings().PrinterName;
                _printerCombo.SelectedIndex = Math.Max(0, printers.IndexOf(def));
            }
            catch { /* no printing subsystem — combo stays empty, print button will report */ }
        }

        var printBtn = new Button { Content = Loc("Demo.Print"), FontSize = 12 };
        printBtn.Click += async (_, _) => await GuardAsync(() =>
            OperatingSystem.IsWindowsVersionAtLeast(6, 1)
                ? Print()
                : throw new PlatformNotSupportedException("Printing requires Windows; use Save PDF instead."));
        var pdfBtn = new Button { Content = Loc("Demo.SavePdf"), FontSize = 12 };
        pdfBtn.Click += async (_, _) => await GuardAsync(SavePdfAsync);
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 12 };

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(10, 8),
            Children = { _printerCombo, printBtn, pdfBtn, _status }
        };

        var stack = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        for (int i = 0; i < pages; i++)
        {
            stack.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#CFCFCF")),
                BorderThickness = new Thickness(1),
                BoxShadow = BoxShadows.Parse("0 1 10 0 #40000000"),
                Child = new Image
                {
                    Source = editor.RenderPrintPage(i),
                    Width = 794,  // A4 @96DPI, matching the library's page metrics
                    Height = 1123
                }
            });
        }

        var dock = new DockPanel();
        var barHost = new Border { Background = new SolidColorBrush(Color.Parse("#F5F6F8")), Child = bar };
        DockPanel.SetDock(barHost, Dock.Top);
        dock.Children.Add(barHost);
        dock.Children.Add(new ScrollViewer
        {
            Content = stack,
            Background = new SolidColorBrush(Color.Parse("#9E9E9E"))
        });
        Content = dock;
    }

    private async System.Threading.Tasks.Task GuardAsync(Func<System.Threading.Tasks.Task> action)
    {
        try { _status.Text = ""; await action(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private System.Threading.Tasks.Task Print()
    {
        var doc = new System.Drawing.Printing.PrintDocument();
        if (_printerCombo.SelectedItem is string printer) doc.PrinterSettings.PrinterName = printer;
        int page = 0, total = _editor.GetPrintPageCount();
        doc.PrintPage += (_, e) =>
        {
            // Render at print DPI, hand to GDI+ as PNG. PageBounds covers the full sheet; the
            // printer's hardware margins clip less than the page's own 48px (~0.5in) margins.
            using var av = _editor.RenderPrintPage(page, 300);
            using var ms = new MemoryStream();
            av.Save(ms);
            ms.Position = 0;
            using var img = System.Drawing.Image.FromStream(ms);
            e.Graphics!.DrawImage(img, e.PageBounds);
            page++;
            e.HasMorePages = page < total;
        };
        doc.Print();
        _status.Text = Loc("Demo.PrintDone");
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task SavePdfAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "document.pdf",
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } }
        });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync();
        _editor.SavePdf(stream); // UI thread by contract (renders pages)
        _status.Text = Loc("Demo.PdfDone");
    }
}
