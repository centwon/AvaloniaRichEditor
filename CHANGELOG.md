# Changelog

All notable changes to **AvaloniaRichEditor** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Selectable paper size** — `PageSize` (`RichEditorPageSize`: `Continuous`, `A4`, `A3`, `A5`, `B4`,
  `B5`, `Letter`, `Legal`, `Tabloid`) fixes the text-column width to the chosen sheet's content width;
  `Continuous` reflows to the control width as before. B4/B5 use the JIS series.
- **`PageOrientation`** (`Portrait`/`Landscape`) swaps the paper's width and height across the editor
  view, print, and PDF.
- **`ShowPageBoundaries`** — for a concrete paper size, toggles between the full Word-style page stack
  (grey desk, paper sheets, margins) and a lighter centered fixed-width column that flows continuously
  with a faint dashed separator and a small whitespace gap at each page boundary.
- **`GetPaperPixelSize()`** returns the current paper's pixel size at 96 DPI (orientation-aware), for
  host fit-to-width and print math.

### Changed
- **Page layout redesign**: the single `PageView` bool is replaced by the orthogonal `PageSize` +
  `ShowPageBoundaries` (+ `PageOrientation`). The default is now **A4 with boundaries** (was the
  continuous layout). Hosts that want the old continuous behaviour set `PageSize = Continuous`.
- Print/PDF now follow the selected paper size and orientation (`Continuous` falls back to A4).
- **Package file extension `.ardx` → `.rdx`** (a more generic name; the ZIP package format itself is
  unchanged, and the stream-based `DocumentPackage` / `SavePackageAsync` API is untouched).

### Removed
- **`PageView`** (property + `PageViewProperty`). Replaced by `PageSize`/`ShowPageBoundaries`.

### Fixed
- Page-view hit-testing used a hardcoded A4 page height, so clicks/caret landed wrong for non-A4
  paper sizes; it now uses the selected paper's height.

## [0.4.0-alpha] - 2026-06-14

Clipboard interop (Word/HWP RTF, async HTML) and a round of editor UX fixes.

### Added
- **RTF clipboard paste** (`RtfDocumentFormatter`): pasting from Word or the Korean HWP now uses
  their "Rich Text Format" — tried before CF_HTML because RTF embeds image bytes inline (Word's
  CF_HTML only references temp files that may be gone). Parses paragraphs, bold/italic/underline/
  strike, font size, foreground colour (CJK text via the document `\ansicpg` code page), embedded
  PNG/JPEG images, simple tables (with source column widths from `\cellx`), and flattens nested
  tables / text boxes so their text isn't lost. Zero external dependencies. Browsers don't put RTF
  on the clipboard, so HTML paste is unchanged.
- **`HtmlDocumentFormatter.ParseHtmlAsync` / `RichEditor.LoadHtmlAsync`**: parse HTML while
  downloading remote (`http`) images concurrently off the UI thread, so a slow network no longer
  freezes the UI when pasting web content. The model is still built on the calling thread (Avalonia
  objects are thread-affine). Rich paste now uses this path; the synchronous `ParseHtml` keeps its
  budgeted inline download for sync callers.

### Changed
- Inserting a table fills the document width with equal columns (was a fixed 100 px each); inserting
  an image larger than the document width scales it down to fit (aspect ratio kept, bytes intact).
