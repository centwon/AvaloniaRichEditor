using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaRichTextBoxPort.Documents;

namespace AvaloniaRichTextBoxPort.Controls;

public class CustomRichTextBox : Control
{
    private DispatcherTimer _caretTimer;
    private bool _isCaretVisible;
    private int _caretIndex;
    
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private bool _isSelecting = false;

    public static readonly StyledProperty<FlowDocument?> DocumentProperty =
        AvaloniaProperty.Register<CustomRichTextBox, FlowDocument?>(nameof(Document));

    public FlowDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    static CustomRichTextBox()
    {
        AffectsRender<CustomRichTextBox>(DocumentProperty);
    }

    public CustomRichTextBox()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);

        _caretTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _caretTimer.Tick += (s, e) =>
        {
            _isCaretVisible = !_isCaretVisible;
            InvalidateVisual();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsFocusedProperty)
        {
            if (IsFocused)
            {
                _isCaretVisible = true;
                _caretTimer.Start();
            }
            else
            {
                _caretTimer.Stop();
                _isCaretVisible = false;
            }
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        int idx = GetIndexFromPoint(point);
        _caretIndex = idx;
        _selectionStart = idx;
        _selectionEnd = idx;
        _isSelecting = true;
        ResetCaretBlink();
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isSelecting)
        {
            var point = e.GetPosition(this);
            int idx = GetIndexFromPoint(point);
            if (_selectionEnd != idx)
            {
                _selectionEnd = idx;
                _caretIndex = idx;
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isSelecting)
        {
            _isSelecting = false;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (Document == null || string.IsNullOrEmpty(e.Text)) return;

        DeleteSelection();
        InsertText(e.Text);
        _caretIndex += e.Text.Length;
        _selectionStart = _caretIndex;
        _selectionEnd = _caretIndex;
        
        ResetCaretBlink();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.B)
        {
            ApplyStyleToSelection(r => r.FontWeight = r.FontWeight == FontWeight.Bold ? FontWeight.Normal : FontWeight.Bold);
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        
        if (ctrl && e.Key == Key.I)
        {
            ApplyStyleToSelection(r => r.Foreground = r.Foreground == Brushes.Red ? Brushes.Black : Brushes.Red); 
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (ctrl && e.Key == Key.C)
        {
            CopyToClipboard();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            if (_caretIndex > 0) _caretIndex--;
            if (!shift) { _selectionStart = _caretIndex; _selectionEnd = _caretIndex; }
            else _selectionEnd = _caretIndex;
            ResetCaretBlink();
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            int maxLen = GetTotalLength();
            if (_caretIndex < maxLen) _caretIndex++;
            if (!shift) { _selectionStart = _caretIndex; _selectionEnd = _caretIndex; }
            else _selectionEnd = _caretIndex;
            ResetCaretBlink();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            if (_selectionStart != _selectionEnd)
            {
                DeleteSelection();
            }
            else if (_caretIndex > 0)
            {
                DeleteText(_caretIndex - 1, 1);
                _caretIndex--;
            }
            _selectionStart = _caretIndex;
            _selectionEnd = _caretIndex;
            ResetCaretBlink();
            e.Handled = true;
        }
    }

    private void ResetCaretBlink()
    {
        _isCaretVisible = true;
        _caretTimer.Stop();
        _caretTimer.Start();
        InvalidateVisual();
    }

    private int GetTotalLength()
    {
        if (Document == null) return 0;
        int len = 0;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    if (inline is Run r && r.Text != null) len += r.Text.Length;
                }
            }
        }
        return len;
    }

    private void ApplyStyleToSelection(Action<Run> styleAction)
    {
        int min = Math.Min(_selectionStart, _selectionEnd);
        int max = Math.Max(_selectionStart, _selectionEnd);
        if (min == max) return;

        int currentIndex = 0;
        if (Document == null) return;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    if (inline is Run run)
                    {
                        int runLen = run.Text?.Length ?? 0;
                        if (max > currentIndex && min < currentIndex + runLen)
                        {
                            styleAction(run);
                        }
                        currentIndex += runLen;
                    }
                }
            }
        }
    }

    private void DeleteSelection()
    {
        int min = Math.Min(_selectionStart, _selectionEnd);
        int max = Math.Max(_selectionStart, _selectionEnd);
        if (min == max) return;
        
        DeleteText(min, max - min);
        _caretIndex = min;
        _selectionStart = _caretIndex;
        _selectionEnd = _caretIndex;
    }

    private string GetSelectedText()
    {
        int min = Math.Min(_selectionStart, _selectionEnd);
        int max = Math.Max(_selectionStart, _selectionEnd);
        if (min == max) return "";

        string fullText = "";
        if (Document != null)
        {
            foreach (var block in Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (var inline in p.Inlines)
                    {
                        if (inline is Run run && !string.IsNullOrEmpty(run.Text))
                        {
                            fullText += run.Text;
                        }
                    }
                }
            }
        }
        
        if (min >= 0 && max <= fullText.Length)
        {
            return fullText.Substring(min, max - min);
        }
        return "";
    }

    private async void CopyToClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            string text = GetSelectedText();
            if (!string.IsNullOrEmpty(text))
            {
                await clipboard.SetTextAsync(text);
            }
        }
    }

    private async void PasteFromClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                DeleteSelection();
                InsertText(text);
                _caretIndex += text.Length;
                _selectionStart = _caretIndex;
                _selectionEnd = _caretIndex;
                ResetCaretBlink();
            }
        }
    }

    private void InsertText(string text)
    {
        if (Document == null || Document.Blocks.Count == 0) return;
        int currentIndex = 0;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    if (inline is Run run)
                    {
                        int runLen = run.Text?.Length ?? 0;
                        if (_caretIndex >= currentIndex && _caretIndex <= currentIndex + runLen)
                        {
                            int localIndex = _caretIndex - currentIndex;
                            run.Text = (run.Text ?? "").Insert(localIndex, text);
                            return;
                        }
                        currentIndex += runLen;
                    }
                }
            }
        }
    }

    private void DeleteText(int index, int length)
    {
        if (Document == null) return;
        int currentIndex = 0;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    if (inline is Run run)
                    {
                        int runLen = run.Text?.Length ?? 0;
                        if (index >= currentIndex && index < currentIndex + runLen)
                        {
                            int localIndex = index - currentIndex;
                            int deleteLen = Math.Min(length, runLen - localIndex);
                            run.Text = (run.Text ?? "").Remove(localIndex, deleteLen);
                            return; 
                        }
                        currentIndex += runLen;
                    }
                }
            }
        }
    }

    private double GetXForIndex(int localIndex, string fullText, Paragraph paragraph)
    {
        if (localIndex == 0) return 0;
        var caretText = new FormattedText(
            fullText.Substring(0, localIndex),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            14,
            Brushes.Black)
        {
            MaxTextWidth = double.PositiveInfinity,
            TextAlignment = TextAlignment.Left
        };

        int localIdx2 = 0;
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is Run run && !string.IsNullOrEmpty(run.Text))
            {
                int length = run.Text.Length;
                int start = Math.Max(localIdx2, 0);
                int end = Math.Min(localIdx2 + length, localIndex);
                if (end > start)
                {
                    if (run.FontWeight != FontWeight.Normal)
                        caretText.SetFontWeight(run.FontWeight, start, end - start);
                    if (run.Foreground != null)
                        caretText.SetForegroundBrush(run.Foreground, start, end - start);
                }
                localIdx2 += length;
            }
        }

        return caretText.WidthIncludingTrailingWhitespace;
    }

    private int GetIndexFromPoint(Point p)
    {
        if (Document == null) return 0;
        double yOffset = 0;
        int globalIndex = 0;
        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                string fullText = "";
                foreach (var inline in paragraph.Inlines) { if (inline is Run r) fullText += r.Text; }
                
                double height = 20; 
                if (p.Y >= yOffset && p.Y <= yOffset + height)
                {
                    for (int i = 0; i <= fullText.Length; i++)
                    {
                        double x = GetXForIndex(i, fullText, paragraph);
                        if (x >= p.X) return globalIndex + Math.Max(0, i - 1); 
                    }
                    return globalIndex + fullText.Length;
                }
                globalIndex += fullText.Length;
                yOffset += height + 10;
            }
        }
        return globalIndex;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Document == null) return;

        double yOffset = 0;
        double maxWidth = Bounds.Width;
        int globalCharIndex = 0;
        
        Point? caretPoint = null;
        double caretHeight = 16; 

        int selMin = Math.Min(_selectionStart, _selectionEnd);
        int selMax = Math.Max(_selectionStart, _selectionEnd);

        foreach (var block in Document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                string fullText = "";
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is Run r && !string.IsNullOrEmpty(r.Text))
                    {
                        fullText += r.Text;
                    }
                }

                if (fullText == "") 
                {
                    if (_caretIndex == globalCharIndex)
                    {
                        caretPoint = new Point(0, yOffset);
                    }
                    yOffset += 20; 
                    continue;
                }

                if (selMin != selMax)
                {
                    int pStart = globalCharIndex;
                    int pEnd = globalCharIndex + fullText.Length;
                    
                    int overlapStart = Math.Max(selMin, pStart);
                    int overlapEnd = Math.Min(selMax, pEnd);

                    if (overlapStart < overlapEnd)
                    {
                        int localStart = overlapStart - pStart;
                        int localEnd = overlapEnd - pStart;

                        double x1 = GetXForIndex(localStart, fullText, paragraph);
                        double x2 = GetXForIndex(localEnd, fullText, paragraph);

                        var rect = new Rect(x1, yOffset, x2 - x1, caretHeight);
                        context.FillRectangle(new SolidColorBrush(Color.FromArgb(100, 0, 120, 215)), rect);
                    }
                }

                var formattedText = new FormattedText(
                    fullText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    14,
                    Brushes.Black)
                {
                    MaxTextWidth = maxWidth > 0 ? maxWidth : double.PositiveInfinity,
                    TextAlignment = TextAlignment.Left
                };

                int localIndex = 0;
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is Run run && !string.IsNullOrEmpty(run.Text))
                    {
                        int length = run.Text.Length;
                        if (run.FontWeight != FontWeight.Normal)
                        {
                            formattedText.SetFontWeight(run.FontWeight, localIndex, length);
                        }
                        if (run.Foreground != null)
                        {
                            formattedText.SetForegroundBrush(run.Foreground, localIndex, length);
                        }
                        localIndex += length;
                    }
                }

                if (_caretIndex >= globalCharIndex && _caretIndex <= globalCharIndex + fullText.Length)
                {
                    int caretLocalIndex = _caretIndex - globalCharIndex;
                    double x = GetXForIndex(caretLocalIndex, fullText, paragraph);
                    caretPoint = new Point(x, yOffset);
                }

                context.DrawText(formattedText, new Point(0, yOffset));
                globalCharIndex += fullText.Length;
                yOffset += formattedText.Height + 10;
            }
        }

        if (_isCaretVisible && caretPoint.HasValue && IsFocused && selMin == selMax)
        {
            var pen = new Pen(Brushes.Black, 1.5);
            context.DrawLine(pen, caretPoint.Value, new Point(caretPoint.Value.X, caretPoint.Value.Y + caretHeight));
        }
    }
}
