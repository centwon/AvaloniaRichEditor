# CLAUDE.md

WPF의 `RichTextBox`/`FlowDocument`를 **순수 C# + Avalonia `TextLayout`**로 바닥부터 이식하는 프로젝트.
PTS(비관리형 C++) 엔진을 못 쓰므로 렌더링/레이아웃/히트테스트를 직접 구현한다.

- **현재 진행 상황과 보류 항목은 항상 [`Project_Roadmap.md`](Project_Roadmap.md)를 먼저 확인**할 것. 작업 후 이 파일을 갱신한다.
- 스택: .NET 10, Avalonia 12.0.1, `WinExe`, `PublishAot`. 의존성: CommunityToolkit.Mvvm, HtmlAgilityPack.

## 빌드 / 실행

```
dotnet build AvaloniaRichTextBoxPort.csproj
dotnet run --project AvaloniaRichTextBoxPort.csproj
```

- ⚠️ **실행 중인 앱이 exe를 잠근다.** 재빌드 전 반드시 종료:
  `Get-Process -Name "AvaloniaRichTextBoxPort" -ErrorAction SilentlyContinue | Stop-Process -Force`
  (빌드 에러 MSB3027/잠김 메시지가 나오면 컴파일은 성공했고 복사만 실패한 것.)
- GUI 동작 검증은 직접 못 하므로, 백그라운드로 `dotnet run` 후 사용자에게 테스트를 요청한다.
- 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- `bin/`,`obj/`,`.vs/`는 `.gitignore` 처리됨(커밋에 넣지 말 것).

## 구조

- `Documents/` — 문서 모델. `FlowDocument.Blocks`(AvaloniaList<Block>)에 `Paragraph`/`TableBlock`/`ImageBlock`이 **형제(sibling)**로 들어감.
  - `Paragraph.Inlines`: `Run`(텍스트) 또는 `InlineImage`(인라인 아이콘).
  - `TextPointer`(Paragraph+Offset), `TextRange`(삭제/복사/서식/구조).
- `Controls/CustomRichTextBox.cs` — 핵심 컨트롤(렌더·입력·선택·클립보드, ~1700줄). `UndoManager`.
- `Formatters/` — `DocumentSerializer`(JSON, 부분 구현), `HtmlDocumentFormatter`(외부 HTML 붙여넣기 파서).
- `Views/MainWindow` — 툴바 + 컨트롤 호스팅.

## 비자명한 핵심 규칙 (꼭 지킬 것)

1. **단일 `TextLayout`가 진실의 원천**: 렌더·커서·히트테스트·선택영역은 모두 `BuildTextLayout`(per-run `ITextSource`)에서 나온 하나의 `TextLayout`을 쓴다. `FormattedText`로 폭을 따로 재면 글자 단위 off-by-one이 생긴다. (Avalonia 12 `FormattedText`엔 `HitTestPoint`/`HitTestTextPosition` 없음 — `TextLayout`에 있음.)
2. **오프셋 모델 = "이미지는 1글자"**: `InlineImage`는 논리적으로 1글자(`U+FFFC` placeholder, `ImageTextRun : DrawableTextRun`로 그림). 길이/오프셋 계산은 전부 `InlineLen()`/`BuildPlain()`을 쓴다. 새 오프셋 로직 추가 시 `Run.Text.Length`만 더하지 말 것.
3. **줄바꿈은 Run 안의 `\n`**: Enter는 새 `Paragraph` 블록이 아니라 현재 Run에 `\n`을 넣는다. 초기 문서·표 셀만 별도 Paragraph. (메모리 `linebreaks-in-runs.md` 참고)
4. **블록 vs 인라인 이미지**: 큰 이미지=`ImageBlock`(독립 블록), 작은 아이콘(<64px)=`InlineImage`(문단 내). 표/큰 이미지에는 텍스트를 직접 넣을 수 없다(문단이 아님) — "표앞/표뒤"는 인접 문단의 끝/시작이다.
5. **`NormalizeBlocks`**: 문서 처음/끝과 "연속된 비문단 블록 사이"에만 문단을 보장(빈 줄 강제 안 함). 캐럿이 모든 블록 앞뒤에 닿게 하는 장치. `UpdateParents`에서 호출됨.
6. **선택/삭제**: 텍스트는 `TextRange`; 큰 이미지/표는 클릭하면 `_selectedBlock` 선택(파란 테두리) 후 Delete/Backspace. 표는 단일클릭=전체선택, 더블클릭=셀 편집.
7. **클립보드**: 붙여넣기 우선순위 = 내부 리치(블록 구조 보존) → 외부 HTML(`ParseHtml`, 재귀 순회) → 평문. 워드 그림은 VML이라 미지원.

## 협업 원칙 (Karpathy의 AI 코딩 조언에서 정리)

> 공식 문서가 아니라 Andrej Karpathy가 AI 코딩 에이전트 협업에 대해 공개적으로 강조해 온 내용을 이 프로젝트에 맞게 정리한 것.

- **에이전트를 짧은 목줄에 둔다 (keep on a tight leash).** 한 번에 거대한 변경을 쏟아내지 말고, 작고 점진적인 단위로 진행한다. 큰 변경일수록 검토 비용·회귀 위험이 커진다.
- **매 단계 검증한다.** 변경 후 빌드하고, 가능한 경우 실제 앱을 실행해 동작을 확인한 뒤 다음 단계로 간다. "될 것 같다"로 넘어가지 않는다.
- **사람을 루프 안에 둔다.** 위험하거나 되돌리기 어려운 변경, 큰 설계 결정은 진행 전에 사용자에게 확인받는다. 생성한 코드를 맹신하지 않는다 — 그럴듯하지만 틀린 코드를 경계한다.
- **구체적으로 작업한다.** 모호한 추측보다 코드/스펙을 직접 읽고 근거 있게 변경한다. 추측이 필요하면 사용자에게 묻는다.
- **컨텍스트를 유지한다.** `Project_Roadmap.md`(상태/보류 항목)와 이 `CLAUDE.md`(규칙)를 항상 최신으로 유지해 다음 세션이 빠르게 따라잡게 한다.
- **검증 가능한 것을 만든다.** 변경은 빌드/실행/테스트로 확인 가능한 형태여야 한다. 끝나면 무엇을 어떻게 확인하면 되는지 사용자에게 알린다.
