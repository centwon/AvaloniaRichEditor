# CLAUDE.md

WPF의 `RichTextBox`/`FlowDocument`를 **순수 C# + Avalonia `TextLayout`**로 바닥부터 이식하는 프로젝트.
PTS(비관리형 C++) 엔진을 못 쓰므로 렌더링/레이아웃/히트테스트를 직접 구현한다.

- **현재 진행 상황과 보류 항목은 항상 [`Project_Roadmap.md`](Project_Roadmap.md)를 먼저 확인**할 것. 작업 후 이 파일을 갱신한다.

## 기술 기반 (Tech Stack)

- **런타임/언어**: .NET 10, C# (`Nullable` enable). 출력 `WinExe`(Windows 데스크톱), `PublishAot`(Native AOT), 컴파일된 바인딩 기본(`AvaloniaUseCompiledBindingsByDefault`).
- **UI 프레임워크**: Avalonia **12.0.1** — `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`. 디버그 전용 `AvaloniaUI.DiagnosticsSupport`.
- **NuGet 의존성**:
  | 패키지 | 버전 | 용도 |
  |---|---|---|
  | Avalonia(.Desktop/.Themes.Fluent/.Fonts.Inter) | 12.0.1 | UI·렌더링·텍스트 레이아웃 |
  | CommunityToolkit.Mvvm | 8.4.1 | ViewModel(`ViewModelBase` 등) |
  | HtmlAgilityPack | 1.12.4 | 외부 HTML 붙여넣기 파싱(`HtmlDocumentFormatter`) |

- **핵심 Avalonia API 사용처** (직접 구현 엔진이라 의존도가 높음):
  - `Avalonia.Media.TextFormatting`: `TextLayout`(+ `ITextSource`), `TextCharacters`, `DrawableTextRun`(인라인 이미지), `GenericTextRunProperties`/`GenericTextParagraphProperties`. → 렌더·커서·히트테스트·선택의 단일 출처.
  - 렌더링: `Control.Render(DrawingContext)` 직접 오버라이드. `DrawingContext.DrawText/DrawImage/DrawRectangle/FillRectangle`.
  - 입력: `OnPointerPressed/Moved/Released`, `OnKeyDown`, `OnTextInput`. IME: `TextInputMethodClient` + `TextInputMethodClientRequestedEvent`.
  - 클립보드(Avalonia 12 신 API): `IClipboard.TryGetDataAsync()` → `IAsyncDataTransfer`/`IAsyncDataTransferItem.TryGetRawAsync(DataFormat)`. (구 `GetFormatsAsync/GetDataAsync` 없음.)
  - 파일: `TopLevel.StorageProvider`(저장/열기 피커).
- **플랫폼/주의**: 현재 Windows 타깃. 클립보드 HTML은 Windows **CF_HTML**(헤더 제거 필요), 워드 그림은 **VML**(미지원). JSON 직렬화는 AOT 대비 Source-Generated 컨텍스트(`DocumentJsonContext`) 사용.

> API 사용상의 함정(예: `FormattedText`엔 히트테스트가 없어 `TextLayout` 사용)은 아래 **비자명한 핵심 규칙** 참고.

## 빌드 / 실행

**솔루션은 2개 프로젝트로 분리됨**(2026-06-08): `src/AvaloniaRichTextBox`(라이브러리=컨트롤+모델+포매터, NuGet 배포 대상) + `samples/AvaloniaRichTextBox.Demo`(WinExe 데모/테스트 앱=툴바·창).

```
dotnet build AvaloniaRichTextBox.slnx
dotnet run --project samples/AvaloniaRichTextBox.Demo/AvaloniaRichTextBox.Demo.csproj
```

- ⚠️ **실행 중인 앱이 exe를 잠근다.** 재빌드 전 반드시 종료:
  `Get-Process -Name "AvaloniaRichTextBox.Demo" -ErrorAction SilentlyContinue | Stop-Process -Force`
  (빌드 에러 MSB3027/잠김 메시지가 나오면 컴파일은 성공했고 복사만 실패한 것.)
