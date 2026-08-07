# AvaloniaRichEditor

[Avalonia](https://avaloniaui.net)를 위한 처음부터 새로 작성된 리치 텍스트 에디터 컨트롤입니다. WPF의 `RichTextBox`/`FlowDocument` 아이디어를 순수 C#으로 포팅했으며, 전적으로 Avalonia의 `TextLayout` 엔진 위에 구축되었습니다(PTS나 비관리형 종속성 없음). 렌더링, 레이아웃, 히트 테스트, 텍스트 선택 및 IME가 직접 구현되었습니다.

> **상태: `1.0.0`** — [NuGet](https://www.nuget.org/packages/AvaloniaRichEditor)에 배포되었습니다.
> 공개 API는 고정되었으며 [SemVer](https://semver.org)를 따릅니다: 주 버전(Major) 변경 없이는 호환성을 깨는 변경(Breaking change)이 없습니다. [`CHANGELOG.md`](CHANGELOG.md)와 [`Project_Roadmap.md`](Project_Roadmap.md)를 참고하세요.

## 설치 (Install)

```
dotnet add package AvaloniaRichEditor
```

## 기능 (Features)

- 다양한 인라인 서식: 굵게(bold) / 기울임(italic) / 밑줄(underline) / 취소선(strikethrough), 글꼴 및 크기, 글자색 및 배경(하이라이트) 색상, 하이퍼링크
- 문단 서식: 정렬, 줄 간격, 들여쓰기, 제목(headings), 글머리 기호 / 번호 매기기 목록
- **표 (Tables)**: 셀 병합(colspan/rowspan), 행/열 크기 조절, Tab 키를 통한 셀 이동 기능. 각 셀은 완전한 블록 컨테이너로, 다중 문단, 블록 이미지, 구분선 및 **중첩 표(Nested tables)**(깊이 제한 없음)를 지원하며, 재귀적 레이아웃/히트 테스트, 셀별 크기 조절 및 중첩을 넘나드는 Tab 키 이동을 지원합니다.
  - **인라인 표 (Inline tables)**: (아래한글의 "글자처럼 취급") 표가 이미지처럼 텍스트 줄 안에 배치되면서도 완벽하게 편집 가능합니다. 셀 안을 클릭하여 입력하고, 방향키나 Tab으로 이동하며, 크기를 조절할 수 있습니다. 우클릭 메뉴를 통해 일반 블록 표와 인라인 표 상태를 서로 전환할 수 있습니다.
  - **드래그하여 표 삽입 (Draw-to-size insertion)**: 그리드에서 행 × 열 개수를 선택한 후 문서 위에서 드래그하여 표의 크기를 직접 지정할 수 있습니다(클릭 시 기본 크기로 삽입).
- 인라인 및 블록 **이미지** (삽입, 크기 조절, 교체, 저장)
- 찾기 / 바꾸기, 실행 취소(undo) / 다시 실행(redo), 객체별 우클릭 컨텍스트 메뉴 (아래한글 스타일로 캐럿의 상태를 반영함; 깔끔한 툴바 유지를 위해 `ShowFormattingMenu = false`가 기본값입니다)
- **Word 표준 키보드 단축키**: `RichEditorShortcuts`라는 단일 소스를 통해 키 핸들러, 메뉴 힌트, 툴바 툴팁이 공유됩니다 — B/I/U/S, 제목 `Ctrl+Alt+1..6`, 정렬 `Ctrl+L/E/R/J`, 목록, 줄 간격 `Ctrl+1/5/2`, 들여쓰기, 글꼴 크기 등
- 클립보드: 내부 리치 복사/붙여넣기, 리치 **HTML 복사** (`CF_HTML`) 및 외부 HTML/**RTF** 붙여넣기 (Word/아래한글), 이미지 붙여넣기, Excel/TSV → 표 변환
- HTML, JSON, **RTF** 가져오기/내보내기. JSON/`.flow` 및 HTML은 무손실 왕복이 가능합니다(인라인 표는 인라인 상태 유지). RTF 내보내기는 RTF 가져오기보다 의도적으로 풍부하게 구현되어, 병합된 셀과 셀별 배경색이 Word/아래한글용으로 작성되지만 다시 가져올 때는 무시되며, 중첩된 표는 기본 열 너비로 가져오게 됩니다(Word는 이들을 무시 가능한 그룹에 보관합니다) — [문서 포맷 사양](docs/DOCUMENT_FORMAT.md)을 참고하세요 (JSON 문서 포맷 v1.0 및 `.flow` 패키지).
- 한국어/CJK **IME** 조합 (인라인 입력 중 표시)
- Word 스타일의 **페이지 뷰**: 용지 크기 선택(`PageSize`: 기본값 연속(Continuous) / A4/A3/A5/B4/B5/Letter/Legal/Tabloid), 용지 방향(`PageOrientation`) 및 페이지 경계 표시(`ShowPageBoundaries`) 기능 — 줄 단위 페이지 나누기, 머리글/바닥글/페이지 번호 지원. 워드 프로세서처럼 페이지 설정은 **문서 단위로 저장**(`FlowDocument.PageSetup`)되며 불러올 때 다시 적용됩니다.
- **인쇄 및 PDF**: 페이지별 비트맵 렌더링(`RenderPrintPage`, 300 DPI) 및 종속성 없는 래스터 PDF 내보내기(`SavePdf`)
- **손쉽게 적용 가능한 `RichEditorView`** (에디터 + 내장된 페이지/확대 축소 제어 및 내보내기/가져오기/인쇄 파일 동작이 포함된 서식 툴바 + 상태 표시줄) 및 밀도를 조절(`ToolbarLevel`: Auto/Minimal/Normal/Maximum)할 수 있는 독립형 `RichEditorToolbar`. 기능 활성화는 `IsReadOnly`(뷰어 모드 전환) 및 `Allow*` 기능 플래그들을 통해 직접 제어됩니다.
- 내장 **다국어 지원(Localization)**: 메뉴, 툴바, 대화 상자에 대해 한국어 및 영어 지원 (호스트에서 확장 가능)

## 빠른 시작 (Quick start)

```xml
<!-- MainWindow.axaml -->
<rtb:RichEditor xmlns:rtb="using:AvaloniaRichEditor.Controls"
                       x:Name="Editor" />
```

```csharp
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

// 빈 문서로 시작하거나 HTML/JSON 불러오기
Editor.Document = new FlowDocument();
Editor.LoadHtml("<p>Hello <b>world</b></p>");

// 다시 읽어오기
string html = Editor.ToHtml();
string json = Editor.ToJson();

// 변경 사항 반응
Editor.TextChanged      += (_, _) => MarkDirty();
Editor.SelectionChanged += (_, _) => UpdateToolbar();

// 외관 사용자 정의
Editor.SelectionBrush   = Brushes.LightSkyBlue;
Editor.CaretBrush       = Brushes.Black;
Editor.FontFamilyChoices = new[] { "Segoe UI", "Arial", "맑은 고딕" }; // 우클릭 폰트 메뉴
```

모든 기능이 포함된 환경을 원하신다면, `RichEditor`를 직접 설정하는 대신 **`RichEditorView`** (에디터 + 툴바 + 페이지/확대 컨트롤 + 상태 표시줄)를 바로 사용하세요. 다른 모든 제어는 `view.Editor` 및 `view.Toolbar`를 통해 가능합니다. 완전한 에디터 호스트 예제는 [`samples/AvaloniaRichEditor.Demo`](samples/AvaloniaRichEditor.Demo)를 참고하세요.

## 플랫폼 지원 (Platform support)

이 컨트롤은 크로스 플랫폼 Avalonia API로 작성되었으며 **P/Invoke가 없습니다**. 하지만 현재 **Windows** 환경에서 주로 개발되고 테스트되었으며, macOS/Linux는 현재 **최선을 다해 지원(best-effort)**하고 있습니다:

- 클립보드 HTML은 포맷 식별자로 매칭되며 Windows `CF_HTML` 헤더를 투명하게 처리합니다(다른 플랫폼의 일반 `text/html`은 변경 없이 통과됩니다).
- 특정 폰트를 가정하지 않습니다: 기본적으로 `DefaultFontFamily`로 대체되며, 우클릭 폰트 목록은 `FontFamilyChoices`에서 가져옵니다. 타겟 플랫폼/지역에 맞게 두 가지를 모두 설정하세요(데모에서는 한국어 폰트를 사용합니다).

CI 빌드 및 테스트는 **Windows, macOS, Linux** (3-OS 매트릭스)에서 통과합니다; macOS/Linux에서의 더 깊이 있는 기능 검증은 아직 보류 중입니다(로드맵에서 추적).

## 접근성 (Accessibility)

에디터는 자동화 피어(`AutomationControlType.Edit` + `IValueProvider`)를 노출하여, 화면 판독기(스크린 리더)가 Avalonia의 내장 `TextBox`가 제공하는 것과 동일한 수준으로 텍스트 내용을 읽고 설정할 수 있습니다(Avalonia의 공개 자동화 모델에는 아직 텍스트 범위/`ITextProvider` 패턴이 포함되어 있지 않습니다). 뷰에서 `AutomationProperties.Name="..."` (또는 `LabeledBy`)을 사용하여 컨트롤에 레이블을 지정하세요.

## 빌드 (Building)

```
dotnet build AvaloniaRichEditor.slnx
dotnet run --project samples/AvaloniaRichEditor.Demo/AvaloniaRichEditor.Demo.csproj
```

## 프로젝트 구조 (Project layout)

| 경로 | 내용 |
|---|---|
| `src/AvaloniaRichEditor` | 컨트롤 라이브러리 (`Controls`, 문서 모델 `Documents`, `Formatters`). NuGet 타겟. |
| `samples/AvaloniaRichEditor.Demo` | WinExe 데모/테스트 앱: 툴바, 창, 샘플 문서. |

## 라이선스 (License)

[MIT](LICENSE) © 2026 centwon. [Avalonia](https://github.com/AvaloniaUI/Avalonia) 및 [HtmlAgilityPack](https://html-agility-pack.net/)에 의존합니다 (모두 MIT) — [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
