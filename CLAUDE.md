# CLAUDE.md

WPF의 `RichTextBox`/`FlowDocument`를 **순수 C# + Avalonia `TextLayout`**로 바닥부터 이식하는 프로젝트.
PTS(비관리형 C++) 엔진을 못 쓰므로 렌더링/레이아웃/히트테스트를 직접 구현한다.

- **현재 진행 상황과 보류 항목은 항상 [`Project_Roadmap.md`](Project_Roadmap.md)를 먼저 확인**할 것. 작업 후 이 파일을 갱신한다.
- **1.x 유지보수 규칙**(2026-10-17): 1.3.0 뒤로는 **보안·데이터 손실·크래시 신고만** 고친다. 감사·외부 평가 검토·리팩토링·
  새 기능·새 공개 API는 먼저 벌이지 않는다 — 사용자가 신고가 아닌 일을 꺼내면 이 규칙을 먼저 짚는다.

## 기술 기반 — 함정만

> 대상 프레임워크·Avalonia 버전·의존성은 `.csproj`가 단일 출처다. 여기 옮겨 적지 말 것.

- 클립보드는 Avalonia 12 신 API: `IClipboard.TryGetDataAsync()` → `IAsyncDataTransfer`/`IAsyncDataTransferItem.TryGetRawAsync(DataFormat)`.
  구 `GetFormatsAsync/GetDataAsync`는 없다.
- 클립보드 HTML은 Windows **CF_HTML**(헤더 제거 필요). 워드 그림은 **VML**이라 미지원.
- JSON 직렬화는 AOT 대비 Source-Generated 컨텍스트(`DocumentJsonContext`).

## 빌드 / 실행

```
dotnet build AvaloniaRichEditor.slnx
dotnet run --project samples/AvaloniaRichEditor.Demo/AvaloniaRichEditor.Demo.csproj
```

- ⚠️ **실행 중인 앱이 exe를 잠근다.** 재빌드 전 반드시 종료:
  `Get-Process -Name "AvaloniaRichEditor.Demo" -ErrorAction SilentlyContinue | Stop-Process -Force`
  (빌드 에러 MSB3027/잠김 메시지가 나오면 컴파일은 성공했고 복사만 실패한 것.)
- GUI 동작 검증은 직접 못 하므로, 백그라운드로 `dotnet run` 후 사용자에게 테스트를 요청한다.
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## 구조

- `src/AvaloniaRichEditor/` — 라이브러리(NuGet 배포 대상, XAML 없음): `Documents/`(모델) · `Controls/`(`RichEditor` partial 클래스들,
  툴바, `RichEditorView`) · `Formatters/`(JSON·HTML·RTF·`.flow`·PDF).
- `samples/AvaloniaRichEditor.Demo/` — 데모 앱(`RichEditorView` 하나) + 개발 하네스(`--roundtrip`, `--bench*`).
- `tests/AvaloniaRichEditor.Tests/`(헤드리스, 병렬화 off) · `tests/AvaloniaRichEditor.Tests.Render/`(real Skia 픽셀·메모리).
- `tools/rtfgen/` — **솔루션에 없다**(Word·HWP·브라우저 상호운용 재현 도구, 사용법은 그 폴더의 `CLAUDE.md`).
- 파일별 상세 지도는 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## 비자명한 핵심 규칙 (꼭 지킬 것)