- GUI 동작 검증은 직접 못 하므로, 백그라운드로 `dotnet run` 후 사용자에게 테스트를 요청한다.
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- `bin/`,`obj/`,`.vs/`는 `.gitignore` 처리됨(커밋에 넣지 말 것).

## 구조

**라이브러리** `src/AvaloniaRichTextBox/` (네임스페이스 `AvaloniaRichTextBox.*`, XAML 없음 — 전부 코드 컨트롤):
- `Documents/` — 문서 모델. `FlowDocument.Blocks`(AvaloniaList<Block>)에 `Paragraph`/`TableBlock`/`ImageBlock`이 **형제(sibling)**로 들어감.
  - `Paragraph.Inlines`: `Run`(텍스트) 또는 `InlineImage`(인라인 아이콘).
  - `TextPointer`(Paragraph+Offset), `TextRange`(삭제/복사/서식/구조).
- `Controls/CustomRichTextBox.cs` — 핵심 컨트롤(렌더·입력·선택·클립보드, ~3700줄). `UndoManager`, `InputDialog`, `NativeEditor`(Jodit 호환 래퍼).
- `Formatters/` — `DocumentSerializer`(JSON), `HtmlDocumentFormatter`(HTML 입출력 파서), `RoundTripHarness`(`--roundtrip` CLI 검증).

**데모/테스트 앱** `samples/AvaloniaRichTextBox.Demo/` (네임스페이스 `AvaloniaRichTextBox.Demo.*`):
- `Views/MainWindow` — 툴바 + 컨트롤 호스팅. `App`/`Program`/`ViewLocator`/`ViewModels`/`Assets`.

## 비자명한 핵심 규칙 (꼭 지킬 것)

1. **단일 `TextLayout`가 진실의 원천**: 렌더·커서·히트테스트·선택영역은 모두 `BuildTextLayout`(per-run `ITextSource`)에서 나온 하나의 `TextLayout`을 쓴다. `FormattedText`로 폭을 따로 재면 글자 단위 off-by-one이 생긴다. (Avalonia 12 `FormattedText`엔 `HitTestPoint`/`HitTestTextPosition` 없음 — `TextLayout`에 있음.)
2. **오프셋 모델 = "이미지는 1글자"**: `InlineImage`는 논리적으로 1글자(`U+FFFC` placeholder, `ImageTextRun : DrawableTextRun`로 그림). 길이/오프셋 계산은 전부 `InlineLen()`/`BuildPlain()`을 쓴다. 새 오프셋 로직 추가 시 `Run.Text.Length`만 더하지 말 것.
3. **Enter = 새 `Paragraph`**: Enter는 커서 위치에서 문단을 분할해 새 Paragraph를 만든다(`SplitParagraphAtCaret`). 새 문단은 리스트/들여쓰기/정렬/배경을 물려받고 제목 레벨은 본문(0)으로. **예외: 표 셀**은 sibling 문단을 가질 수 없어 셀 안 Enter는 여전히 Run에 `\n`. (이전엔 모든 Enter가 `\n`이었음 — 2026-06 변경. 로드 문서·붙여넣기로 들어온 `\n`은 그대로 한 문단 여러 줄로 렌더되며 줄마다 처리되는 곳들이 이를 지원.)
4. **블록 vs 인라인 이미지**: 큰 이미지=`ImageBlock`(독립 블록), 작은 아이콘(<64px)=`InlineImage`(문단 내). 표/큰 이미지에는 텍스트를 직접 넣을 수 없다(문단이 아님) — "표앞/표뒤"는 인접 문단의 끝/시작이다.
5. **`NormalizeBlocks`**: 문서 처음/끝과 "연속된 비문단 블록 사이"에만 문단을 보장(빈 줄 강제 안 함). 캐럿이 모든 블록 앞뒤에 닿게 하는 장치. `UpdateParents`에서 호출됨.
6. **선택/삭제**: 텍스트는 `TextRange`; 큰 이미지/표는 클릭하면 `_selectedBlock` 선택(파란 테두리) 후 Delete/Backspace. 표는 단일클릭=전체선택, 더블클릭=셀 편집.
7. **클립보드**: 붙여넣기 우선순위 = 내부 리치(블록 구조 보존) → 외부 HTML(`ParseHtml`, 재귀 순회) → 평문. 워드 그림은 VML이라 미지원.

