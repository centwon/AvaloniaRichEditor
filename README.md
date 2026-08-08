# AvaloniaRichEditor

*Read this in other languages: [한국어](README.ko.md)*

A from-scratch rich text editor control for [Avalonia](https://avaloniaui.net) — a pure C# port of the
ideas behind WPF's `RichTextBox`/`FlowDocument`, built entirely on Avalonia's `TextLayout` engine (no
PTS/unmanaged dependency). Rendering, layout, hit-testing, selection, and IME are implemented directly.

> **Status: `1.1.0`** — published on [NuGet](https://www.nuget.org/packages/AvaloniaRichEditor).
> The public API is frozen and follows [SemVer](https://semver.org): no breaking change without a major
> bump. See [`CHANGELOG.md`](CHANGELOG.md) and [`Project_Roadmap.md`](Project_Roadmap.md).

## Install

```
dotnet add package AvaloniaRichEditor
```

## Features

- Rich inline formatting: bold / italic / underline / strikethrough, font family & size, foreground &
  highlight colors, hyperlinks
- Paragraphs with alignment, line spacing, indentation, headings, and bullet / numbered lists
- **Tables** with cell merge (colspan/rowspan), column & row resize, and Tab cell navigation. Cells are
  full block containers — multiple paragraphs, block images, dividers, and **nested tables** (to any
  depth), with recursive layout/hit-testing, per-cell resize, and Tab traversal that enters/exits nesting
  - **Inline tables** (HWP-style "treat as character"): a table flows inside a text line like an image but
    stays fully editable — click into a cell, type, navigate with arrows/Tab, resize. Toggle a block table
    to inline and back via the right-click menu
  - **Draw-to-size insertion**: pick rows × columns from the grid, then drag on the document to set the
    table's size (or click for the default size)
- Inline and block **images** (insert, resize, replace, save)
- Find / replace, undo / redo, per-object right-click context menus (HWP-style, reflecting the caret's
  state; a slim `ShowFormattingMenu = false` default keeps rich formatting on the toolbar)
- **Word-standard keyboard shortcuts** via a single source (`RichEditorShortcuts`) shared by the key
  handler, menu hints, and toolbar tooltips — B/I/U/S, headings `Ctrl+Alt+1..6`, alignment `Ctrl+L/E/R/J`,
  lists, line spacing `Ctrl+1/5/2`, indent, font size, and more
- Clipboard: internal rich copy/paste, rich **HTML copy-out** (`CF_HTML`) and external HTML/**RTF** paste
  (Word/HWP), image paste, Excel/TSV → table
- HTML, JSON, and **RTF** import/export. JSON/`.flow` and HTML round-trip losslessly (an inline table
  stays inline); RTF export is deliberately richer than RTF import — merged cells and per-cell shading are
  written for Word/HWP but ignored on the way back in, and a nested table imports at default column widths
  (Word keeps them in an ignorable group) — see the
  [document format specification](docs/DOCUMENT_FORMAT.md) (JSON document format v1.0 and the `.flow` package)
- Korean/CJK **IME** composition (inline preedit)
- Word-style **page view** with selectable paper size (`PageSize`: Continuous (default) / A4/A3/A5/B4/B5/
  Letter/Legal/Tabloid), `PageOrientation`, and `ShowPageBoundaries` — line-boundary page breaks,
  headers/footers/page numbers. The page setup is **persisted per document** (`FlowDocument.PageSetup`) and
  re-applied on load, like a word processor
- **Print & PDF**: per-page bitmap rendering (`RenderPrintPage`, 300 DPI) and dependency-free raster
  PDF export (`SavePdf`)
- **Drop-in `RichEditorView`** (editor + formatting toolbar with built-in page/zoom controls and
  Export/Import/Print file actions + status bar) and a standalone `RichEditorToolbar` with a `ToolbarLevel`
  density knob (Auto/Minimal/Normal/Maximum). Capability is expressed directly through `IsReadOnly`
  (viewer switch) plus the `Allow*` feature flags
- Built-in **localization** (Korean & English, host-extensible) for menus, toolbar, and dialogs

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

For a batteries-included host, drop in **`RichEditorView`** (editor + toolbar + page/zoom + status bar)
instead of wiring `RichEditor` yourself; reach `view.Editor` / `view.Toolbar` for everything else. See
[`samples/AvaloniaRichEditor.Demo`](samples/AvaloniaRichEditor.Demo) for a full editor host.

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

## Project layout

| Path | Contents |
|---|---|
| `src/AvaloniaRichEditor` | The control library (`Controls`, document model `Documents`, `Formatters`). NuGet target. |
| `samples/AvaloniaRichEditor.Demo` | A WinExe demo/test app: toolbar, window, sample document. |

## License

[MIT](LICENSE) © 2026 centwon. Depends on [Avalonia](https://github.com/AvaloniaUI/Avalonia) and
[HtmlAgilityPack](https://html-agility-pack.net/) (both MIT) — see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
