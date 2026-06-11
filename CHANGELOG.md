# Changelog

All notable changes to **AvaloniaRichEditor** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **`RichEditorToolbar`** (N3.6 layer ②): optional formatting toolbar driven by a single
  `Target` property — calls the editor's public commands, reflects the caret's formatting
  (B/I/U/S, lists, font family/size, heading, alignment, undo/redo), and follows the editor's
  feature flags (`AllowImages`/`AllowTables` hide insert buttons; `IsReadOnly` hides the strip).
  Includes color-palette and table-size-grid flyouts promoted from the demo.
- **`RichEditorView`** (N3.6 layer ③): one-line drop-in bundling editor + toolbar + scroller,
  pre-wired. Reach `Editor`/`Toolbar` for everything else.
- **`RichEditorLocalization`**: key-based UI strings for the built-in chrome (context menus,
  toolbar, dialogs). Korean and English ship in-box and follow the OS UI language; hosts can
  add or override languages at runtime via `Register()` (per-key merge, English fallback).
  AOT-safe (plain dictionaries, no satellite assemblies).

### Added (editing)
- **HWP-style "Inline with text" (글자처럼 취급) toggle** on the image context menu: a block image
  can be demoted to an inline image that flows with the text as a single character, and an inline
  image promoted back to a block (disabled inside table cells, which cannot host blocks). Bytes,
  mime type and display size survive the round trip; both directions are undoable.

### Added (file format)
- **`.ardx` package format**: a ZIP container with `document.json` plus raw image entries keyed by
  content hash (stored uncompressed — the win over plain JSON is dropping the ~33% base64
  overhead). New APIs: `RichEditor.SavePackageAsync(Stream)` / `LoadPackageAsync(Stream)`
  (snapshot + background, like the JSON async pair) and `DocumentPackage.Save/Load`. The JSON
  string contract is unchanged.

### Changed
- **JSON schema v2 — image pool deduplication**: identical images (same encoded bytes) are now
  stored once in a document-level pool keyed by SHA-256 and referenced from blocks, instead of
  inline base64 per image. Documents that repeat a logo/screenshot shrink accordingly, and loading
  shares a single byte array per pooled image in memory. v1 documents (inline base64) still load.
- **Font pickers list the installed system fonts** by default (`FontFamilyChoices` empty =
  system list, sorted and localized by the OS UI language — e.g. "맑은 고딕" on Korean Windows);
  assigning a non-empty list still curates as before.
- **Default font is the OS UI font** (Windows message font via `SystemParametersInfo`,
  e.g. Malgun Gothic on Korean Windows) instead of Avalonia's app font; non-Windows platforms
  keep `FontFamily.Default`. The toolbar's font combo shows the effective default as placeholder
  text when the text at the caret carries no explicit font.
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
- `RichEditor.ToJsonAsync()` / `LoadJsonAsync()` — JSON save/load on a background thread (N6-3).
  `ToJsonAsync` snapshots the document first, so edits made during serialization can't tear the
  output. The synchronous `ToJson`/`LoadJson` remain unchanged.
- **Block margins**: `MarginTop`/`MarginBottom` moved up to `Block`, so images, tables, and
  dividers now have adjustable vertical spacing (previously a fixed 10px gap); paragraphs gain
  `MarginRight` (narrows the wrap width — paragraph-only, since nothing flows around blocks),
  and the left margin reuses the existing `Indent`. A right-click "Margin" submenu offers per-side
  presets. Documents saved before these fields existed load with the historical fixed spacing.
- **Pluggable chrome icons**: `RichEditorIcons.Provider` lets a host swap the built-in text glyphs
  on the toolbar and context menus for any icon library (e.g. FluentIcons.Avalonia), keyed by the
  `RichEditorIcon` slot enum (41 slots). The library still ships no icon assets; a null provider
  (or null per slot) keeps the lightweight built-in glyphs. The demo app maps all slots to
  Fluent UI System Icons as a reference.
- **Soft image-count limit (N6-6)**: `MaxRecommendedImages` styled property (default 50, ≤0
  disables) and the edge-triggered `RecommendedImageLimitExceeded` event let hosts warn the user
  when a document grows past the smooth-editing range measured in benchmarks; editing is never
  blocked. `GetImageCount()` reports the current count (block + inline + table-cell images).

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