## 안드레 카파시(Andrej Karpathy)의 코딩 스킬

LLM의 일반적인 코딩 실수를 줄이기 위한 행동 지침이다. **위의 프로젝트별 지침이 있을 경우 본 가이드라인과 병합하여 사용한다.**

트레이드오프: 본 지침은 속도보다 신중함에 우선순위를 둔다. 사소한 작업은 상황에 맞게 판단한다.

### 1. 구현 전 사고 (Think Before Coding)
가정하지 않는다. 모호함을 숨기지 않는다. 트레이드오프를 명확히 밝힌다. 구현을 시작하기 전 다음을 준수한다:
- 자신의 가정을 명시적으로 기술한다. 불확실한 경우 질문한다.
- 해석의 여지가 여러 가지라면 임의로 선택하지 말고 대안들을 제시한다.
- 더 간단한 접근 방식이 있다면 제안한다. 정당한 사유가 있다면 사용자의 요청에 반대 의견을 제시한다.
- 불분명한 부분이 있다면 작업을 중단한다. 혼란스러운 부분을 구체적으로 언급하며 질문한다.

### 2. 단순성 우선 (Simplicity First)
- 문제를 해결하는 최소한의 코드만 작성한다. 추측에 기반한 코드는 배제한다.
- 요청되지 않은 기능은 추가하지 않는다.
- 일회성 코드를 위해 추상화 계층을 만들지 않는다.
- 요청되지 않은 유연성이나 설정 가능성을 고려하지 않는다.
- 발생 불가능한 시나리오에 대한 예외 처리를 하지 않는다.
- 200줄의 코드를 50줄로 줄일 수 있다면 코드를 다시 작성한다.
- "시니어 엔지니어가 보기에 이 코드가 지나치게 복잡한가?"라고 자문한다. 그렇다면 단순화한다.

### 3. 정밀한 수정 (Surgical Changes)
필요한 부분만 수정한다. 본인이 만든 코드의 뒷정리만 수행한다. 기존 코드를 편집할 때 다음을 준수한다:
- 인접한 코드, 주석, 포맷을 임의로 개선하지 않는다.
- 망가지지 않은 부분을 리팩토링하지 않는다.
- 본인의 스타일과 다르더라도 기존 스타일을 따른다.
- 작업과 무관한 데드 코드를 발견하면 보고하되 직접 삭제하지 않는다.

수정으로 인해 사용되지 않게 된 요소가 발생할 경우:
- 본인의 수정으로 인해 불필요해진 임포트, 변수, 함수는 제거한다.
- 기존에 존재하던 데드 코드는 요청이 없는 한 그대로 둔다.
- 테스트 기준: 변경된 모든 라인은 사용자의 요청사항과 직접적으로 연결되어야 한다.

### 4. 목표 중심 실행 (Goal-Driven Execution)
성공 기준을 정의한다. 검증될 때까지 반복한다. 작업을 검증 가능한 목표로 변환한다:
- "유효성 검사 추가" → "잘못된 입력에 대한 테스트 작성 후 통과 확인"
- "버그 수정" → "버그를 재현하는 테스트 작성 후 통과 확인"
- "X 리팩토링" → "리팩토링 전후의 테스트 통과 확인"

다단계 작업의 경우 간략한 계획을 수립한다:
1. [단계] → 검증: [확인 사항]
2. [단계] → 검증: [확인 사항]
3. [단계] → 검증: [확인 사항]

성공 기준이 명확해야 독립적인 작업이 가능하다. "작동하게 만들기"와 같은 모호한 기준은 불필요한 재질의를 야기한다.

지침 작동 확인: Diff 내 불필요한 변경 감소, 복잡성으로 인한 재작성 빈도 감소, 구현 전 질문을 통한 명확한 의사결정 증대.

출처: https://americanopeople.tistory.com/514 [복세편살:티스토리]
