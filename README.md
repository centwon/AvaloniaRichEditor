# AvaloniaRichEditor

A from-scratch rich text editor control for [Avalonia](https://avaloniaui.net) — a pure C# port of the
ideas behind WPF's `RichTextBox`/`FlowDocument`, built entirely on Avalonia's `TextLayout` engine (no
PTS/unmanaged dependency). Rendering, layout, hit-testing, selection, and IME are implemented directly.

> **Status: `0.x` (pre-release / alpha).** The public API may still change. See
> [`Project_Roadmap.md`](Project_Roadmap.md) for the path to a stable `1.0` and NuGet release.

## Features

- Rich inline formatting: bold / italic / underline / strikethrough, font family & size, foreground &
  highlight colors, hyperlinks
- Paragraphs with alignment, line spacing, indentation, headings, and bullet / numbered lists
- **Tables** with cell merge (colspan/rowspan), column & row resize, and Tab cell navigation
- Inline and block **images** (insert, resize, replace, save)
- Find / replace, undo / redo, per-object right-click context menus
- Clipboard: internal rich copy/paste, external **HTML** paste, image paste, Excel/TSV → table
- HTML and JSON import/export (round-trippable)
- Korean/CJK **IME** composition (inline preedit)

## Quick start

```xml
<!-- MainWindow.axaml -->
<rtb:RichEditor xmlns:rtb="using:AvaloniaRichEditor.Controls"
                       x:Name="Editor" />
```

```csharp
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

// Start from an empty document, or load HTML/JSON
Editor.Document = new FlowDocument();
Editor.LoadHtml("<p>Hello <b>world</b></p>");

// Read it back
string html = Editor.ToHtml();
string json = Editor.ToJson();

// React to changes
Editor.TextChanged      += (_, _) => MarkDirty();
Editor.SelectionChanged += (_, _) => UpdateToolbar();

// Customize appearance
Editor.SelectionBrush   = Brushes.LightSkyBlue;
Editor.CaretBrush       = Brushes.Black;
Editor.FontFamilyChoices = new[] { "Segoe UI", "Arial", "맑은 고딕" }; // right-click font menu
```

See [`samples/AvaloniaRichEditor.Demo`](samples/AvaloniaRichEditor.Demo) for a full editor host with a
toolbar.

## Platform support

The control is written against cross-platform Avalonia APIs and has **no P/Invoke**. However it is
currently developed and tested on **Windows**; macOS/Linux are **best-effort** for now:

- Clipboard HTML is matched by format identifier and handles the Windows `CF_HTML` header transparently
  (other platforms' plain `text/html` passes through unchanged).
- No fonts are assumed: runs fall back to `DefaultFontFamily`, and the right-click font list comes from
  `FontFamilyChoices`. Set both for your target platform/locale (the demo uses Korean fonts).

Cross-platform smoke testing and CI are tracked in the roadmap (N3/N4).

## Accessibility

The editor exposes an automation peer (`AutomationControlType.Edit` + `IValueProvider`), so screen
readers can read and set its text content — the same level Avalonia's built-in `TextBox` offers
(Avalonia's public automation model does not yet include a text-range/`ITextProvider` pattern). Give the
control a label from your view with `AutomationProperties.Name="..."` (or `LabeledBy`).

## Building

```
dotnet build AvaloniaRichEditor.slnx
dotnet run --project samples/AvaloniaRichEditor.Demo/AvaloniaRichEditor.Demo.csproj
```

## Project layout

| Path | Contents |
|---|---|
| `src/AvaloniaRichEditor` | The control library (`Controls`, document model `Documents`, `Formatters`). NuGet target. |
| `samples/AvaloniaRichEditor.Demo` | A WinExe demo/test app: toolbar, window, sample document. |

## License

[MIT](LICENSE) © 2026 centwon. Depends on [Avalonia](https://github.com/AvaloniaUI/Avalonia) and
[HtmlAgilityPack](https://html-agility-pack.net/) (both MIT) — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