- After inserting a table/image/divider the caret moves into it (a table's first cell) or just after
  it, the editor refocuses, and the block scrolls into view — no click needed to see it.
- Selecting a whole table now shows an accent frame + fill, and hovering its outer left/top border
  shows a move cursor to signal it's selectable.

### Fixed
- Dragging a table's row/column border to resize no longer hitches when the caret is outside the
  table (the resize now re-measures the content height mid-drag).
- The bundled `RichEditorView` no longer clips the document's left/top edge, and reserves a right
  gutter so a full-width table/image's resize handle isn't hidden under the scrollbar.

## [0.3.0-alpha] - 2026-06-13

Toolbar polish, a bundled-view zoom, and host-injectable toolbar items.

### Added
- **`RichEditorView.ZoomFactor`** (1.0 = 100%, clamped 0.2–5.0): visual zoom for the document
  area only — the toolbar never scales. In page view the page scales to the zoom; in the
  continuous layout the editor reflows to the zoomed width.
- **`RichEditorToolbar.LeadingItems` / `TrailingItems`**: host controls (e.g. app-shell
  save/open/zoom buttons) injected before/after the formatting buttons in the same wrapping
  strip, so the whole toolbar stays one row that wraps together when narrow.
- **Built-in vector toolbar icons**: the toolbar's default glyphs are now hand-drawn vector
  paths (still zero icon dependencies). A host `RichEditorIcons.Provider` still overrides them.

### Changed
- The toolbar **wraps to additional rows** when the host is narrower than the strip, instead of
  showing a horizontal scrollbar.
- Undo/redo moved to the **start** of the toolbar (quick-access convention).
- `RichEditorView` anchors a short document to the **top** of the scroller (was vertically
  centered) and enables **horizontal scrolling in page view** so a zoomed-in page isn't clipped.

### Fixed
- `SavePackageAsync` / `ToJsonAsync` threw "the calling thread cannot access this object" for
  documents with colored text: the DTO (which reads thread-affine brush colors) is now built on
  the calling thread, with only JSON/zip writing offloaded (issue #1).

## [0.2.0-alpha] - 2026-06-13

Second pre-release: page layout, print/PDF output, the library toolbar/localization layer,
the `.ardx` package format, and a large editing-behaviour audit. First version published to NuGet.

### Added (page layout & print, 2026-06-12)
- **Word-style page view**: the `PageView` styled property (default `false` — existing hosts are
  unaffected) renders the document as a stack of A4 pages on a grey desk, breaking content at
  line boundaries; paragraphs straddling a page boundary continue on the next page. Editing,
  hit-testing, IME and selection all work across pages.
- **Print rendering**: `GetPrintPageCount()` and `RenderPrintPage(pageIndex, dpi = 96)` render
  one A4 page to a bitmap (300 DPI = print quality) with editing chrome (caret, selection,
  resize handles) stripped. Printing paginates even when `PageView` is off.
- **PDF export**: `SavePdf(Stream, dpi = 300)` writes a raster PDF (one image per page,
  Flate-compressed, no external dependencies). Text is not selectable in the output —
  full-fidelity for print/archive; vector PDF is a possible future addition.
- The demo gains a "Pages" view toggle and a print-preview window with printer selection,
  print (Windows, `System.Drawing.Printing` — demo-side only), and save-as-PDF.

### Fixed
- Tables with a custom bottom margin rendered with a hardcoded 10px gap (measure/hit-testing
  already honored `MarginBottom`; the render pass now matches).

### Fixed (2026-06-12 audit sweep)
- Partial formatting/deletion inside a styled run no longer drops the font family and highlight
  on the tail half (run split now clones every field).
- Copy no longer crashes the process when another application holds the clipboard open.
- Clicks on empty space below ~2,000px of content now place the caret (hit-test fill covered a
  fixed height instead of the control bounds).
- "Clear formatting" also resets font family and highlight.
- Pasting HTML with remote images no longer freezes the UI for 5s per image (shared HttpClient
  plus a 5-second total budget per paste; over-budget images are skipped).
- Backspace/Delete/arrows treat emoji (surrogate pairs) as one character instead of leaving a
  broken half behind.
- Deleting a selection that spans table cells keeps the cell structure (text no longer migrates
  across the grid).
- Mutating public APIs (`InsertText`, paste, formatting commands) consistently no-op when
  `IsReadOnly` is set.
- Tab-indented plain text no longer pastes as a bogus table (tighter TSV grid heuristic).

### Changed (editing behaviour)
- **Formatting toggles without a selection are now Word-style**: a caret inside a word styles
  that word; at an empty position the toggle becomes a *pending format* applied to the next
  typed text (previously the whole paragraph was styled). The pending state shows in
  `GetCaretFormat()` and clears on any caret move.
- Backspace/Delete runs coalesce into one undo checkpoint per run (like typing), removing the
  per-keypress full-document clone hitch on large documents.

### Added (editing)
- **Shift+Enter** inserts a soft line break (no paragraph split).
- **Ctrl+Shift+V** pastes as plain text (skips rich/HTML/image formats and the TSV-table heuristic).
- **URL auto-link**: typing a space after `http(s)://…` turns the URL into a hyperlink
  (the space stays unlinked).
- **`AllowLocalFileImages`** styled property (default `true`): when `false`, `file://` image
  sources in ingested HTML are skipped instead of read from disk — closes the path by which
  untrusted HTML pulls local files into the document. `HtmlDocumentFormatter.ParseHtml` gains a
  matching optional parameter.

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

[Unreleased]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.2.0-alpha...HEAD
[0.2.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.1.0-alpha...v0.2.0-alpha
[0.1.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/releases/tag/v0.1.0-alpha
