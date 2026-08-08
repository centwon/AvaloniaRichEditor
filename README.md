# AvaloniaRichEditor

[![NuGet](https://img.shields.io/nuget/v/AvaloniaRichEditor.svg)](https://www.nuget.org/packages/AvaloniaRichEditor)
[![Downloads](https://img.shields.io/nuget/dt/AvaloniaRichEditor.svg)](https://www.nuget.org/packages/AvaloniaRichEditor)
[![CI](https://github.com/centwon/AvaloniaRichEditor/actions/workflows/ci.yml/badge.svg)](https://github.com/centwon/AvaloniaRichEditor/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/centwon/AvaloniaRichEditor/blob/main/LICENSE)

A from-scratch rich text editor control for [Avalonia](https://avaloniaui.net) — a pure C# port of the
ideas behind WPF's `RichTextBox`/`FlowDocument`, built entirely on Avalonia's `TextLayout` engine (no
PTS/unmanaged dependency). Rendering, layout, hit-testing, selection, and IME are implemented directly.

*Read this in other languages: [한국어](https://github.com/centwon/AvaloniaRichEditor/blob/main/README.ko.md)*

> The public API is frozen and follows [SemVer](https://semver.org): no breaking change without a major
> bump. See the
> [changelog](https://github.com/centwon/AvaloniaRichEditor/blob/main/CHANGELOG.md) and the
> [roadmap](https://github.com/centwon/AvaloniaRichEditor/blob/main/Project_Roadmap.md).

## Requirements

| | |
|---|---|
| Target framework | .NET 10 (`net10.0`) |
| Avalonia | 12.0.1 |
| Dependencies | [Avalonia](https://github.com/AvaloniaUI/Avalonia), [HtmlAgilityPack](https://html-agility-pack.net/) — that's all |
| Platforms | Developed and tested on Windows; macOS/Linux are best-effort ([details](#platform-support)) |
| Native AOT | Supported (`IsAotCompatible`) |

## Install

```
dotnet add package AvaloniaRichEditor
```

## Quick start

```xml
<!-- MainWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:rte="using:AvaloniaRichEditor.Controls">
    <rte:RichEditor x:Name="Editor" />
</Window>
```

```csharp
using Avalonia.Media;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

// Start from an empty document...
Editor.Document = new FlowDocument();

// ...or load HTML / JSON
Editor.LoadHtml("<p>Hello <b>world</b></p>");

// Read it back
string html = Editor.ToHtml();
string json = Editor.ToJson();

// React to changes
Editor.TextChanged      += (_, _) => MarkDirty();
Editor.SelectionChanged += (_, _) => UpdateToolbar();

// Customize appearance
Editor.SelectionBrush    = Brushes.LightSkyBlue;
Editor.CaretBrush        = Brushes.Black;
Editor.FontFamilyChoices = new[] { "Segoe UI", "Arial", "맑은 고딕" }; // right-click font menu
```

For a batteries-included host, drop in **`RichEditorView`** (editor + toolbar + page/zoom + status bar)
instead of wiring `RichEditor` yourself; reach `view.Editor` / `view.Toolbar` for everything else. See
[`samples/AvaloniaRichEditor.Demo`](https://github.com/centwon/AvaloniaRichEditor/tree/main/samples/AvaloniaRichEditor.Demo)
for a full editor host.

## Features

### Text and paragraphs

- Inline formatting: bold / italic / underline / strikethrough, font family and size, foreground and
  highlight colors, hyperlinks
- Paragraphs with alignment, line spacing, indentation, headings, and bullet / numbered lists
- Korean/CJK **IME** composition with inline preedit
- Find / replace, undo / redo

### Tables

- Cell merge (colspan/rowspan), column and row resize, Tab cell navigation
- Cells are **full block containers** — multiple paragraphs, block images, dividers, and **nested tables**
  to any depth, with recursive layout/hit-testing, per-cell resize, and Tab traversal across nesting
- **Inline tables** (HWP-style "treat as character"): a table flows inside a text line like an image but
  stays fully editable — click into a cell, type, navigate with arrows/Tab, resize. Toggle between block
  and inline from the right-click menu
- **Draw-to-size insertion**: pick rows × columns from the grid, then drag on the document to set the
  size (or click for the default)

### Images and page layout

- Inline and block **images** — insert, resize, replace, save
- Word-style **page view**: `PageSize` (Continuous by default, or A4/A3/A5/B4/B5/Letter/Legal/Tabloid),
  `PageOrientation`, `ShowPageBoundaries`, line-boundary page breaks, headers/footers/page numbers
- Page setup is **persisted per document** (`FlowDocument.PageSetup`) and re-applied on load, like a word
  processor
- **Print and PDF**: per-page bitmap rendering (`RenderPrintPage`, 300 DPI) and dependency-free raster PDF
  export (`SavePdf`)

### Interchange

- Clipboard: internal rich copy/paste, rich **HTML copy-out** (`CF_HTML`), external HTML/**RTF** paste
  (Word/HWP), image paste, Excel/TSV → table
- **HTML, JSON, and RTF** import/export. JSON/`.flow` and HTML round-trip losslessly (an inline table
  stays inline)
- RTF export is deliberately **richer than RTF import**: merged cells and per-cell shading are written for
  Word/HWP but ignored on the way back in, and a nested table imports at default column widths (Word keeps
  those in an ignorable group)

### Hosting

- **Drop-in `RichEditorView`**: editor + formatting toolbar with built-in page/zoom controls and
  Export/Import/Print file actions + status bar
- Standalone `RichEditorToolbar` with a `ToolbarLevel` density knob (Auto/Minimal/Normal/Maximum)
- Capability is expressed directly through `IsReadOnly` (viewer switch) plus the `Allow*` feature flags
- **Word-standard keyboard shortcuts** from a single source (`RichEditorShortcuts`) shared by the key
  handler, menu hints, and toolbar tooltips — B/I/U/S, headings `Ctrl+Alt+1..6`, alignment `Ctrl+L/E/R/J`,
  lists, line spacing `Ctrl+1/5/2`, indent, font size, and more
- Per-object right-click context menus (HWP-style, reflecting the caret's state; a slim
  `ShowFormattingMenu = false` default keeps rich formatting on the toolbar)
- Built-in **localization** (Korean and English, host-extensible) for menus, toolbar, and dialogs

## Documentation

| | |
|---|---|
| [Document format specification](https://github.com/centwon/AvaloniaRichEditor/blob/main/docs/DOCUMENT_FORMAT.md) | JSON document format v1.0 and the `.flow` package |
| [Changelog](https://github.com/centwon/AvaloniaRichEditor/blob/main/CHANGELOG.md) | Release history |
| [Roadmap](https://github.com/centwon/AvaloniaRichEditor/blob/main/Project_Roadmap.md) | Current status and what is pending |

API documentation ships with the package as XML docs, so IntelliSense covers every public member.

## Platform support

The control is written against cross-platform Avalonia APIs and has **no P/Invoke**. However it is
currently developed and tested on **Windows**; macOS/Linux are **best-effort** for now:

- Clipboard HTML is matched by format identifier and handles the Windows `CF_HTML` header transparently
  (other platforms' plain `text/html` passes through unchanged).
- No fonts are assumed: runs fall back to `DefaultFontFamily`, and the right-click font list comes from
  `FontFamilyChoices`. Set both for your target platform/locale (the demo uses Korean fonts).

CI builds and tests pass on **Windows, macOS, and Linux** (3-OS matrix); deeper functional verification
on macOS/Linux is still pending (tracked in the roadmap).

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

### Project layout

| Path | Contents |
|---|---|
| `src/AvaloniaRichEditor` | The control library (`Controls`, document model `Documents`, `Formatters`). NuGet target. |
| `samples/AvaloniaRichEditor.Demo` | A WinExe demo/test app: toolbar, window, sample document. |
| `tests/` | xUnit v3 suites: model/formatters, headless control tests, and real-Skia render tests. |
| `tools/rtfgen` | Interop reproduction tool — generates documents and measures them in Word over COM. |

## Contributing

Issues and pull requests are welcome at
[github.com/centwon/AvaloniaRichEditor](https://github.com/centwon/AvaloniaRichEditor/issues). Interop
reports are especially useful — if a document looks wrong in Word, HWP, or a browser, that is the one
class of defect this project's own tests cannot see.

## License

[MIT](https://github.com/centwon/AvaloniaRichEditor/blob/main/LICENSE) © 2026 centwon. Depends on
[Avalonia](https://github.com/AvaloniaUI/Avalonia) and [HtmlAgilityPack](https://html-agility-pack.net/)
(both MIT) — see
[THIRD-PARTY-NOTICES.md](https://github.com/centwon/AvaloniaRichEditor/blob/main/THIRD-PARTY-NOTICES.md).
