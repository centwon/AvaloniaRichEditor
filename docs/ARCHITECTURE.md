# 구조 (파일 지도)

`CLAUDE.md`에서 옮긴 상세 지도(2026-10-17). 지키는 규칙은 `CLAUDE.md`의 "비자명한 핵심 규칙"이 단일 출처다 — 여기는 어디에 무엇이 있는지만.

## 라이브러리 `src/AvaloniaRichEditor/`

네임스페이스 `AvaloniaRichEditor.*`, XAML 없음 — 전부 코드 컨트롤.

- `Documents/` — 문서 모델. `FlowDocument.Blocks`(`AvaloniaList<Block>`)에 `Paragraph`/`TableBlock`/`ImageBlock`/`DividerBlock`이
  **형제(sibling)**로 들어간다.
  - `Paragraph.Inlines`: `Run`(텍스트), `InlineImage`(인라인 그림), `InlineTable`(인라인 표).
  - `TableCell.Blocks`: 셀도 블록 리스트(문단·그림·구분선·중첩 표).
  - `TextPointer`(Paragraph+Offset), `TextRange`(삭제/복사/서식/구조). `PageSetup`·`PageMargins`는 문서가 지니는 쪽 설정.
- `Controls/RichEditor.cs` — 핵심 컨트롤(`partial`). 본체는 속성/이벤트·undo·편집 코어·`BuildTextLayout` 레이아웃 캐시. 부분 클래스:

  | 파일 | 맡는 것 |
  |---|---|
  | `Input` | 포인터·키보드·IME·블록 횡단 캐럿 |
  | `HitTesting` | `GetPositionFromPoint` 등 + `LayoutTable`(렌더·히트테스트·측정이 공유하는 표 기하) |
  | `Rendering` | Measure/Render/AutomationPeer |
  | `Formatting` | 서식 명령·리스트·하이퍼링크·포맷 페인터 |
  | `DocumentApi` | HTML/JSON/RTF/`.flow` 입출력, `NotifyDocumentChanged` |
  | `Tables` | 셀 탐색·Tab·행/열 연산 |
  | `Images` | 크기/교체/블록↔인라인 변환 |
  | `DragBlock` | 표·그림 끌어 옮기기 |
  | `Clipboard` · `IncomingContent` | 붙여넣기·드래그드롭, 들어오는 내용 정리 |
  | `FindReplace` · `ContextMenu` | 찾기/바꾸기, 우클릭 메뉴 |
  | `Modes` | `IsReadOnly`·`Allow*` 기능 플래그·그림 소프트 제한 |
  | `Pagination` | 쪽 나눔·용지·`ViewMode`·`FlowDocument.PageSetup` 동기화·인쇄·벡터 PDF(`SKDocument`) |

  내부 헬퍼: `UndoManager`(타이핑 코얼레싱), `ImageDisplayCache`, `InputDialog`, `PrintPageVisual`, `SystemFontInfo`.
  `RichEditorShortcuts` — Word 표준 단축키 표(키 처리·메뉴 힌트·툴바 툴팁의 단일 출처, 공개).
  접근성 `RichEditorAutomationPeer`(IValueProvider). 아이콘 훅 `RichEditorIcons`(전역 Provider)·`ToolbarIcons`.
- 툴바 `RichEditorToolbar`(+`.PageFile`: `ToolbarLevel` 밀도·쪽/확대·Export/Import/Print, 확대는 호스트 훅).
  드롭인 `RichEditorView`(+`.FindBar`) — 툴바+찾기 막대+스크롤+상태 표시줄, 쪽 보기로 연다.
- `Formatters/` — `DocumentSerializer`(JSON), `HtmlDocumentFormatter`(HTML 입출력), `RtfDocumentFormatter`(RTF),
  `DocumentPackage`(`.flow` zip), `PdfFontSubsetter`(벡터 PDF용 HarfBuzz `hb_subset`), `PdfWriter`(래스터 PDF — 벡터가 안 될 때).

## 데모 `samples/AvaloniaRichEditor.Demo/`

네임스페이스 `AvaloniaRichEditor.Demo.*`. 창은 `RichEditorView` 하나(`Views/MainWindow`).
`RoundTripHarness`(`--roundtrip` HTML 왕복)·`BenchHarness`(`--bench`/`--bench-text`/`--bench-table` 성능 실측)는 개발 도구라
라이브러리가 아니라 여기 있다. `NativeEditor`(웹 에디터 호환 래퍼)는 데모 전용.

## 테스트

- `tests/AvaloniaRichEditor.Tests/`(xUnit v3) — 모델/포매터(일반) + 컨트롤(Avalonia.Headless, `[AvaloniaFact]`, 병렬화 off).
  실입력 구동은 `InteractionHost`.
- `tests/AvaloniaRichEditor.Tests.Render/` — 픽셀·메모리 검증(real Skia). 헤드리스 `Bitmap`은 디코드하지 않는 가짜라 그림 메모리는 여기서 잰다.

## 상호운용 재현 도구 `tools/rtfgen/`

솔루션에 없다(빌드·CI 대상 아님, 손으로 돌리는 개발 도구). Word·HWP·브라우저가 우리 출력을 어떻게 보는지 재현한다.
사용법·함정은 그 폴더의 [`CLAUDE.md`](../tools/rtfgen/CLAUDE.md)와 [`README.md`](../tools/rtfgen/README.md).
