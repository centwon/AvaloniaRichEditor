# Changelog

All notable changes to **AvaloniaRichEditor** are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-07-31

**The API is frozen.** From here on the public surface follows SemVer: no breaking change without a
major bump. What made this 1.0 was not new features — the editor has been feature-complete since 0.8.0 —
but verification depth: interaction and render-pixel test infrastructure, a full-source audit, real
Word/HWP/browser checks of the exported formats, and measured performance.

Round-2 backport of platform-agnostic features the WinUI 3 port (WinUIRichEditor) pulled ahead on after
0.9.0, the defects a full read-through of every source file turned up, and the 1.0 verification work
(interaction and render-pixel test infrastructure, interoperability gaps, performance measurement, API
freeze review). New public API is additive apart from the two removals noted below;
355 → 528 unit + 9 → 17 render tests, build 0 warn / 0 err.

### Added
- **`RichEditor.IsModified` / `MarkSaved()` / `IsModifiedChanged`** — a "needs saving" dirty flag. Any edit
  (typing, delete, structural, formatting) sets it; loading a document or calling `MarkSaved()` clears it.
  Undo/redo count as modifications (it is a hint, not a content diff). The built-in toolbar Export clears it.
- **`RichEditor.RemoveList()`** — removes the list attribute (bullet/number, marker style, nesting level)
  from the selected paragraphs entirely, so "None" is reachable as an explicit list-style pick (not only the
  toggle, which required matching the current kind).
- **`RichEditor.AutoLinkOnType`** (default true) — toggle for typing-time auto-linking, and the linker now
  also recognizes `www.` tokens (prefixed `https://`), trims trailing punctuation, validates the result as an
  absolute http(s) URI with a dotted host, and fires on Tab/Enter as well as space.
- **`RichEditor.AllowRemoteImagesOnPaste`** (default true) — privacy opt-out: when false, remote
  (`http`/`https`) `<img>` sources in pasted/loaded HTML are not fetched. `HtmlDocumentFormatter.ParseHtml` /
  `ParseHtmlAsync` gain a matching `allowRemoteImages` parameter. `data:`/`file:` images are unaffected.
- **Find highlight-all.** `SetFindHighlight(query, matchCase)` / `ClearFindHighlight()` tint every occurrence
  of the query (amber, screen-only — never printed); `GetFindMatchPosition()` returns the selection's
  `(current, total)` position among matches for a find bar's "n/m" counter. `FindNext`/`FindPrev` set it.
- **Staged `Ctrl+A` inside a table** (HWP/Excel): the first press selects the cell's own contents, the
  next the whole table, then one enclosing table per press (a table nested in a cell, or an inline
  table’s host cell), and finally the document. A single-cell table has no separate table stage. Outside a table it still selects the document straight away. The stage is derived from
  the current selection, so a click or arrow key in between restarts the sequence.
- **`Select Cell` context-menu command** (ko: 셀 선택) — enters cell-selection mode with the clicked cell
  selected as a block, the only way to select exactly ONE cell (dragging needs two). Once in the mode a
  single click picks another cell, a drag extends the block, and a double-click drops back to a caret
  inside the cell.
- **`Paragraph.CopyFormatFrom(Paragraph)`** — copies every paragraph-level formatting field. One source
  for the edit paths that derive a new paragraph from an existing one (see the Enter fix below).

### API
- **`RichEditor.AutoLinkOnType` is now a `StyledProperty`** (`AutoLinkOnTypeProperty`), like every other
  behaviour flag (`IsReadOnly`, `Allow*`), so it can be bound and styled instead of only assigned in code.
  Same name, same default (`true`); the property was added after 0.9.0, so nothing depended on the old form.
- **`Formatters.RoundTripHarness` is no longer part of the library.** It is the `--roundtrip` CLI dev tool,
  not consumer API, and it uses nothing but the public formatter surface — so it moved into the demo
  project as an internal type. Removing public API is breaking, which is why it happens now, at the 1.0
  freeze, rather than after it.
- **Documentation corrected to match behaviour** ahead of the 1.0 API freeze: the `RichEditor` summary still
  advertised the removed `EditorMode` presets; `HtmlDocumentFormatter` did not mention that inline tables
  round-trip; `RtfDocumentFormatter` did not say that writing covers more than reading (merges and shading
  are export-only, nested tables flatten on import, an inline table splits its host paragraph);
  `RichEditorToolbar` did not state the focus guarantee; and `SetFindHighlight` claimed it tinted every
  match when the current selection is deliberately excluded.

### Interoperability
- **Inline tables survive an HTML round-trip.** HTML has no inline table, so one went out as a `<table>`
  and came back as a *block* table — saving and reloading split every paragraph that held one, and the
  text after the table started a new paragraph. Our own export marks those tables (`data-are-inline`) and
  the import puts them back on the text line. Foreign HTML carries no marker and still lands as a block
  table.
- **RTF import reads nested tables as nested tables.** `\nestcell`/`\nestrow` used to flatten into the
  parent cell's text (tabs and newlines) because the model had no table-in-a-cell; it does now, so a table
  inside a cell survives a Word/HWP paste — at any depth, keyed off `\itap` the way Word tells the levels
  apart — and the parent cell keeps its own paragraphs in their original order around it. Nested column
  widths still come out at the default: they live in the ignorable `{\*\nesttableprops}` group. The
  `{\nonesttables …}` fallback copy is now skipped, so its `\par` no longer lands as a stray line break in
  the parent cell.
- **RTF export carries what a table actually holds.** The writer emitted only plain runs from a cell, so
  merged cells, cell shading, images, list markers, nested tables and inline tables were dropped. Merges
  now emit `\clmgf`/`\clmrg` and `\clvmgf`/`\clvmrg` (a `\cellx` per column either way), shading emits
  `\clcbpat`, a cell writes all of its paragraphs/images/dividers/markers, and tables inside a cell go one
  `\itap` deeper via `\nestcell`/`\nestrow`. An inline table splits its host paragraph (RTF has no inline
  table), so text-then-table-then-rest comes out in order. Visual verification in Word/HWP is pending.

### Fixed
Found by reading every source file end to end; each is covered by a regression test.