1. **단일 `TextLayout`가 진실의 원천**: 렌더·커서·히트테스트·선택영역은 모두 `BuildTextLayout`(per-run `ITextSource`)에서 나온 하나의 `TextLayout`을 쓴다. `FormattedText`로 폭을 따로 재면 글자 단위 off-by-one이 생긴다. (Avalonia 12 `FormattedText`엔 `HitTestPoint`/`HitTestTextPosition` 없음 — `TextLayout`에 있음.)
2. **오프셋 모델 = "이미지는 1글자"**: `InlineImage`는 논리적으로 1글자(`U+FFFC` placeholder, `ImageTextRun : DrawableTextRun`로 그림). 길이/오프셋 계산은 전부 `InlineLen()`/`BuildPlain()`을 쓴다. 새 오프셋 로직 추가 시 `Run.Text.Length`만 더하지 말 것.
3. **Enter = 새 `Paragraph`**: Enter는 커서 위치에서 문단을 분할해 새 Paragraph를 만든다(`SplitParagraphAtCaret`, 컨테이너 일반화 — `Document.Blocks` 또는 `TableCell.Blocks`). 새 문단은 리스트/들여쓰기/정렬/배경을 물려받고 제목 레벨은 본문(0)으로. **표 셀도 sibling 문단을 가진다**(마일스톤 A/P3): `TableCell.Blocks`가 블록 리스트라 셀 안 Enter도 문단 분할이며, 셀이 커지면 표 행 높이가 재계산된다(`InvalidateMeasure`). Shift+Enter는 최상위·셀 공통으로 한 문단 안 소프트 `\n`. (로드 문서·붙여넣기로 들어온 `\n`도 한 문단 여러 줄로 렌더되며 줄마다 처리되는 곳들이 이를 지원.)
4. **블록 vs 인라인 이미지 / 셀=재귀 컨테이너**: 큰 이미지=`ImageBlock`(독립 블록), 작은 아이콘(<64px)=`InlineImage`(문단 내). 표/큰 이미지에는 텍스트를 직접 넣을 수 없다 — "표앞/표뒤"는 인접 문단의 끝/시작이다. **셀은 블록 리스트**라 렌더(`DrawCellBlockList`↔`DrawNestedTable`)·히트테스트(`HitTestBlockList`)·측정(`MeasureCellContentHeight`)이 `LayoutTable`과 임의 깊이로 상호 재귀한다. 블록 캐럿(`_caretBlock`)은 top-level 블록 전용 — 중첩 표 경계는 평면 문단 순서(`ParagraphsInBlocks`)·기하 이동으로 넘는다.
5. **`NormalizeBlocks`**: 문서 처음/끝과 "연속된 비문단 블록 사이"에만 문단을 보장(빈 줄 강제 안 함). 캐럿이 모든 블록 앞뒤에 닿게 하는 장치. `UpdateParents`에서 호출됨.
6. **선택/삭제**: 텍스트는 `TextRange`; 큰 이미지/표는 클릭하면 `_selectedBlock` 선택(파란 테두리) 후 Delete/Backspace. 표는 단일클릭=전체선택, 더블클릭=셀 편집.
7. **클립보드**: 붙여넣기 우선순위 = 내부 리치(블록 구조 보존) → 외부 HTML(`ParseHtml`, 재귀 순회) → 평문. 워드 그림은 VML이라 미지원.
8. **문서 모델 객체는 UI 스레드에서만 생성**: 모델에 들어가는 `SolidColorBrush`/`TextDecoration` 등은 Avalonia 스레드 친화 객체라, `Task.Run` 안에서 만들면 첫 렌더에서 컴포지터가 크래시한다("calling thread cannot access this object"). 비동기 로더는 파싱(DTO+바이트, `ParseJson`/`ReadPackage`)까지만 백그라운드, `FromDto`(모델 생성)는 UI 스레드에서. 직렬화기가 만드는 브러시는 `ImmutableSolidColorBrush`(친화성 없음) 사용.

## 일하는 방식 (Karpathy 지침 압축 — 출처: https://americanopeople.tistory.com/514)

- **구현 전 사고**: 가정을 밝히고, 해석이 갈리면 고르지 말고 묻는다. 더 단순한 길이 있으면 제안한다.
- **단순성 우선**: 문제를 푸는 최소한의 코드. 요청 안 된 기능·유연성·일어날 수 없는 경우의 처리는 넣지 않는다.
- **정밀한 수정**: 변경된 모든 줄이 요청과 직접 이어져야 한다. 주변 코드·주석·포맷을 손보지 않고, 무관한 데드 코드는 보고만 한다.
- **목표 중심 실행**: 버그는 재현 테스트(빨강)부터 → 수정 → **수정을 되돌려 다시 빨강인지 반증**. 다단계 작업은 단계마다 검증 기준을 둔다.
