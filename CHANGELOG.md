# Changelog

All notable changes to **AvaloniaRichEditor** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **Image storage model (N6-2)**: images now keep their original encoded bytes
  (`ImageBlock.RawBytes`/`InlineImage.RawBytes` + `MimeType`); the `Bitmap` is a lazy render cache.
  Saving no longer re-encodes to PNG (a pasted ~80KB JPEG stays ~80KB instead of ballooning),
  JSON/HTML export embed the original format, and opening a document defers all image decoding
  to first render. Drag-handle resizes only change Width/Height (no generation loss).
  Legacy documents (no `MimeType` field) still load, treated as PNG.

### Added
- `RichEditor.InsertImageBytes(byte[])` — insert an image from its encoded bytes, preserving the
  original format. Preferred over `InsertImage(Bitmap)` when bytes are available.
- Image context menu: 1/2, 1/3, 1/4 scale presets (relative to natural size; display size only,
  no re-encoding).

## [0.1.0-alpha] - 2026-06-10

First public pre-release. The control is feature-complete for everyday rich-text
editing on Windows; the public API may still change before `1.0`.

### Added
- **Rich text editing** from scratch on Avalonia's `TextLayout` engine (no PTS/unmanaged dependency):
  rendering, layout, hit-testing, selection, caret, and IME all implemented directly.
- **Inline formatting**: bold / italic / underline / strikethrough, font family & size,
  foreground & highlight colors, hyperlinks (hover cursor + click to open).
- **Paragraphs** with alignment, line spacing, indentation, headings (h1–h6), and
  bullet / numbered lists.
- **Tables** with cell merge (colspan/rowspan), column & row resize, and Tab cell navigation.
- **Images**: inline icons and block images (insert, resize, replace, save).
- **Clipboard**: internal rich copy/paste (structure-preserving), external HTML paste,
  image paste/drag-drop, and Excel/TSV table paste.
- **HTML and JSON** import/export (round-trippable). JSON documents now carry a
  `"version"` schema field (legacy documents without it load as version 1).
- **Find / replace**, **undo / redo** (with typing coalescing), and per-object
  right-click context menus.
- **Korean/CJK IME** composition with inline preedit.
- **Editor modes**: `ReadOnly` / `Basic` / `Full` presets plus feature flags
  (`AllowImages`, `AllowTables`, `AllowRichPaste`, `AllowFindReplace`).
- **Public API**: `RichEditor` control with `ToHtml`/`LoadHtml`, `ToJson`/`LoadJson`,
  `Clear`, `CanUndo`/`CanRedo`, `TextChanged`/`SelectionChanged`/`DocumentChanged` events,
  and styled properties (`SelectionBrush`, `CaretBrush`, `DefaultFontFamily`, `DefaultFontSize`).
- **Accessibility**: `RichEditorAutomationPeer` (IValueProvider) exposing document plain text.
- **Packaging**: NuGet package with XML docs, symbol package (snupkg), SourceLink,
  MIT license, and bundled README.

### Known limitations
- Windows-first; macOS/Linux are best-effort (CI builds & tests pass on all three).
- Word images exported as VML (not standard `<img>`) are not imported.
- Precise pagination / PDF printing is not implemented (browser print fallback only).

[Unreleased]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.1.0-alpha...HEAD
[0.1.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/releases/tag/v0.1.0-alpha