Four of these came out of the Word/HWP visual check the roadmap had been carrying as pending — they
are the failures a round-trip through *our own* reader cannot show, because our reader tolerates the
malformed output our writer produced.
- **Exported tables had no borders in Word or HWP.** The writer emitted no `\clbrdr*` at all, so every
  table — top-level, nested, or from an inline table — arrived as an invisible grid: present and
  selectable, but with no lines, so an exported document did not look like the one on screen until
  borders were applied by hand. Each cell now carries a plain single border on all four sides.
- **Word glued a cell's text onto its nested table's first cell.** A paragraph that preceded a nested
  table inside the same cell was never terminated with `\par` before the writer descended, so
  "…형제 문단)" and "중첩1" ran together in both Word and HWP.
- **Word dropped the paragraphs that followed a nested table in the same cell.** They were written
  while `\itap` still pointed at the *inner* table's level, so Word booked them into a table that had
  already been closed and discarded them outright. The cell's own level is now re-declared as soon as
  the nested table ends, rather than only just before the closing `\cell`.
- **An inline table exported to HTML became a full-width band on its own line.** It inherited the
  block table's `width:100%`, so every consumer except our own importer (which reads the
  `data-are-inline` marker) laid it out as a block. It is now sized to its own columns and marked
  `display:inline-table`, so it sits in the text line in browsers and Word too.

- **The resize handle on an image inside a table cell did nothing.** A cell scales a picture down to
  fit its width, so the handle sits at the *drawn* right edge — but the drag arithmetic started from the
  image's *declared* width, which for anything inserted into a cell is normally several times larger
  (insertion caps to the document width, not the cell's). A drag of a few dozen pixels only moved the
  declared size within the range that still clamps to the same drawn width, so nothing moved at all; at
  greater nesting depth, where cells are narrower, it was worse. The handle now carries the size it was
  drawn at and the drag is measured against that. The resize also evicts the enclosing table chain and
  re-measures, like the column/row drags already did, so the row grows with the image on the same frame
  instead of after the next edit.
- **RTF import ignored cell merges and shading.** The writer has always emitted `\clmgf`/`\clmrg`,
  `\clvmgf`/`\clvmrg` and `\clcbpat`, and Word and HWP honour them — but the reader skipped all five, so
  a document exported and reloaded through *our own* format came back with the grid un-merged and the
  cell colours gone. Both are now read back.
- **An inline table did not survive an RTF round trip.** RTF has no inline table, so it goes out as a
  block table and came back as one, splitting its host paragraph permanently. Our own ignorable
  `{\*\arinline}` marker now rides along: other applications skip it and see exactly the block table
  they saw before, while our reader puts the table back on its text line. An inline table nested inside
  a table cell is still written (and read) as a nested block table.
- **A block inside an inline table's cell could not be deleted.** The block-removal walk searched the
  document's top level and then recursed through *block* table cells only, so an image, divider or
  nested table living in an inline table's cell was never found: Delete pushed an undo checkpoint,
  dropped the selection, and left the block on screen.
- **An inline table's host paragraph kept a stale layout when a non-paragraph block in one of its cells
  changed.** The host paragraph's cache signature folded in the inline table's cell *paragraphs* only, so
  resizing an image (or editing a table) inside such a cell changed the table's measured box without
  invalidating the line it sits on — the text kept its old line box until some other edit forced a rebuild.
- **An HTML round-trip inserted a space after every inline table.** The exported `</table>` carried the
  pretty-printing newline that block tables get, and inside a text line that whitespace text node parsed
  back as a space — so saving and reloading grew one space per inline table, per cycle.
- **README described RTF import as flattening nested tables**, which stopped being true when
  `\nestcell`/`\nestrow` began importing as real nested tables. It now states the losses that remain
  (merge flags, shading, and a nested table's column widths).
- **RTF import dropped text before any ignorable group.** The pending run was flushed at the group's
  closing brace with the skipped destination still active, which threw it away. Word writes such groups
  routinely (bookmarks, fields, and a nested table's `{\*\nesttableprops …}`), so ordinary Word documents
  imported with text missing. The run is now committed before descending into a group.
- **RTF import restarted a table row from inside a skipped group.** `\trowd`/`\cell`/`\row`/`\cellx` acted
  regardless of destination, and Word puts a nested table's row definition in `{\*\nesttableprops \trowd
  …\nestrow}` — so the parent cell's accumulated text was discarded mid-cell. All four are now guarded on
  the destination.
- **`Ctrl+Shift+X` cut the selection instead of striking it through.** The cut branch matched `Ctrl+X`
  without excluding Shift, so it swallowed the strikethrough shortcut added in 0.9.0 — pressing it made
  the selected text disappear. (`Ctrl+Z` already guarded the same way for `Ctrl+Shift+Z`.)
- **Enter, block paste and list conversion dropped paragraph formatting.** Three edit paths built the new
  paragraph by hand-copying five fields, silently losing line spacing, absolute line height, the right
  margin, the bullet/number marker style and the quote bar — so pressing Enter in a 1.5-spaced paragraph
  reverted the next one to single spacing. All three now go through `Paragraph.CopyFormatFrom`; Enter
  still drops back to body text (core rule #3).
- **Merging table cells destroyed everything past a covered cell's first paragraph.** `MergeCells` only
  folded in `.Para`, a leftover from the single-paragraph-cell era; extra paragraphs, block images,
  dividers and nested tables stayed in the covered cell, which `LogicalCells()` skips — invisible, and
  wiped outright by a later unmerge. They now move to the anchor cell in document order.
- **`TextPointer.CompareTo` never descended into nested or inline tables**, so a pointer inside one was
  reported "not found" (index -1) and always sorted first — selection ordering, range deletion and
  highlighting came out backwards for milestone A/B content. It now walks the same recursive document
  order as `ParagraphsInBlocks` / `CollectParagraphs`.
- **Find highlight-all muddied the current match.** The amber tint painted over the translucent selection
  blue, blending to a low-contrast fill that hid which match the caret was on. Highlight-all now marks the
  *other* matches (browser / VS Code behaviour); the current one stays a clean selection.
- **List markers were never drawn inside table cells.** `DrawListMarker` was called only from the
  top-level paragraph path, so a bulleted/numbered cell paragraph kept its `ListType` in the model but
  rendered as plain text. `DrawCellBlockList` now draws one marker per hard line (numbering restarts per
  cell), and the marker gutter is applied by *every* cell walk through the new `CellParaLeft` — render,
  hit-test, link hit-test, caret line layout and measure — so the text, the click position and the caret
  can't drift apart (core rule #1).
- **List commands only reached the first selected paragraph inside a table cell.** `ToggleBullet` /
  `ToggleNumbering` / `SetListStyle` / `RemoveList` collected only *top-level* selected paragraphs and
  otherwise fell back to "just the caret's paragraph", so selecting several paragraphs in one cell listed
  the first alone. Selection collection is now container-agnostic (`SelectedParagraphsInOrder`); only the
  hard-line splitting path, which splices `Document.Blocks`, stays top-level — cell paragraphs toggle in
  place.
- **Clicking a resize handle marked the document modified.** Image, inline-image, column and row handles
  pushed an undo checkpoint on pointer-*press*, so a bare click (no drag) left a no-op undo step and
  flipped `IsModified` — a freshly loaded document reported unsaved changes. The checkpoint is now taken
  on the drag's first actual movement.
- **HTML `font-weight` was read from the whole style string.** The check looked for `bold`/`:600`…
  anywhere in the declaration list, so `font-weight:normal;width:600px` parsed as bold. It now reads the
  declaration's own value and compares numerically (`>= 600`), which also covers values like 650.
- **An RTF picture inside a table row escaped its cell.** `\pict` data 64 px or larger was always spliced
  in as a document-level image block — mid-row that pushed the half-built cell paragraph into the document
  body, so a photo in a Word/HWP table came out beside the table with the surrounding text reordered.
  Pictures stay inline while a row is open.
- **Inline-table cells were never normalized.** Core rule #5 (every cell holds at least one paragraph)
  recursed into block-table cells only, so an inline-table cell whose only block is an image — as a
  deserialized `.flow` can contain — stayed paragraph-less and the caret could not enter it.
- **`LoadHtml` / `LoadHtmlAsync` / `InsertHtml` ignored `AllowRemoteImagesOnPaste`.** The flag reached the
  paste path only, so the documented privacy opt-out did not apply to loaded or inserted HTML.

- **A nested or inline table never showed cell-block selection.** `DrawNestedTable` didn't ask for the
  selected cell range, so selecting several of an inner table's cells — by drag, or with the staged
  Ctrl+A — painted no fill and looked like nothing had happened. It now applies the same chrome as a
  top-level table.
- **Undo/redo threw the caret to the start of the document when the edit was made at depth.**
  `UndoManager` identifies the caret by its index in document paragraph order, and both that walk and its
  inverse stopped at each cell's *first* paragraph — a leftover from the single-paragraph-cell era. A
  caret in a cell's 2nd+ paragraph (milestone A/P3), in a nested table (P4-2b) or in an inline table
  (milestone B) was therefore never numbered, so the lookup fell back to index 0 and undo dropped the
  caret at the top of the document instead of where the edit happened. Both walks now descend
  recursively through cells and inline tables, in the same order as `TextPointer.CompareTo` /
  `ParagraphsInBlocks` (anchor cells only).
- **Deleting a selection spanning two paragraphs of the SAME table cell left a stray paragraph break.**
  The merge was gated on "are both endpoints top-level blocks?", so two paragraphs of one cell — which
  cells have hosted since milestone A/P3 — were classified as "crosses the grid" and never joined; any
  blocks spanned between them were only blanked, not removed. The test is now "are they siblings in one
  block list?", which covers a cell exactly as it covers the document's top level. A selection that
  genuinely crosses cells still preserves the grid structure, as before.
- **Deleting a selection anchored inside a nested or inline table left the blocks between it and the
  other endpoint behind.** `TextRange.TopLevelBlockOf` (and its copy `RichEditor.FindTopLevelBlock`)
  scanned each cell's block list one level deep, so a paragraph in a nested/inline table resolved to
  "no top-level block" and the between-blocks removal was skipped entirely. Both now share one
  parent-chain walker, which resolves a paragraph at any depth. This also fixes the paste target and
  the clipboard block capture, which silently fell back to the document end from such a caret.
- **`GetPlainText()` dropped all text inside nested and inline tables** — it descended exactly one
  level (top-level paragraphs plus a cell's own paragraphs). The accessibility peer reads this method,
  so that content was invisible to assistive technology too. It now uses the recursive document order.
- **`GetImageCount()` under-counted images in nested and inline tables**, so the
  `RecommendedImageLimitExceeded` soft-limit warning fired late or not at all.
- **Paragraph commands ignored the selection and only changed the caret's paragraph.**
  `SetTextAlignment`, `SetHeading`, `SetLineSpacing`, `SetLineHeight`, `Indent` and `ToggleQuote` each
  poked `_caretPosition.Paragraph` directly, so selecting several paragraphs and clicking "center"
  aligned just the one the caret happened to land on — while the list commands on the same toolbar
  already applied to the whole selection. All six now go through one selection-aware choke point that
  reaches paragraphs at any depth (table cells and inline tables included). `ToggleQuote` takes its
  direction from the caret paragraph and applies it uniformly, so a mixed selection ends up consistent
  rather than inverted item by item (the same rule the list toggle uses).
- **Backspace at the very start of the document (or Delete at its end) left a no-op undo step** and
  flipped `IsModified`, so a freshly loaded document reported unsaved changes after one stray key
  press. The checkpoint is now taken only when there is an adjacent block to merge or remove —
  the guard `Ctrl+Backspace`/`Ctrl+Delete` already had.
- **One `Enter` pushed two identical undo checkpoints**, so the first `Ctrl+Z` after it appeared to do
  nothing (it restored an already-current state) and a second press was needed to undo the split. The
  redundant inner checkpoint is gone; Enter is one step, like every other structural edit.

- **The table cell block was painted but never operated on.** `SelectedCellRange` fed the renderer and
  the context menu and nothing else, so every edit/format command walked the linear text run between the
  drag's two endpoints instead. The two disagree in both directions: the linear run starts at the drag's
  *offset* inside the first cell (so a Delete left the text before it standing, and character formatting
  covered only part of the corner cells), and document order between two corners sweeps in cells that
  lie **outside** the painted rectangle (a vertical block in a 3-column table also caught the cells to
  its right). Delete, character formatting, paragraph formatting and the list commands now all consult
  the cell block first. Per Excel/HWP, `Delete` on a cell block clears the selected cells and leaves the
  grid standing — removing rows or columns stays an explicit menu action.
- **A single cell could not be selected as a unit.** `SelectedCellRange` returned null outright when both
  endpoints were in one cell, so the only way into cell-selection mode was a drag across two or more
  cells. A one-cell block is now a first-class selection, reachable from the new **Select Cell** context
  menu command (and by a single click once in cell-selection mode, which already worked).
- **An inline table inside a table cell could not be entered or drag-selected with the mouse.** The
  top-level paragraph hit-test descended into an inline table's cells, but `HitTestBlockList` — the walk
  for a cell's own contents — had no such descent, so a click stopped at the host paragraph's
  object-replacement character. The table rendered (the cell render walk does flush inline-table draws),
  it just wasn't reachable: the exact state you get by pasting a paragraph containing an inline table
  into a cell. Both walks now share one geometry helper (rule #1). The same gap in the *link* walks is
  fixed with it, so a hyperlink inside an inline table is now hoverable and clickable at any depth —
  previously it read as plain text everywhere, top level included.
- **A table row did not grow while the IME was composing in one of its cells.** The render walk splices
  the preedit text into the caret's paragraph, but the measure walk rebuilt that paragraph without it,
  so the row stayed sized for the text without the composition and the composed glyphs spilled past the
  cell's bottom border — on every wrap while typing Korean/Japanese/Chinese into a narrow cell. The cell
  measure now uses the same layout the renderer draws, and starting or ending a composition evicts the
  cached table geometry up the enclosing chain (a nested cell pushes its host row; an inline table
  re-shapes the paragraph line it sits in), since composition changes no model content and nothing else
  would invalidate it. Top-level paragraphs had the same measure gap with a milder symptom — the render
  walk does advance by the preedit height, so nothing overlapped, but the reported extent stayed a line
  short and a composition at the end of a document could not be scrolled to. `MeasureContentHeight` now
  applies the composition height there too; `BlockExtent` deliberately keeps handing the hit-tests and
  pagination the plain layout, whose indices are logical offsets rather than display positions.
- **Clicking the empty space to the right of a line put the caret one past the paragraph's end.**
  `HitTestIndex` passed Avalonia's `TextPosition + IsTrailing` straight through, and past the end of a
  line that is the paragraph's length + 1 — an offset that doesn't exist. Two everyday consequences:
  **Backspace deleted nothing** (the delete range fell outside every run), and **typing there started a
  new unformatted run**, so text typed after clicking to the right of a bold or coloured line came out
  unstyled. Reproduced regardless of script or line length. The offset is now clamped to the paragraph's
  real length in the one place every caret placement, drag selection, link and cell hit-test goes
  through.
- **Right-clicking inside an inline table offered no table operations.** The menu picked its target with
  `GetBlockAtPoint`, which only sees top-level blocks; an inline table's grid hangs off a paragraph's
  inlines, so the click fell through to the plain text menu with no row/column/merge/delete at all. The
  target is now resolved from the hit position (`ContextMenuTargetTable`), which reaches a table at any
  depth — the menu builder already handled inline tables, only the routing was missing.
- **Resizing an inline table's row did not reflow its host paragraph until the next edit.** An inline
  table is laid out inside the host paragraph's line box, and a resize drag mutates size without going
  through an edit, so the frame runs as a "trusted" pass that returns the cached paragraph layout
  without re-checking its signature. Only the table cache was evicted, so the paragraph kept its old
  line box until a click or keystroke forced a rebuild. Both resize handlers now evict the whole
  enclosing chain — the table, every table around it, and the host paragraph of any inline table on the
  way (measured: 80 → 80 before, 80 → 206 after). The IME composition path, which is stale for the same
  reason, now shares that helper.
- **Pressing Space with the caret on a table's right-hand side opened the gap in front of it.** Indent
  is the "space before a block" feature, but it fired from either side of the block caret, so the
  whitespace appeared on the table's far side, nowhere near the caret. Indent/outdent (Space, Tab,
  Shift+Tab) now belongs to the leading side only; on the trailing side the key types normally.
- **Typing with a block caret active inserted at the wrong place.** Dismissing the block caret left the
  text caret wherever it happened to be when the block caret was set, so a letter typed with the caret
  sitting *after* a table went back into that table's last cell, and one typed *before* a table landed
  in its first cell. The text caret now moves to the position the block caret stood for — the end of
  the paragraph before the block, or the start of the one after it.
- **A bare modifier press acted as a keystroke.** Pressing Shift on its own — the first half of
  Shift+Tab — dismissed the block caret, and with the caret-placement fix above that visibly moved the
  caret out from under the shortcut being typed. The same fall-through cancelled a selected image
  before its Ctrl+C could arrive. Modifier keys (Shift/Ctrl/Alt/Win, Caps/Num/Scroll Lock) are now
  ignored by the key handler, which acts on none of them.
- **Toolbar buttons stole focus, so the caret vanished and typing stopped.** The caret is only painted
  while the editor is focused, and nothing handed focus back after a click. The command itself still ran
  against the remembered caret position, which is why the buttons looked like they worked while the
  keyboard had gone dead — the outdent button made it obvious, but every button behaved this way. No
  toolbar button takes focus now. The combo boxes still do (their dropdowns need it) and give it back
  when the dropdown closes, rather than on every selection change, which would pull focus away while
  arrowing through an open list.
- **Typing never scrolled the caret back into view.** Every other edit reaches the bring-into-view
  request through `ResetCaretBlink`, but the typing path deliberately skips that call — it would end
  the undo coalescing run and give each keystroke its own checkpoint — so nothing asked the host to
  scroll. The caret walked off the bottom as text grew, most visibly inside a table cell, which expands
  downward as its content wraps. Typing now raises the request without disturbing the coalescing.
- **Shift+Tab outside a table typed four spaces.** It fell into the same branch as plain Tab, so the
  key did the opposite of what it means. It now undoes what Tab did — removing up to four spaces
  immediately before the caret — and falls back to outdenting the paragraph when there are none, so an
  indent set from the toolbar is still reachable from the keyboard.
- **Clicking while the IME was composing landed at the wrong offset.** The glyphs on screen include the
  preedit but the hit-test read the plain layout, so a click resolved to the position it would have had
  without the composition — drifting further the longer the composition grew, and clamping to the end of
  the paragraph once the click passed where the uncomposed text ended. The composed layout is now
  hit-tested and its display index mapped back to a logical offset (before the composition maps through,
  after it shifts back by its length, inside it resolves to its start). Inside a table cell the walk also
  advanced by the uncomposed height while the cell's rect grew with the composition, so clicks on the
  composition's own wrapped lines were attributed to the block below it.
- **Moving the caret now ends a cell block.** The mode survived arrow keys, so the cell stayed filled
  while the collapsed selection meant the edit commands acted on one character — the same paint versus
  operation mismatch, reached by pressing → after selecting a cell.

### Changed
- **↑/↓ now walk into a table's rows (HWP), instead of stopping beside it and then skipping it.**
  Arriving at a table from the paragraph above, ↓ enters its first row in the column under the caret;
  ↑ from below enters the last row. Inside, the arrows step row to row, and from the far row they leave
  to the neighbouring paragraph. Previously vertical navigation parked on the block caret next to the
  table and a second press jumped over the whole thing, so a table's cells could not be reached by
  keyboard at all without Tab, →, or a click. The block caret is unchanged and still reached with ←/→
  or by clicking the table's outer border — that is where indenting and deleting the table as a unit
  live. An **image** keeps the old behaviour: it has no text to enter, so ↑/↓ still stop on its block
  caret.
- **The synchronous `ParseHtml` / `LoadHtml` / `InsertHtml` no longer load remote images.** They used to
  download every `http(s)` `<img>` on the calling thread (a 5 s per-parse budget), so loading web content
  from the UI thread froze the app for up to that long — a hung UI is a worse failure than a missing
  image. The synchronous path now performs no network I/O at all; `data:` and `file:` images are
  unaffected. Use `ParseHtmlAsync` / `LoadHtmlAsync` (what paste already uses) to fetch remote images.

### Performance
- **`FindCell` no longer scans the document.** It resolved a paragraph's table cell by recursing through
  every table and inline table in the document; it now walks the `Paragraph → TableCell → TableBlock`
  parent chain (wired by `UpdateParents`). It runs per keystroke, per pointer move and per menu build.
- **`PruneLayoutCaches` no longer evicts live nested/inline tables' geometry.** It collected only
  top-level tables as "live", so past the cache cap every prune dropped the still-used entries for
  tables in cells and inline tables, forcing a full re-measure on the next frame.

## [0.9.0] - 2026-07-07

Backported the features the WinUI 3 port (WinUIRichEditor) had pulled ahead on. Includes a breaking
API removal (`EditorMode`) and a default-behavior change (`PageSize` now `Continuous`), so the minor
version is bumped (pre-1.0).

### Added
- **Per-document page setup.** `FlowDocument.PageSetup` (paper size, orientation, page boundaries,
  header/footer, page numbers) is serialized in JSON/`.flow` and applied to the editor on load; changing a
  page property captures it back into the document. A default (Continuous) or absent setup is omitted, so
  plain documents keep their exact bytes.
- **`IncreaseFontSize()` / `DecreaseFontSize()`** — step the caret's font size along the standard point ladder.
- **Central shortcut table (`RichEditorShortcuts`, Word-standard).** One source of truth shared by the key
  handler, the context-menu hints, and (via `Display`) the toolbar. New shortcuts: align `Ctrl+L/E/R/J`,
  headings `Ctrl+Alt+1..6`, body `Ctrl+Shift+N`, bullet list `Ctrl+Shift+L`, line spacing `Ctrl+1/5/2`,
  strikethrough `Ctrl+Shift+X`, font size `Ctrl+Shift+.`/`,`, indent `Ctrl+M`/`Ctrl+Shift+M`, redo `Ctrl+Shift+Z`.
- **`RichEditor.ShowFormattingMenu`** (default false) — a slim right-click menu (clipboard + quick B/I/U
  toggles + object menus); opt in for the full formatting groups when there's no toolbar.
- **`ToolbarLevel`** `{ Auto, Minimal, Normal, Maximum }` on `RichEditorToolbar` — a density knob. A read-only
  target now shows a *view toolbar* (page/zoom + Export/Print) instead of hiding the strip.
- **Toolbar-native page/zoom + file actions.** `RichEditorToolbar` builds the zoom·paper·orientation controls
  and Export/Import/Print itself (`ShowPageControls`/`ShowFileActions`/`PrintRequested`), so a standalone
  toolbar carries them; `RichEditorView` delegates to these instead of injecting its own chrome.

### Changed
- **Default `PageSize` is now `Continuous`** (was `A4`), unified with the WinUI port — the editor reflows to
  width out of the box; choose a concrete paper size for page view.
- **Right-click menu reorganized HWP-style**: character/paragraph/list/heading groups, alignment as a radio
  group, list/heading promoted to the top level with current-state radios/checks, shortcut hints on items;
  object menus regrouped edit → shape → file → delete.

### Removed
- **`EditorMode` enum (breaking).** Capability is expressed directly through `IsReadOnly` + the `Allow*`
  flags — a "viewer" is `IsReadOnly = true` plus a minimal/no toolbar, not a preset.

## [0.8.0] - 2026-06-20

### Added
- **Inline tables — HWP-style "treat as character" (Milestone B).** A table can now flow inside a text
  line as a single character (`InlineTable`, occupying one `U+FFFC` position like an inline image) while
  staying fully editable. Built on Milestone A's recursive primitives.
  - **Full editing**: click into a cell, type (the host line grows to fit), select, navigate. Arrow keys
    enter the table at its character boundary, traverse its cells, and exit back to the host text on the
    far side; Tab/Shift+Tab traverse the cells; resize handles and the cell right-click menu work as for
    block tables.
  - **Treat-as-character toggle**: a top-level block table ⇄ inline table via the right-click "treat as
    character" check item (mirrors the inline-image toggle).
  - **`InsertInlineTable(rows, cols)`** public API inserts an inline table at the caret.
  - Serializes through JSON / `.flow` (recursive table DTO) and HTML (`<table>`, best-effort since HTML has
    no inline-table concept); in-app copy/paste preserves it.
- **Draw-to-size table insertion.** Picking rows × columns from the insert-table grid (context menu or
  toolbar) now arms a draw mode: drag on the document to create the table at that size (a plain click
  falls back to the default size; Esc/right-click cancels).
- **Blocks inside table cells (Milestone A).** A table cell is now a block list (`TableCell.Blocks`)
  instead of a single paragraph, so a cell can hold multiple paragraphs, block images, dividers, and
  **nested tables** to arbitrary depth. Render, hit-testing and measurement share one recursive
  primitive (`DrawCellBlockList`/`HitTestBlockList` ↔ `LayoutTable`/`MeasureCellContentHeight`).
  - **Enter inside a cell** splits into a sibling paragraph (the table grows to fit) instead of inserting
    a hard `\n`. Shift+Enter still inserts a soft line break.
  - **Insert table/image/divider** with the caret in a cell now nests the block inside that cell; a
    nested table's columns are sized to fit the cell width.
  - Arrow keys, click placement, selection, and hyperlinks all descend into nested cells. Crossing a
    nested-table boundary uses paragraph-order navigation (block carets remain top-level only).
  - **Tab/Shift+Tab** traverse every cell in document order, entering and exiting nested tables; Tab past
    the document's last cell adds a row to the top-level table.
  - Block images inside a cell get the same chrome as top-level ones: click to select, drag the corner to
    resize (clamped to the cell), Delete/Backspace to remove, and a right-click image menu. Nested tables
    get resize handles (the outer-right edge clamped to the cell) and a right-click menu that targets the
    innermost table.

### Changed
- **HTML table export/import fidelity.** Table cells now export their full block content (nested tables,
  block images, multiple paragraphs) and import it back as blocks (previously cells were parsed as inline
  text only, so nested tables were dropped on load). Per-column widths round-trip via a `<colgroup>`.
- The editor surface fills the scroll viewport (`RichEditorView`), so clicking — or drawing a table — in
  the empty area below short content works.
- **Document format**: multi-block / non-paragraph cells serialize as a `Type:"Cell"` wrapper carrying a
  recursive `Blocks` list; a plain one-paragraph cell still serializes in the legacy single-paragraph form
  (with the cell background on it), so older readers keep loading these documents. See
  [`docs/DOCUMENT_FORMAT.md`](docs/DOCUMENT_FORMAT.md).

### Fixed
- HTML/`.htm` files failed to import (parsed as JSON → empty document); the importer now detects HTML.
- **Copy inside a table cell** captured the whole table (paste reproduced the entire table); a selection
  within a single cell now copies just the cell content.
- **Paste with the caret in a cell** landed after the table instead of in the cell; multi-block paste now
  splits the caret paragraph and inserts at the caret (in cells and at the top level alike).
- **Right-click inside a cell** showed only the table-structure menu; it now shows the same text menu as a
  top-level paragraph, with row/column/merge operations under a "Table" submenu.

## [0.7.1] - 2026-06-18

A patch release: bug fixes (mostly fallout from the 0.7.0 points migration) and idle/input hot-path
allocation cleanups. No public API or document-format changes.

### Fixed
- **HTML import font size**: text without an explicit `font-size` (notably every table cell, and
  inline-wrapped top-level text like a bare `<span>`) was imported at a stale **14pt** — a leftover from
  the px-era default before the points migration — instead of the **10pt** body default, so a pasted
  table read larger than the surrounding text. It now matches the rest of the document at 10pt.
- **RTF round-trip dropped astral characters** (emoji and other non-BMP text): the writer emits a
  surrogate pair as two `\u` units, but the reader decoded each via `ConvertFromUtf32`, which throws on a
  lone surrogate and silently dropped the character. The reader now appends each `\u` as a raw UTF-16
  code unit so the halves recombine.
- **Inline-image serialization** no longer risks a `NaN` width/height reaching `System.Text.Json` (which
  rejects raw `NaN`): inline images now go through the same `NaN → null` guard as block images and read
  back at the default size.
- **Accessibility `IValueProvider.SetValue`** preserved no line breaks — a multi-line value collapsed
  into one paragraph. Each line is now its own paragraph, round-tripping with `GetPlainText`.
- **`Ctrl+Delete`/`Ctrl+Backspace` (delete word)** no longer pushes an empty undo checkpoint when there
  is nothing to delete at a paragraph boundary.

### Performance
- The blinking text caret no longer allocates a `Pen` every repaint (cached, rebuilt only when
  `CaretBrush` changes), matching the other render pens.
- Drawing the selection highlight finds both selection endpoints in a single pass instead of two
  `IndexOf` scans of the paragraph list (runs every render frame a selection exists — once per visible
  page in page view).
- `GetStatus` (status-bar char/word/line/column, recomputed on every caret move) walks the inlines
  directly instead of building a per-paragraph string, removing the per-keystroke string allocations on
  large documents.
- Cursor hover detects the table-select border in one document walk instead of two (it previously called
  `GetBlockAtPoint` and then re-walked via `GetTableRect`).

## [0.7.0] - 2026-06-18

The first release to **drop the pre-release suffix** (alpha/beta) — on the 0.x line the API may still
evolve before 1.0, but the editor is feature-complete and usable for general work. Headlines: a
**breaking** switch to point-based font sizes, proportional line spacing, bullet/number marker styles,
RTF export, a SemVer document-format version (`"1.0"`), and a reworked, combo-consistent toolbar.

### Added
- **Bullet and number list styles** (`Paragraph.ListMarker` / `RichEditor.SetListStyle(ListMarkerStyle)`):
  bullets can be a disc (•), circle (◦), square (▪) or dash (–), and numbered lists can use `1.`, `1)`,
  `a)`, `A)` or `i)`. Each list button on the toolbar gains a ▾ dropdown to pick the marker, and the
  right-click List menu adds Bullet Style / Number Style submenus. Round-trips through JSON; HTML maps to
  the closest `list-style-type` and RTF emits the literal marker (the `)` suffix and dash bullet have no
  equivalent there — lossy by design). The bullet/numbered-list toolbar buttons also get vector icons.
- **Proportional line spacing** (`Paragraph.LineSpacing` / `RichEditor.SetLineSpacing(double)`): line
  spacing as a multiple of the natural single-line height (1.0 = single, 1.5 = 1.5 lines, 2.0 = double —
  i.e. HWP's % ÷ 100 or Word's "Multiple"), which **scales with font size**. The toolbar's line-spacing
  dropdown now shows HWP-style percentages (100–300%). The existing `Paragraph.LineHeight` is retained as
  an absolute-pixel ("exactly") value; `LineSpacing` takes priority when both are set. Round-trips through
  JSON. (Also corrects `LineHeight`'s XML doc, which described a multiplier the field never implemented.)
- **RTF export** (`RichEditor.ToRtf()` / `RtfDocumentFormatter.Write`), making RTF symmetric with the
  existing import: the document saves as Rich Text Format readable by Word, WordPad, LibreOffice, and
  HWP. Covers paragraphs, runs (bold/italic/underline/strike, size, colour, font family), alignment and
  indent, headings (exported at their displayed size/weight), lists (as markers), tables, and embedded
  PNG/JPEG images; non-ASCII text is emitted as `\u` escapes so the output is code-page independent.
- **`RichEditor.LoadRtf(string)`** loads a document from RTF, and the bundled `RichEditorView`'s
  Export/Import buttons now offer `.rtf` alongside JSON/`.flow`/HTML (import sniffs the `{\rtf` header).
  Round-trip (write → parse) preserves text, character formatting, colour, tables, images, and Unicode.
- **More paragraph styles**: the paragraph-style dropdown now offers **Heading 1–6** (was 1–3), and a
  new **Quote** toggle (`RichEditor.ToggleQuote()`) applies blockquote styling, available in the
  right-click List submenu. `CaretFormat` gains a `Quote` flag so hosts can reflect the quote state.
- **Justify alignment**: text can now be justified (both edges flush) via the toolbar alignment
  dropdown and the right-click Alignment submenu. Round-trips through JSON, HTML (`text-align:justify`),
  and RTF (`\qj`). A real-Skia render test confirms Avalonia stretches non-last lines to the margin.

### Changed
- **Document format version is now a SemVer string, starting at `"1.0"`** (was an incrementing integer,
  last `2`). It marks the stable baseline (image pool + pt font sizes + proportional line spacing) and is
  tracked independently of the NuGet package version. The reader accepts both the new string and the
  legacy integer forms, so older files still load. The `.flow` package now also carries a `meta.json`
  container marker (`{"format":"flow","version":"1.0"}`) so the container layout can version separately;
  readers tolerate its absence. `DocumentSerializer.CurrentSchemaVersion` changes type `int` → `string`.
- **BREAKING — font sizes are now points (pt), not pixels.** `Run.FontSize`, `RichEditor.DefaultFontSize`,
  `CaretFormat.FontSize`, and the JSON/`.flow`/HTML/RTF serializers all carry **pt**; the value is
  converted to device-independent pixels (×4/3 at 96 DPI) only at the render boundary. The body default
  is now **10pt** (was 14px) and the toolbar size list is in pt (8–72). Headings render at a pt ladder
  (h1–h6 = 20/16/14/12/11/10). No runtime migration — old px-valued files are simply reinterpreted as pt
  (and appear ~33% larger); the document-format version (see above) marks the pt baseline. See
  `docs/DOCUMENT_FORMAT.md` (`FontSize` field + schema notes).
- **Image context menu**: the size presets (Original / 1/2 / 1/3 / 1/4) now live in a single **Size**
  submenu instead of cluttering the top level, and the fractions now scale the **current displayed
  size** (so they compound) rather than always restarting from the natural size. "Original" still
  resets to the natural size. Applies to both block and inline images.
- **Left-clicking a block image now selects it** (blue border), matching right-click and inline-image
  behaviour, so it can be deleted with Delete/Backspace without first opening the context menu.
- **Toolbar UI refresh**: the line-spacing control and the bullet/numbered-list controls are now
  combo-style bordered boxes — `[icon | current value/marker | ▾ menu]` — matching the other combos in
  border, height (28px) and dropdown placement (menus open below the box like a ComboBox). The line-spacing
  box adds an editable `%` field (type a value, or step ±10% with ▲▼); the list boxes show the caret
  paragraph's current marker live and dim it when the list is inactive. Built-in vector icons are larger
  (16→20px) with lighter strokes, and the rarely-used Format Painter toolbar button was removed (the
  `RichEditor.StartFormatPainter()` API is unchanged). `CaretFormat` gains `LineSpacing` and `ListMarker`
  fields so the toolbar can reflect the caret paragraph's spacing and list marker.

### Fixed
- **Caret and selection geometry at large font sizes / line spacing.** The caret no longer drops below
  (or rises above) the glyph on lines expanded by line spacing — it now centres within the line box, the
  way Avalonia positions the text (inline-image lines still baseline-align). And changing the font size of
  a selection now updates the highlight in the same frame instead of a frame late (`ApplyStyleToSelection`
  was invalidating only the visual, not the measure, leaving the layout stale until the next interaction).

## [0.6.0-beta] - 2026-06-16

First **beta**. The public API is stabilizing (the whole surface is tracked by the PublicAPI analyzer;
this release promotes `TextRange.GetRichInlines`), and the geometry-worker unification — render, measure,
hit-testing, and pagination now derive every block's position/height/layout from one shared source —
closes the last structural drift-risk. Headlines: rich-HTML copy export, a heading-formatting data-loss
fix, live (no-snap) table resize, and image-decode hardening. The remaining gates before `1.0` are
verification depth (render pixel tests, cross-platform functional checks, large-document performance),
not features.

### Added
- **Copy now exports rich HTML.** Copying a selection puts Windows `CF_HTML` ("HTML Format") on the
  clipboard alongside the plain text, so pasting into Word, browsers, or other rich editors preserves
  formatting — making copy/paste symmetric with the existing HTML *import*. The HTML is built from a
  trimmed sub-document that keeps paragraph properties (lists, headings, alignment, indent), tables, and
  inline images, so bullets/numbers, headings, table grids, and pasted-back pictures survive.
  For maximum consumer compatibility the markup uses double-quoted attributes, quoted font-family names,
  `pt` font sizes, `<s>`/`<u>` tags, and explicit `list-style-type`. Note: Word and HWP honour different
  subsets of clipboard CSS, so exact font/size/colour fidelity varies by target app — a documented limit.

### Changed
- **`GetPlainText()` and copied plain text now use the platform newline** (CRLF on Windows) instead of a
  bare `\n`, so extracted/copied text shows real line breaks in files and native text controls rather
  than running together on one line. Soft line breaks inside a paragraph are normalized too.
- **List markers follow the item's own text style.** Bullets/numbers now take the first run's font
  size, family, weight, and colour instead of a fixed 14 px black default, so a heading or coloured
  list item gets a matching marker.
- **Page-outline view uses thinner grey margins.** The desk gap above/between pages and on the sides
  shrank from 24 px to ~2 pt (`PageGap`), so pages sit close together with just a sliver of desk instead
  of a wide grey band; the fit-to-width side margin now follows the same constant.
- **Headings are a render-time style, not baked into runs.** Setting a heading level now styles the
  paragraph (larger + bold) at layout time and leaves each run's own font size untouched, so toggling a
  heading on and back to body text no longer overwrites manually-set sizes. (`GetCaretFormat` / the
  toolbar report a heading run's underlying size, not the displayed heading size.)

### Fixed
- **Heading toggle no longer loses formatting**: applying a heading and switching back to body text used
  to flatten every run to 14 px / normal weight, discarding manually-set sizes — see the render-time
  heading change above.
- **A decode-failing image is no longer dropped on save.** When a picture's encoded bytes can't be
  decoded on this platform, the bytes are now kept (the format may decode elsewhere) instead of being
  cleared on first render, so a later save still round-trips the image. The decode just isn't retried.
- **Table/cell resize is now live.** Dragging a column or row border (or a cell handle) resizes the
  table continuously during the drag instead of snapping to the new size only after the mouse stops.
- **`InsertHtml` honours `IsReadOnly`** (no-ops on a read-only editor), matching the other mutating commands.
- `GetPlainText()` no longer drops a leading blank line (an empty first paragraph now contributes its
  separator).
- Pasted text with `\r\n` line endings no longer leaves stray `\r` characters in runs (normalized to
  the model's `\n` on insert).
- HTML export now escapes `"`/`<`/`>`/`&` in `NavigateUri` and `FontFamily` attribute values, so a
  quote in a URL or font name can't break the emitted (double-quoted) markup.
- `TableBlock.InsertColumn` keeps column widths aligned with columns when the width list was shorter
  than the column count; JSON table load pads jagged/short rows so the grid stays rectangular.

### Performance
- Caret moves no longer re-hash every paragraph: `MeasureOverride` trusts the layout cache when no edit
  is pending, and drag-selection hit-testing trusts it too (previously each mouse-move/arrow-key
  re-fingerprinted the whole document).
- `GetStatus()` computes character/word/line/column in a single pass without building a whole-document
  string or a `string.Split` array.
- Find/Replace stops at the first qualifying match instead of materializing every match in the document.
- Table geometry is reused across page-view passes (same column widths and measured row heights at a
  different vertical offset no longer re-measure every cell).
- **Adjacent same-format runs coalesce** after merges, deletes, and style toggles, so the run list no
  longer fragments over an editing session (cheaper layout fingerprinting, less memory).
- **`TextPointer.CompareTo`** compares two positions in a single early-exiting document pass (was two
  full traversals per comparison).
- **One geometry source**: render, measure, hit-testing, and pagination now all derive each block's
  top/height/layout from a single shared pass (`BlockExtent`) instead of duplicating the per-block height
  math, eliminating a class of caret / hit-test / page-break drift bugs.

### Accessibility
- The automation peer now reports when the editor's read-only state toggles. (Caret/selection exposure
  still isn't possible — Avalonia's public automation model has no `ITextProvider`.)

## [0.5.0-alpha] - 2026-06-14

A self-contained `RichEditorView` (built-in page/zoom/file-action toolbar + status bar), a Word-style
table-size picker in the context menu, font-combo and context-menu font fixes, and idle-render
performance work — bundled with the page-layout redesign.

### Added
- **`RichEditorView` is now a complete drop-in view**: its toolbar carries built-in page controls
  (paper size, orientation, page outline) and a zoom combo, plus a bottom **status bar** (character/word
  counts, caret line/column, page count, image-limit warning). Toggle via `ShowStatusBar` /
  `ShowFileActions`.
- **`FitToWidth`** (default on) auto-scales the document to fill the viewport width — no horizontal
  scrollbar — recomputing on resize and paper/orientation/outline changes. An explicit `ZoomFactor`
  (or Ctrl+wheel / Ctrl+`+`/`-`) turns it off; Ctrl+`0` restores fit.
- **Built-in Export / Import / Print toolbar buttons.** Export/Import use the platform file picker
  (JSON / `.flow` / HTML); Print is delegated to the host via the new **`PrintRequested`** event, so the
  library keeps no platform print dependency. New `RichEditorIcon.Export` / `Import` / `Print` icon
  slots with built-in vector glyphs.
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
- **Package file extension `.ardx` → `.flow`** (a shorter, more memorable name evoking the
  `FlowDocument` model; the ZIP package format itself is unchanged, and the stream-based
  `DocumentPackage` / `SavePackageAsync` API is untouched).
- **`RichEditorView` defaults** to A4 + no page outline + fit-to-width (a bare flowing column).
- **Font-family combo** now renders each font name in its own typeface (in both the dropdown and the
  selected display), scoped to the combo so the rest of the toolbar is unaffected.
- **Context-menu "Insert Table"** is now a drag-to-size grid picker (matching the toolbar), replacing
  the fixed 2×2 item.
- **Performance**: passes that cannot have changed content (caret blink, scroll, pointer hover) reuse
  the cached paragraph and table layouts without re-hashing every paragraph's text, and stale cache
  entries for deleted paragraphs/tables are pruned — removing full-document work from the idle blink
  and from mouse-move hit-testing.

### Removed
- **`PageView`** (property + `PageViewProperty`). Replaced by `PageSize`/`ShowPageBoundaries`.

### Fixed
- Page-view hit-testing used a hardcoded A4 page height, so clicks/caret landed wrong for non-A4
  paper sizes; it now uses the selected paper's height.
- The right-click context menu now uses a stable UI font instead of inheriting (and drifting with)
  the editor's selected font.

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

[Unreleased]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.7.1...HEAD
[0.7.1]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.6.0-beta...v0.7.0
[0.6.0-beta]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.5.0-alpha...v0.6.0-beta
[0.5.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.4.0-alpha...v0.5.0-alpha
[0.4.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.3.0-alpha...v0.4.0-alpha
[0.3.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.2.0-alpha...v0.3.0-alpha
[0.2.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/compare/v0.1.0-alpha...v0.2.0-alpha
[0.1.0-alpha]: https://github.com/centwon/AvaloniaRichEditor/releases/tag/v0.1.0-alpha
