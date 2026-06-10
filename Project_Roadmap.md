# Avalonia RichTextBox Port Project

## 🎯 프로젝트 목표 (Project Goal)
이 프로젝트의 최종 목표는 WPF의 방대한 `RichTextBox` 및 `FlowDocument` 프레임워크를 Avalonia UI로 이식하는 것입니다. 
기존 Avalonia 생태계에는 완벽한 네이티브 RichTextBox가 없으며, WPF의 내부 렌더링 엔진(PTS, 비관리형 C++)을 그대로 가져올 수 없으므로 **"순수 C#과 Avalonia의 TextLayout 엔진만을 사용하여 바닥부터(From-Scratch) 독자적인 렌더링 및 레이아웃 엔진을 구축하는 것"**이 핵심입니다.

---

## 🗺️ 구현 로드맵 및 상태 (Implementation Status)

### 🟢 [완료] Phase 1: 기반 모델 및 렌더링 엔진 구축
- **완료된 내용**: `TextElement`, `Block`, `Paragraph`, `Inline`, `Run`, `FlowDocument` 데이터 구조 설계 완료.
- **완료된 내용**: Avalonia `FormattedText`를 이용해 글꼴 크기, 굵기, 색상, 밑줄, **취소선(Strikethrough)** 등 다중 서식 렌더링 적용 성공.

### 🟢 [완료] Phase 2: 에디터 상호작용 (커서 및 키보드 입력)
- **완료된 내용**: 화면 상의 X, Y 픽셀 좌표를 문서 내의 글자 인덱스로 변환하는 정밀한 히트 테스트(Hit-Testing) 구축 완료. (일반 문단 및 **표(Table) 내부 셀 진입 완벽 지원**)
- **완료된 내용**: `KeyDown` 이벤트를 통한 커서 이동(상/하/좌/우/Home/End) 및 텍스트 입력 로직 구현 완료.
- **완료된 내용**: `DeleteLocalText` 역순 반복 버그 수정. 문단 경계 Backspace/Delete 시 이전/다음 문단 병합 로직 추가.

### 🟢 [완료] Phase 3: 텍스트 선택 및 부분 서식 변경
- **완료된 내용**: 마우스 드래그를 이용한 텍스트 범위 선택(Selection) 시각화 기능 (`DrawSelectionHighlight`로 반투명 파란색 하이라이트 렌더링).
- **완료된 내용**: `TextRange.Delete()` — 다중 문단 선택 삭제 완벽 지원 (중간 문단 삭제 + 시작/끝 문단 병합, 테이블 셀 지원).
- **완료된 내용**: `SplitRunAtOffset` 알고리즘을 통한 부분 서식(Bold, Italic 등) 적용 — 선택한 단어/범위 단위로 `Run`을 분할하여 개별 서식 적용 가능.
- **완료된 내용**: `TextRange.ApplyPropertyValue()` — 다중 문단 걸친 서식 적용 시 중간 문단 포함 전체 적용.
- **완료된 내용**: Ctrl+A(전체 선택), Ctrl+C(복사), Ctrl+X(잘라내기), Ctrl+V(붙여넣기) 키보드 단축키 지원.
- **완료된 내용**: `TextRange.GetText()` 메서드 추가 — 다중 문단 걸친 텍스트 추출 지원.
- **완료된 내용**: 히트 테스트/커서/선택 영역을 단일 `TextLayout`(per-run `ITextSource`) 기반으로 전환하여 글자 단위 off-by-one 오차 제거. (부가 효과: 텍스트 정렬 Center/Right 실제 적용)
- **완료된 내용**: 앱 내부 리치 클립보드(`GetRichRuns`/`InsertRuns`) — Ctrl+C/V 시 서식 유지 복사/붙여넣기.
- **완료된 내용**: 표(Table) 셀 선택 하이라이트 — 선택 범위에 포함된 셀을 시각적으로 표시(완전 선택 셀은 셀 전체 채움, 빈 셀 포함).
- **완료된 내용**: 표 구조(행/열) 복사·붙여넣기 보존 — 선택이 표/이미지를 통째로 포함하거나 여러 최상위 블록에 걸치면 내부 클립보드가 블록 구조를 클론 저장(`CaptureBlockStructure`)하고, 붙여넣기 시 `InsertBlocks`로 표를 재구성. 일반 인라인 선택은 기존 Run 기반 유지.

### 🟢 [완료] Phase 4: 클립보드 및 포맷 파서
- **완료된 내용**: 자체 커스텀 JSON 포맷으로 저장 및 불러오기 동작.
- **완료된 내용**: 클립보드에서 텍스트(Plain Text)를 붙여넣기하는 기본 기능 연동 완료.
- **완료된 내용**: 외부 앱(웹/워드 등) HTML 붙여넣기 — Avalonia 12 신규 클립보드 API(`TryGetDataAsync`→`IAsyncDataTransfer`)로 HTML 포맷 읽기, Windows CF_HTML 헤더 제거(`ExtractHtmlFragment`), `HtmlDocumentFormatter.ParseHtml`로 FlowDocument 변환 후 커서 위치에 삽입(`InsertParsedDocument`). Ctrl+V·Paste HTML 버튼 모두 적용(내부 리치 → 외부 HTML → 평문 순서 폴백).
- **완료된 내용**: HTML 파서 재작성 — 중첩 `<div>`/레이아웃을 재귀 순회(`WalkBlocks`)하여 표/이미지/문단/제목(h1~h6)/리스트/`<br>` 보존. 인라인 서식(굵게·기울임·색상 rgb()/#hex/이름·글자크기 px), 공백 정리. 하이퍼링크: 카드형 `<a href>`가 블록을 감싸도 링크 컨텍스트를 전파(`WalkBlocks`/`ParseInlines` `linkUri`)하여 파란+밑줄로 인식. 작은 아이콘/로고/이모지 이미지(≤64px)는 생략(`IconMaxSize`)하여 줄 깨짐 방지. `file://` 이미지 지원.
- **완료된 내용**: 하이퍼링크 상호작용 — 링크 위 호버 시 손모양 커서, 클릭 시 기본 브라우저로 열기(`GetLinkRunAtPoint`/`OpenUrl`, http/https만 허용).
- **완료된 내용**: 인라인 이미지 지원 — `InlineImage`(Inline) + `DrawableTextRun`(`ImageTextRun`)로 작은 아이콘/로고를 텍스트 줄 안에 배치. 오프셋 모델 전반을 "이미지=1글자(U+FFFC)"로 일관화(`InlineLen`/`BuildPlain`, `BuildTextLayout` 세그먼트화, `DeleteLocalText`/`TryInsertTextCore`/`SplitInlinesAt`/`RunAtOffset` 및 `TextRange`의 split/delete/style/text 추출). HTML 파서는 작은 img(<64px)는 `InlineImage`(인라인), 큰 img는 `ImageBlock`(블록)로 분기.
- **완료된 내용**: 그림·표 삭제 — (1) 인접 캐럿: 앞 문단 끝 Delete / 뒤 문단 시작 Backspace, `NormalizeBlocks`로 항상 인접 문단 보장. (2) 드래그 선택: `TextRange.Delete`가 시작~끝 사이의 모든 최상위 블록(그림/표/문단)을 제거(`TopLevelBlockOf`). (3) 클릭 선택: 이미지/표를 클릭하면 파란 테두리로 선택(`_selectedBlock`, `GetBlockAtPoint`)되고 Delete/Backspace로 삭제. 표는 단일 클릭=전체 선택, 더블클릭(또는 편집 중 클릭)=셀 편집. 작은 인라인 아이콘은 HTML 파싱 시 바로 앞 줄(제목 문단)에 붙도록 휴리스틱 적용.
- **완료된 내용**: 화살표 키 블록 횡단 — `이전 문단 끝(표앞) → 첫 셀 → … → 마지막 셀 → 다음 문단 시작(표뒤) → 다음 문단` 순서(역방향 대칭)로 표/이미지를 가로질러 이동. `NormalizeBlocks`는 빈 줄을 강제하지 않고(문서 처음/끝·연속 블록 사이에만 문단 보장) "표앞/표뒤"를 인접 텍스트 줄의 끝/시작으로 처리.
- **참고(한계)**: 워드의 그림은 표준 `<img>`가 아니라 VML(`<v:shape>`/WMF/EMF base64)로 내보내므로 가져오지 못함.

### 🟡 [진행 중] Phase 5: 고급 레이아웃 요소 지원
- **완료된 내용**: 이미지 블록 삽입 및 렌더링 기능.
- **완료된 내용**: 동적 `TableBlock` 렌더링 엔진 도입 완료. 텍스트 크기 증가나 줄바꿈에 따른 **셀 높이 동적 연장 문제 완벽 해결**.
- **완료된 내용**: 표 열(Column) 크기 조절 — Render에서 각 열 오른쪽 경계에 6px 드래그 핸들(`_columnBoundaries`)을 생성해 배선 완료. 내부 경계는 전체 폭 고정 + 인접 열 비율 재분배, 맨 오른쪽 바깥 경계는 해당 열만 늘려 전체 폭 확장. 경계 호버 시 좌우 화살표 커서, 최소 20px, Undo 1회 복원. `InsertTable`이 `ColumnWidths`를 열 개수만큼 채우고 `UpdateParents` 호출하도록 수정.
- **완료된 내용**: 한글/IME 입력 지원 — `TextInputMethodClient` 연결로 조합 입력 활성화 + 조합 중 글자를 커서 위치에 밑줄과 함께 인라인 표시(preedit, `BuildTextLayout`에 `SplicePreedit` 주입). 한 박자 늦게 보이던 문제 해결.
- **완료된 내용**: 이미지 크기 조절 — 이미지 오른쪽 아래 드래그 핸들(`_imageHandles`)로 종횡비 유지 리사이즈, 대각선 커서, Undo 1회 복원.
- **완료된 내용**: 이미지/표 삽입 위치 개선 — 항상 문서 끝이 아니라 커서가 위치한 블록 다음에 삽입(`InsertBlockAtCaret`).
- **완료된 내용**: 우클릭 컨텍스트 메뉴 — 위치별(텍스트/이미지/표/빈 곳) 동적 `ContextMenu`. 텍스트(잘라/복사/붙여/삭제·굵게/기울임/밑줄/취소선·크기/색/정렬·서식 지우기·링크 삽입/편집/제거/열기·삽입), 이미지(삭제·원본크기·교체·저장), 표(행·열 삽입/삭제·표 삭제). 하이퍼링크 URL 입력용 `InputDialog`.
- **완료된 내용**: 완전한 저장/불러오기 — `DocumentSerializer` 재작성. 표·이미지·인라인이미지·서식(굵게/기울임/밑줄/취소선/크기/색/링크)·정렬·여백·열폭·행높이를 모두 JSON 직렬화/복원. 비트맵은 PNG→base64. AOT 친화 평면 DTO(Type 판별자).
- **완료된 내용**: 서식 단축키(`Ctrl+B/I/U`) + 밑줄 토글(`ToggleUnderline`). 밑줄·취소선이 공존하도록 `ToggleDecoration` 헬퍼로 데코레이션 단위 토글.
- **완료된 내용**: 찾기/바꾸기 — `Ctrl+F`로 찾기 바, 다음/이전(`FindNext`/`FindPrev`, 랩어라운드), 바꾸기/모두 바꾸기(`ReplaceNext`/`ReplaceAll`), 대소문자 구분.
- **완료된 내용**: 표 안 Tab 이동 — Tab=다음 셀, Shift+Tab=이전 셀, 마지막 셀 Tab=새 행 추가(`HandleTab`/`FocusCell`). 표 밖에서는 공백 삽입.
- **완료된 내용**: 표 행(Row) 높이 수동 조절 — 열 조절 코드를 대칭 적용. `TableBlock.RowHeights`(사용자 지정 최소 높이, 빈/0=자동) 추가, Render에서 행 하단 6px 가로 드래그 핸들(`_rowBoundaries`) 생성 + `rowMaxHeight = Max(내용 높이, RowHeights[r])`, 상하 화살표 커서(`SizeNorthSouth`), 최소 20px, Undo 1회 복원. 렌더-히트 일치를 위해 히트테스트 3곳(`GetPositionFromPoint`/`GetBlockAtPoint`/`GetLinkRunAtPoint`)에도 동일 클램프 적용. `Clone`이 `RowHeights` 복사.
- **❗ 미해결 문제 (보류)**:
  1. **블록 여백(Margin) 조정**: 현재 `Paragraph`는 `MarginTop/Bottom` 속성이 있으나 렌더에는 `MarginBottom`만 반영되고 조정 UI 없음. `ImageBlock`/`TableBlock`은 여백 속성 자체가 없고 고정 10px 간격·들여쓰기 사용. 구현하려면 (1) `Block`에 상하좌우 여백 속성 추가, (2) 렌더에서 고정값 대신 반영(`MarginTop` 포함), (3) 툴바 입력 또는 드래그 핸들 UI 필요. (보류)

### 🟢 [완료] Phase 6: 우클릭 메뉴·찾기/바꾸기 + 상용 에디터 수준 기능 완성
- **완료**: 우클릭 컨텍스트 메뉴(텍스트/이미지/표/빈 곳), 찾기/바꾸기(Ctrl+F), 표 안 Tab 이동, 서식 단축키(Ctrl+B/I/U)·밑줄.
- **완료**: 완전한 JSON 저장/불러오기(표·이미지·서식·정렬·여백·열폭·행높이, 비트맵 base64).
- **완료**: HTML 무손실 왕복 강화 — 글꼴/임의색/배경/크기(px·pt)/밑줄·취소선/이미지(data:)/번호목록(ol)/제목(h1~6)/구분선(hr)/셀배경/들여쓰기. 모델 확장(`Run.FontFamily/Background`, `Paragraph.ListType/HeadingLevel/Background/Indent`, `DividerBlock`).
- **완료**: 편집 UI(툴바·메뉴) 파리티, ReadOnly 모드, 이미지 붙여넣기/드래그드롭(+다운스케일), 인쇄 우회.
- **완료**: `NativeEditor` 호환 래퍼(웹 에디터 동일 API 표면) — **외부 앱 통합 가능 수준**. AOT 퍼블리시 확인 통과. 왕복 검증 하네스(`--roundtrip`)+코퍼스.
- **완료**: 잔여 정리(blockquote·중첩목록 깊이·블록 정렬 읽기), HWP/Excel 붙여넣기.
- **완료**: 클립보드 붙여넣기 버그 2건 수정 — (1) HTML 포맷 감지를 `fmt.Identifier` 기준으로(엑셀/한글 표가 텍스트·이미지로 새던 문제), (2) 엑셀 CF_HTML의 `<table>` 누락 보정. → 엑셀·한글 진짜 표가 표로 붙음(한글 글상자는 이미지=정상).
- **완료**: **표 셀 병합(colspan/rowspan)** — 실데이터(코퍼스 8건 중 5건, 최대 189회)가 요구하던 격차 해소. 밀집 그리드+가려짐 마커 모델(`TableBlock.ColSpans/RowSpans`), 렌더·히트테스트 3곳의 기하를 단일 `LayoutTable` 헬퍼로 추출. HTML 파싱(occupancy-fill)/출력/JSON/내비게이션(앵커 단위 Tab)/우클릭 병합·해제 UI 전부 지원. 왕복 하네스 colspan·rowspan **in==out** 정확 일치.
- **보류**: HWP/XLS 붙여넣기, 정밀 인쇄(페이지네이션/PDF), blockquote/중첩목록 깊이.

### 🔵 [백로그] 향후 작업 후보
> 우선순위는 실데이터 충실도(HTML 왕복) 기준으로 재평가. 메모리 `future-work-suggestions.md`와 동기화.

- **사용성(UX) 제안** (로드맵 외 일반 리치에디터 기능):
  - 더블클릭=단어 선택 / 세 번 클릭=문단 선택
  - 자동 목록(스마트 입력): `1. ` 또는 `- ` 입력 시 자동 리스트 전환
  - 서식 페인터(Format Painter)
  - 상태바: 글자 수/단어 수/커서 위치
- **🐞 미해결(블록 캐럿)**: 그림/표 앞·뒤에 블록 캐럿을 두고 **Space로 앞 여백** 주는 기능 구현(동작함).
  단 **표 마지막 셀에서 ↓로 "표 뒤(after)" 캐럿 진입이 안 되는 버그** 남음(메모리 `future-work-suggestions.md` 참고). 추후 재논의.
- **A4 페이지 레이아웃(추후)**: 편집 영역을 A4 기준으로. ① 간단안=A4 폭 고정+회색 배경 위 흰 페이지(가운데 정렬, 경계 없음, 저위험), ② **진짜 페이지네이션**=A4 한 장(≈794×1123@96DPI)마다 경계로 끊고 다음 장으로(렌더·히트테스트가 페이지 좌표를 알아야 함, 대공사). 정밀 인쇄/PDF와 연계. 참고: 주 용도인 HTML 콘텐츠는 웹 리플로우라 본질적 페이지 크기는 없음.
- **IME 한글 기본 입력(시도→실패, 보류)**: IMM32 `ImmSetConversionStatus(IME_CMODE_NATIVE)` P/Invoke를 GotFocus + 시작시 자동포커스와 함께 시도했으나 **한국어 Win11에서 한글 전환 안 됨**(예상대로 TSF가 IMM32 변환모드 무시). 코드 제거함. 남은 대안: `SendInput(VK_HANGUL)`(토글이라 현재 상태 확인 필요·위험) 또는 TSF 인터롭(복잡). 실용성 대비 비용이 커서 보류. (시작시 에디터 자동포커스는 유지 — 클릭 없이 바로 입력 가능.)
- **사용성(기능) 개선 후보**:
  - **블록 여백(Margin) 제어**: `Block`에 상하좌우 여백 속성 추가 → 렌더에서 고정값 대신 반영(`MarginTop` 포함) → 툴바 입력 또는 컨텍스트 메뉴 UI. 현재 문단은 `MarginBottom`만, 이미지/표는 고정 10px.
  - **DOCX 클립보드 파싱**: 한글(HWP) 표/글상자, 워드 그림(VML)이 이미지로 들어오는 문제 해결. 클립보드 DOCX format (`<w:tbl>`, `<w:drawing>`) 파싱. OOXML 스펙이 방대해 "표+이미지+기본 서식"만 타게팅해도 상당한 작업량.
  - **마크다운 입출력**: Import는 Markdig 등 기존 파서 활용 가능. Export는 손실적(표 병합, 인라인 이미지, 글자색 등 표현 불가). 대상 사용자층에 따라 우선순위 결정.
- **구조적 기반** (성능 개선의 전제):
  - **테스트 보강**: 27개 테스트는 4,000줄+ 에디터 대비 낮은 수준. N6 이미지 모델 전환 등 구조 변경의 안전망 확보 필요.
  - **크로스플랫폼 실검증**: mac/Linux 스모크 테스트 미실행. GitHub 푸시 후 CI 3-OS 매트릭스로 확인.
- **남은 보류 항목**:
  - 정밀 인쇄(페이지네이션/PDF) — 현재 브라우저 우회만
  - 외부 앱 실통합 (기능 플래그 롤아웃)

---

## 📦 NuGet 배포 계획 (NuGet Publication Plan)

> **목표**: `AvaloniaRichEditor`(src/) 라이브러리를 **NuGet에 배포 가능한 수준**으로 끌어올린다.
> 현실적 출시 기준선은 **`0.1.0-alpha`**(실험적·기능 한정 공개)이며, 그 위에 **`1.0`**(프로덕션) 로드맵을 둔다.
> 평가 근거: 코드는 탄탄하나(표 병합·HTML 왕복·IME·레이아웃 캐싱) 패키징·공개 API·테스트·크로스플랫폼·접근성이 부재.

### 출시 품질 기준선 (Release Tiers)
- **`0.1.0-alpha`** = 패키지로 설치·참조 가능 + 최소 공개 API/문서 + Windows에서 동작 보장 + LICENSE/README. "써볼 수 있다."
- **`0.x`** = 크로스플랫폼 검증 + 공개 API 안정화 + 테스트 + CI + 에디터 모드(읽기전용/최소/전체). "실무에 조심스럽게 쓸 수 있다."
- **`1.0`** = 기존 기능의 안정성·성능·문서화를 프로덕션 수준으로. 새 기능 추가 없이 품질 집중. "프로덕션."

### 🟢 [대부분 완료] 최우선: GitHub 저장소 생성 + 푸시 (단일 차단점 해소, 2026-06-10)
> **N1 잔여·N3 잔여(mac/Linux 스모크)·N4 잔여(CI 그린)·`0.1.0-alpha` 체크리스트 전체가 이 하나에 막혀 있었다.** → 저장소 생성·푸시·CI 그린으로 핵심 차단 해제.
- [x] 푸시 전 정리: `test.json`(스크래치) 추적 해제 + `.gitignore` 추가. (`tests/out`·`roundtrip-out`·`test.html`·corpus `real_*`는 이미 ignore/미추적 확인.)
- [x] 히스토리 정리: 초기 커밋에 박혀 있던 ~240MB 빌드 산출물(`bin/`·`obj/`)을 `git filter-branch`로 전체 히스토리에서 제거 후 force-push. 결과: 깨끗한 저장소.
- [x] GitHub 저장소 생성 + 푸시 — **`centwon/AvaloniaRichEditor`**.
- [x] **CI 3-OS 매트릭스 첫 실행 그린** — windows/ubuntu/macos 전부 ✓ (Linux 헤드리스 폰트 이슈 없음). → **N3 mac/Linux 스모크 + N4 CI 그린 동시 해소.**
- [x] **Public 전환 완료** (2026-06-10) — 전환 전 스캔(시크릿·1MB+ 파일·개인경로 0건, LICENSE/corpus 합성 확인). https://github.com/centwon/AvaloniaRichEditor
- [ ] N1 미결 해소: `RepositoryUrl`/`PackageProjectUrl` + SourceLink 채움 (Public 전환 후).
- [ ] (유지보수) CI 액션 Node 20 → Node 24 deprecation 대응(`actions/checkout`·`setup-dotnet` 버전 갱신).

---

### 🟢 [완료] N0: 프로젝트 구조 분리 (2026-06-08)
- 단일 WinExe → `src/AvaloniaRichEditor`(라이브러리) + `samples/AvaloniaRichEditor.Demo`(데모/테스트 앱)로 분리.
- 네임스페이스 `AvaloniaRichEditor.*` / `AvaloniaRichEditor.Demo.*`. 솔루션 `AvaloniaRichEditor.slnx`. 빌드·실행 검증 완료.

### 🟡 N1: 패키징 기반 — **`0.1.0-alpha`** (로컬 pack 검증 완료 2026-06-08)
- [x] NuGet 메타데이터: `PackageId=AvaloniaRichEditor`, `Version=0.1.0-alpha`, `Authors=centwon`, `Description`, `PackageTags`, `PackageLicenseExpression=MIT`, `Copyright`, `PackageReadmeFile`.
- [x] `LICENSE`(MIT, © 2026 centwon) 추가.
- [x] 패키지에 `README.md` 동봉(`<None Include="..\..\README.md" Pack=true>`).
- [x] `<GenerateDocumentationFile>true` + XML 동봉(`CS1591`은 부분 문서화라 임시 NoWarn). `<Deterministic>`.
- [x] `<IncludeSymbols>` + `snupkg` 생성.
- [x] `dotnet pack -c Release` → `AvaloniaRichEditor.0.1.0-alpha.nupkg`/`.snupkg` 생성, nuspec/DLL/XML/README/의존성 확인.
- [x] **검증**: 별도 빈 net10 프로젝트가 로컬 피드로 패키지 설치 후 공개 API(`RichEditor`/`LoadHtml`/`TextChanged`/`SelectionBrush`/`ToHtml`/`ToJson` 등) 소비 빌드 성공.
- [x] `RepositoryUrl`/`PackageProjectUrl` + **SourceLink** (2026-06-10) — .NET 8+ SDK in-box 공급자 사용(`PublishRepositoryUrl`/`EmbedUntrackedSources`, 별도 패키지 없음). pack 결과 nuspec에 `repository url+branch+commit` 박힘 확인.
- [x] `CHANGELOG.md` 시작 (Keep a Changelog 형식, `0.1.0-alpha` 항목).
- [ ] **의도적 보류(2026-06-10 결정)**: `PackageIcon` + nuget.org 실제 게시를 **함께 미룸**. 근거: ① 게시는 비가역(unlist는 되나 삭제 불가, 버전 영구 예약)인데 alpha API가 아직 변할 수 있음. ② 지금도 태그 CI의 `Pack` 아티팩트(`.nupkg`)를 로컬 피드/GitHub Release로 소비 가능 — nuget.org는 "더 넓은 배포"일 뿐 alpha 성립 조건 아님. ③ 아이콘이 의미를 갖는 시점이 곧 게시 시점이라 둘을 한 묶음으로 처리. **재개 조건**: API 안정화 + nuget API 키 발급 → 시크릿 `NUGET_API_KEY` 추가 + [ci.yml](.github/workflows/ci.yml) push 스텝 주석 해제 + `PackageIcon` 추가 + GitHub Release 작성.
- **참고**: `AvaloniaRichEditor` ID는 nuget.org 미등록(사용 가능). `Avalonia.` 점 프리픽스는 예약이라 회피.

### 🟡 N2: 공개 API 설계 & 문서화 (대부분 완료 2026-06-08)
- [x] **표면 정리**: 직렬화 DTO·`UndoManager`/`UndoState`·`InputDialog`를 `internal`로(중첩 레이아웃 타입은 이미 private). `[InternalsVisibleTo("AvaloniaRichEditor.Tests")]` 추가.
- [x] **표준 이벤트**: `TextChanged`, `SelectionChanged`, `DocumentChanged` 추가. 변이 신호를 `PushUndo()` 단일 choke point로 집약, Render에서 `Dispatcher.Post`로 비재진입 플러시.
- [x] **스타일 가능 속성(StyledProperty)**: `SelectionBrush`, `CaretBrush`, `DefaultFontFamily`, `DefaultFontSize` 추가(선택색/캐럿색 하드코딩 제거, 기본 글꼴 외부화).
- [x] **편의 API**: `ToHtml`/`LoadHtml`(기존 Get/SetHtml 개명), `ToJson`/`LoadJson`, `Clear`, `CanUndo`/`CanRedo`.
- [x] `NativeEditor`(웹 에디터 호환 래퍼) 라이브러리→`samples` 이동.
- [ ] 공개 멤버 XML 문서 주석 — **부분 완료**(신규 멤버만). 기존 공개 명령(`ToggleBold` 등) 주석 미작성.
- [ ] **API 동결 가드**: `Microsoft.CodeAnalysis.PublicApiAnalyzers` 도입 — 미착수.
- [ ] (선택) 데모 코드비하인드를 새 이벤트/속성으로 마이그레이션 — 미착수(`StatusChanged` 계속 사용 중).

### 🟡 N3: 크로스플랫폼 / Windows 의존 게이팅 (코드 게이팅 완료 2026-06-08)
- [x] **클립보드 CF_HTML**: 조사 결과 이미 안전 — `TryGetHtmlAsync`는 포맷 식별자에 "html" 포함 매칭(Windows `HTML Format`/mac `public.html`/Linux `text/html` 공통), `ExtractHtmlFragment`는 CF_HTML 마커 없으면 원문 통과. 별도 분기 불필요.
- [x] **하드코딩 한글 폰트** 외부화: 컨텍스트 메뉴 글꼴 목록을 `FontFamilyChoices` 속성으로(기본=범용 폰트, 플랫폼 가정 없음). 데모가 한글 폰트로 설정. (`DefaultFontFamily`는 N2에서 외부화 완료)
- [x] `OpenUrl`은 `Process.Start(UseShellExecute=true)` + try/catch — 현대 .NET에서 크로스플랫폼(xdg-open/open). P/Invoke 없음 확인.
- [x] `app.manifest`/`PublishAot`는 데모에만(구조 분리로 확인). 라이브러리는 플랫폼 중립.
- [x] README에 플랫폼 지원(Windows 우선, mac/Linux 베스트에포트) 명시.
- [x] mac/Linux 실제 스모크 테스트(헤드리스 빌드/렌더) — **CI 3-OS 매트릭스 첫 실행 그린(2026-06-10)으로 검증 완료**(ubuntu/macos 빌드+테스트 통과).

### 🟢 [완료] N3.5: 에디터 모드 (2026-06-09, `0.x` 목표)
> 하나의 컨트롤로 뷰어·간편 입력·본격 편집을 모두 커버한다. 기능 플래그 조합으로 유연성을 확보하고, `EditorMode` 프리셋으로 편의 제공. 구현: `RichEditor.Modes.cs`(enum+플래그+프리셋/ReadOnly 핸들러). 기본=Full이라 기존 호스트 동작 불변.

- [x] **기능 플래그(StyledProperty)**: `AllowImages`, `AllowTables`, `AllowRichPaste`, `AllowFindReplace`(전부 기본 `true`). 소비자가 개별 기능을 켜고 끌 수 있음.
- [x] **`EditorMode` 프리셋**: `ReadOnly`(기존 `IsReadOnly` 통합—프리셋이 `IsReadOnly=true` 세팅), `Basic`(텍스트+기본 서식만), `Full`(현재 전체 기능, 기본값). 프리셋 설정 시 내부 플래그 일괄 적용(`ApplyEditorModePreset`, 정적 클래스 핸들러). **개별 플래그가 프리셋을 오버라이드 가능**(프리셋 적용 후 플래그 재설정).
  | 모드 | 텍스트 입력 | 기본 서식 | 표/이미지 | 리치 붙여넣기 | 찾기/바꾸기 | 컨텍스트 메뉴 | 툴바 |
  |------|:---------:|:-------:|:-------:|:----------:|:---------:|:----------:|:----:|
  | ReadOnly | — | — | 렌더만 | — | — | 복사만 | 없음/뷰어 |
  | Basic | O | O | — | 평문만 | — | 서식만 | 서식 버튼만 |
  | Full | O | O | O | O | O | 전체 | 전체 |
  > **툴바 열은 의도(설계 목표)이며 아직 미구현** — 툴바가 라이브러리 밖(데모 `NativeEditor`)에 있어 현재 모드는 동작·컨텍스트 메뉴까지만 지배. 툴바 연동은 N3.6 참고.
- [x] **가드 삽입**: 붙여넣기 경로(내부리치/HTML→AllowRichPaste, 이미지→AllowImages, TSV표→AllowTables), 드래그드롭(AllowImages), 공개 삽입 명령(`InsertImage`/`InsertTable`/`InsertImageFromFileAsync`), 컨텍스트 메뉴(표/이미지 삽입 항목 조건부), 찾기/바꾸기(`FindNext`/`FindPrev`/`ReplaceNext`/`ReplaceAll` no-op).
- [x] **ReadOnly 최적화**: Undo 스택 비활성(`UndoManager.Clear`), IME 클라이언트 미연결(`e.Client=null`), 캐럿 블링크 타이머 정지(2Hz 재그리기 제거). `OnReadOnlyChanged` 중앙 처리 — `IsReadOnly`가 프리셋/직접설정 어느 쪽으로 와도 동작.
- [x] **테스트**: `EditorModeTests.cs` 8건(프리셋 번들, 가드, 플래그 오버라이드 우선순위, ReadOnly undo 클리어). 총 27→**35건 통과**.
- **참고**: 데모 `NativeEditor`의 자체 `EditorMode{ReadOnly,Simple,Full}`은 의미가 달라(Simple=툴바만 숨김) 그대로 유지.
- **비용**: 낮음. 구조 변경 없이 기존 코드에 분기 추가.

### 🔵 N3.6: 라이브러리 툴바 승격 + 모드 연동 (미착수, `0.2.0` 목표)

> **배경**: 거의 모든 소비 앱이 서식 툴바를 필요로 한다. 서식 툴바(B/I/U·글꼴·목록·정렬 등)는 컨트롤 *자신의 공개 명령*만 호출하므로 "에디터의 일부"이지 앱 셸이 아니다. 현재는 N0 분리(2026-06-08) 때 툴바가 데모 쪽(`NativeEditor.BuildToolbar`/`MainWindow`)에 남아 라이브러리 밖에 있다. → N3.5 모드 표의 "툴바" 열이 미구현인 근본 원인. 이를 라이브러리로 되돌려 **모드가 동작·컨텍스트 메뉴·툴바를 일관되게 지배**하도록 한다.
>
> **경계(중요)**: "서식 툴바"는 라이브러리(선택 계층), "앱 셸"(창·저장/열기·메뉴바·파일 다이얼로그)은 앱. 이 선을 지켜 비대화를 막는다.

- [ ] **3계층 구조** (소비자가 추상화 수준 선택, 셋 다 같은 패키지):
  | 계층 | 타입 | 용도 |
  |------|------|------|
  | ① 코어 | `RichEditor` (현행 유지) | 명령+상태+이벤트만. 완전 커스텀 UI를 만드는 소수용 |
  | ② 툴바 | `RichEditorToolbar` | 선택적 서식 툴바. `Target`으로 ①을 가리켜 명령 호출+모드 반영. 레이아웃은 소비자가 배치 |
  | ③ 번들 뷰 | `RichEditorView` | ①+②+스크롤러를 묶은 한 줄 drop-in. 가장 편한 기본값 |
- [ ] **연결 고리 = `Target` 속성**: `RichEditorToolbar.Target`(`StyledProperty<RichEditor?>`) 하나로 세 방향 연결.
  - 버튼 → 명령: `Target.ToggleBold()` 등 *기존 공개 명령* 호출.
  - 모드/플래그 → 가시성: `Target`의 `EditorMode`/`AllowImages`/`AllowTables` 구독 → 표/이미지 버튼 표시 토글(컨텍스트 메뉴가 쓰는 그 플래그 재사용, 새 로직 최소). ReadOnly=숨김/뷰어, Basic=서식 버튼만, Full=전체.
  - 선택 상태 → 버튼 표시: `Target.SelectionChanged` 구독 → 커서가 굵은 글자 위면 B 버튼 눌림 표시(토글 반영).
- [ ] **`NativeEditor` 승격**: 데모의 툴바 빌더(`NativeEditor.BuildToolbar`)를 라이브러리로 이관하되 데모 가정(한글 폰트 등) 제거. `FontFamilyChoices` 같은 기존 외부화 패턴 재사용.
- [ ] **현지화/오버라이드**: 라벨·아이콘 교체, 버튼 구성 커스터마이즈, 또는 툴바 무시(①만 사용) 가능하게.
- **설계 주의점**:
  - **선택 상태 반영엔 소폭 신규 API 필요**: 버튼이 명령을 *호출*하는 건 기존 명령으로 끝나지만, 현재 선택의 서식을 *반영*(B 눌림)하려면 "지금 선택이 Bold인가?" 조회 표면이 필요(현재 `SelectionChanged` 이벤트는 있으나 상태 조회 API 없음 → 예: `CurrentFormat` 신설). 비용 중간.
  - **스크롤러 소유권**: 스크롤은 ③(번들 뷰)만 품고, ①②는 스크롤 비소유로 분리(경계 명확화). 현재 `NativeEditor`가 스크롤러를 품고 있으므로([NativeEditor.cs](samples/AvaloniaRichEditor.Demo/NativeEditor.cs)) 승격 시 ③으로만 이전.
- **✅ 결정(2026-06-10): `0.1.0-alpha`에는 미포함, `0.2.0`으로.** 근거: ① alpha의 독자는 정의상 얼리어답터(부품 조립형 개발자)이고 데모에 동작하는 툴바 프로토타입이 참고 코드로 존재. ② 툴바에 필요한 `CurrentFormat` 등 신규 공개 API를 API 동결 가드 도입 전에 서두르면 동결 전에 표면만 넓히는 꼴. **`PublicApiAnalyzers` 도입(N2 잔여)을 0.2.0 진입 조건으로** 하여 "0.x = API 안정화" 선언과 순서를 맞춘다.
- **비용**: 낮음~중. 툴바 자체는 데모 프로토타입 존재, 가시성 로직은 기존 플래그 재사용. 선택상태 반영만 소폭 신규.

### 🟡 N4: 테스트 & CI (기반 완료 2026-06-08)
- [x] `tests/AvaloniaRichEditor.Tests`(xUnit) 신설 — **19개 테스트 통과**.
- [x] 단위 테스트: 표 병합/해제·행열 삽입삭제(`MergeCells`/`SpanOf`/`AnchorOf`/`IsCovered`), `TextRange`(GetText/Delete/ApplyPropertyValue), JSON 왕복(텍스트·서식·정렬·제목·표 병합 + 멱등), HTML 왕복(bold/list/table). 헤드리스 없이 순수 단위테스트로 동작.
- [x] **GitHub Actions**(`.github/workflows/ci.yml`): build+test **3-OS 매트릭스**(ubuntu/windows/macos) → N3의 mac/Linux 스모크 겸함. 태그(`v*`) 푸시 시 `dotnet pack` → 아티팩트 업로드(nuget push는 시크릿 추가 후 주석 해제).
- [x] **컨트롤 헤드리스 테스트**: xUnit **v3** 전환(테스트 프로젝트 Exe, 병렬화 off) 후 `Avalonia.Headless.XUnit`로 8개 추가 — InsertText, ToggleBold+Undo, InsertTable+Undo+Redo, LoadHtml/ToHtml, ToJson/LoadJson, GetPlainText, Clear. **총 27개 통과**. (렌더·히트테스트 픽셀 단언은 향후.)
- [x] CI 실제 실행 확인 — **저장소 푸시 후 3-OS 매트릭스 그린 확인 완료(2026-06-10)**. windows/ubuntu/macos 전부 build+test ✓.
- [x] **오프셋 모델 + 멀티문단 회귀 테스트(2026-06-10, N6-2 안전망)**: `TextRangeOffsetTests.cs` 10건 — 인라인 이미지=1글자(GetText 플레이스홀더 드롭+오프셋 보존, Delete가 이미지 제거/보존, ApplyPropertyValue 양측 스타일, GetRichRuns 이미지 스킵), 부분 서식 Run 분할, 멀티문단(GetText 개행 조인·Delete 중간 제거+양끝 병합·표 횡단 블록 제거·중간 문단 스타일). `TextRange`가 public이라 `InlineLen`/`BuildPlain`과 같은 오프셋 규칙을 모델 레벨에서 검증. **총 37→47.**
- [x] **컨트롤 편집 경로 + 붙여넣기 구성요소 테스트(2026-06-10)**: `RichEditorKeyInputTests.cs` 11건 — 라우티드 이벤트로 실제 OnKeyDown/OnTextInput 파이프라인 구동(Backspace 문자삭제·문단병합, Delete 전방병합+Undo, Enter 분할·제목→본문 리셋·빈 리스트 항목 탈출, 타이핑 Undo 코얼레싱·캐럿이동 시 런 분리, ReadOnly 차단). 레이아웃 의존 키(Home/Up/Down)는 회피, Ctrl+Home/Left/Right로 캐럿 제어. `RichEditorClipboardTests.cs` 6건 — CF_HTML 헤더 제거(`ExtractHtmlFragment` internal 승격) 3변형, `InsertHtml` 단일문단=인라인 병합/다중문단=블록 삽입+Undo. **총 47→64.**
- [ ] **잔여 갭(낮은 우선순위)**: async 클립보드 획득 체인(`TryGetDataAsync` 포맷 순회·폴백 순서) — 헤드리스 클립보드가 임의 포맷을 지원하지 않아 페이크 주입 구조 필요. 레이아웃 의존 키(Home/End/Up/Down 히트테스트)와 렌더 픽셀 단언은 향후.

### 🔵 N5: 견고성·성능 — **`1.0` 목표** (우선순위 5)
- [~] **Undo 입력 코얼레싱**: 연속 타이핑을 단일 체크포인트로(`PushUndoTyping`, 타이핑 1런=클론 1개. 캐럿 이동/선택/이산 편집 시 런 종료). 키 입력마다 전체 복제하던 최악 케이스 해소. (완전 델타/명령 기반 전환은 향후 — 이산 편집·삭제·서식은 여전히 op당 클론, 50벌 상한 유지.)
- [x] **접근성(프레임워크 천장 도달)**: `RichEditorAutomationPeer : ControlAutomationPeer, IValueProvider` — 컨트롤 타입 Edit, 값=문서 평문(`GetPlainText`), `IsReadOnly`/`SetValue`, `GetNameCore` 기본 이름. 스크린리더 내용 읽기/쓰기 가능. **전체 `ITextProvider`(캐럿/범위/속성)는 불가** — Avalonia 공개 automation 모델에 ITextProvider/ITextRangeProvider가 없음(Win32 COM interop 전용). Avalonia 내장 `TextBox`도 동일하게 IValueProvider만 노출. → Avalonia가 TextPattern을 추가하면 그때 확장.
- [~] **God-class 분해(진행 중)**: `RichEditor`를 `partial`로 전환, 컨텍스트 메뉴(`RichEditor.ContextMenu.cs`)·클립보드(`RichEditor.Clipboard.cs`)·렌더(`RichEditor.Rendering.cs`: Measure/Render/AutomationPeer) 분리(메인 ~3,760→3,162줄). 동작 불변(27 테스트 통과). 입력/표 추가 분리는 점진 진행(이후 영역은 입력↔히트테스트↔편집이 섞여 있어 신중히).
- **검증**: 수백 페이지 문서에서 타이핑/스크롤 지연 측정, 메모리 상한 확인.

### 🔵 N6: 이미지 저장 모델 전환 및 성능 최적화 (미착수)

> **배경**: 현재 이미지는 `Bitmap` 객체가 데이터 주체이며, 저장 시 매번 PNG로 재인코딩된다. 원본이 JPEG(~80KB)여도 PNG(~500KB)로 부풀고, 직렬화마다 인코딩 비용이 발생한다. 외부 의존성 추가 없이(Avalonia 내장 + .NET 내장만) 용량·속도·화질을 동시에 개선한다.

#### 🟢 [완료] N6-1: JSON 스키마 버전 필드 (2026-06-10, alpha 선행)
- [x] `FlowDocumentDto`에 `Version` 필드 추가(`CurrentSchemaVersion=1`, Serialize가 기록).
- [x] 역직렬화 시 버전 미존재 → 초기값 `1`로 폴백(기존 문서 하위 호환). 테스트 2건(쓰기 포함·레거시 로드) 추가, 총 35→37.
- **목적**: 이후 스키마 변경(RawBytes, MimeType, 이미지 해시 참조 등)의 마이그레이션 경로 확보.
- **티어 변경 사유**: alpha 사용자가 `ToJson()`으로 문서를 저장하기 시작하는 순간 스키마는 사실상 동결된다. "NuGet 배포 전 필수"이므로 1.0이 아니라 **첫 공개(alpha) 전**에 있어야 한다. 30분 작업.

#### N6-2: `byte[]` 중심 이미지 모델 (핵심, **선행: 테스트 보강**)
> **착수 조건**: 직렬화 왕복(이미지 포함 문서)·오프셋 모델(`InlineLen`/`BuildPlain`) 회귀 테스트를 **먼저** 추가한 뒤 착수. 백로그의 "테스트 보강은 구조 변경의 안전망" 원칙을 순서로 강제. (N5의 God-class 추가 분해도 동일하게 테스트 뒤로.)

- [ ] `ImageBlock`/`InlineImage`에 `byte[] RawBytes` + `string MimeType` 속성 추가.
- [ ] `Bitmap`은 렌더 캐시로 격하: `Bitmap? _cachedBitmap` — 첫 렌더 시 `new Bitmap(new MemoryStream(RawBytes))`로 지연 생성.
- [ ] **붙여넣기/드롭 경로 수정**: 원본 바이트를 Bitmap 디코딩 전에 캡처하여 `RawBytes`로 보관. 원본 포맷(JPEG/PNG/WebP 등) 유지.
- [ ] **리사이즈 경로**: 이미지 폭이 콘텐츠 폭(~754px) 또는 1920px 상한 초과 시 `CreateScaledBitmap` → `Bitmap.Save(Stream)`으로 PNG `byte[]` 생성. 리사이즈 1회만 수행, 이후 드래그 핸들 크기 조절은 `Width`/`Height` 값만 변경(세대 손실 없음).
- [ ] **Clone/Undo**: `RawBytes = this.RawBytes`로 참조 공유. 추가 메모리 없음.
- [ ] **직렬화 수정**: `BitmapToBase64` → `Convert.ToBase64String(RawBytes)` 직행(인코딩 제거). `InlineDto`/`BlockDto`에 `MimeType` 필드 추가. 기존 문서(`MimeType` 없음)는 `image/png`로 폴백.
- [ ] **HTML 출력**: `data:image/{MimeType};base64,...`로 원본 포맷 반영.

| 항목 | 현재 | 개선 후 |
|------|------|---------|
| 저장 속도 | 이미지당 PNG 인코딩 수십~수백ms | base64 변환만 (~1ms) |
| 저장 용량 (사진 10장) | ~6.5MB (전부 PNG) | ~1.3MB (JPEG 원본 유지) |
| 문서 열기 | 모든 이미지 즉시 Bitmap 디코딩 | 화면 표시 시 지연 디코딩 |
| 리사이즈 화질 | 세대 손실 가능 | Width/Height만 변경, 원본 보존 |
| Undo 메모리 | Bitmap 참조 공유 (양호) | byte[] 참조 공유 (동일) |
| 외부 의존성 | 없음 | 없음 (Avalonia 내장만) |

#### N6-3: 직렬화 비동기화
- [ ] `Serialize`/`Deserialize`를 `Task.Run`으로 백그라운드 스레드에서 실행.
- [ ] 대용량 문서(이미지 다수 포함)에서 저장/열기 중 UI 프리징 방지.

#### N6-4: 이미지 중복 제거 (해시 참조)
- [ ] `SHA256(RawBytes)` 해시로 동일 이미지 식별.
- [ ] JSON에 이미지 풀(pool) 섹션을 두고 바이트는 한 번만 저장, 블록에서는 해시로 참조.
- [ ] 같은 로고/스크린샷을 반복 사용하는 문서에서 용량 대폭 감소.

#### N6-5: 렌더링 가상화 (보류)
- [ ] 뷰포트 밖 블록의 `Draw` 호출 생략.
- 레이아웃 캐싱(완료)으로 셰이핑 비용은 이미 제거됨. 남은 건 Draw 호출 비용.
- **난점**: LayoutTransform(줌) + ScrollViewer 좌표 + 히트테스트 3곳(`GetPositionFromPoint`/`GetBlockAtPoint`/`GetLinkRunAtPoint`)의 일관성을 맞춰야 함.
- **착수 기준**: 수백 페이지 초대형 문서에서 프레임 드랍이 실측될 때.

#### N6-6: 대용량 문서 소프트 제한 (N6-2 이후 실측 후 결정)
- [ ] N6-2(byte[] 이미지 모델) 적용 후 10장/20장/50장/100장 분량에서 타이핑 지연·스크롤 프레임·저장 속도 실측.
- [ ] 실측 결과에 따라 **모드별** 임계값 결정. 예상 기준:
  | 모드 | 권장 상한 (경고 표시) | 최대 상한 (알림) | 근거 |
  |------|:-------------------:|:--------------:|------|
  | Full (편집) | ~20장 | ~50장 | Undo Clone + Draw + 저장 인코딩 전부 적용 |
  | Basic (간편 편집) | ~30장 | ~50장 | Undo Clone + Draw (표/이미지 삽입 없음) |
  | ReadOnly (뷰어) | ~50장 | ~100장+ | Draw만 남음 — Undo/입력/저장 병목 전무 |
- [ ] `MaxRecommendedLength` StyledProperty로 소비자가 임계값 조절 가능하게.
- **방침**: 하드 제한(입력 거부)이 아니라 **소프트 제한(경고)**. 데이터 손실 없음.
- **배경**: 가상화 없는 현재 아키텍처에서 편집 모드의 병목은 Undo Clone·입력 처리·이미지 저장 인코딩 3가지인데, ReadOnly에서는 전부 사라지고 Draw 호출만 남는다. 레이아웃 캐싱이 적용되어 있으므로 뷰어 용도는 상한이 훨씬 높음. 경쟁 비교 결과 무료/내장 에디터(웹 기반, WPF RTB 등)도 100장 이상에서 동일하게 고전.

#### N6-7: `.ardx` 패키지 파일 포맷 (선택, **N6-2 의존**)

> **방침**: 기존 JSON **문자열** 계약(`ToJson()`/`LoadJson()`)은 **그대로 유지**(이식·임베드·DB TEXT 컬럼·diff 용도). 그 **위에** 파일 저장용 ZIP 컨테이너 포맷을 **추가**한다. 치환이 아니라 계층 추가.

- [ ] **파일 포맷 `.ardx`** (Avalonia Rich Document, ZIP 컨테이너 — `System.IO.Compression.ZipArchive`, 외부 의존성 없음·AOT 호환):
  ```text
  내문서.ardx  (ZIP)
   ├── document.json   ← 뼈대+텍스트 (ToJson 재사용, 이미지는 인덱스 참조만)
   ├── images/img_001.jpg / img_002.png  ← 원본 byte[] 그대로
   └── meta.json       ← (선택) 저자/생성일/schema version
  ```
- [ ] **추가 API**: `Task SavePackageAsync(Stream)` / `Task LoadPackageAsync(Stream)`. `ToJson`/`LoadJson`은 변경 없음.
- [ ] **이미지 엔트리는 무압축(Stored, `CompressionLevel.NoCompression`)**: JPEG/PNG는 이미 압축돼 있어 Deflate해도 용량 ~0%·CPU만 낭비. ZIP의 이득은 "압축"이 아니라 **base64 제거(텍스트 ~33% 오버헤드 소거) + 지연 디코드**.
- [ ] **지연 로딩**: `document.json`으로 뼈대 먼저 렌더 → 이미지 바이트는 백그라운드(N6-3 비동기와 연계)에서 디코드해 채움.
- **의존성**: N6-2(원본 byte[] 보존)가 선행 필수 — 그래야 "원본 JPEG 무손실 저장"이 성립(현재는 PNG 재인코딩이라 불가).
- **참고**: DB(SQLite) 저장 용도라면 `.ardx`보다 `ToJson()` 문자열을 TEXT 컬럼에 넣는 편이 검색 텍스트 분리·쿼리에 유리. `.ardx`는 **파일로 주고받는** 시나리오용.

---

### ✅ 배포 전 최종 체크리스트 (`0.1.0-alpha`)
- [ ] **GitHub 저장소 푸시 + CI 첫 실행 그린** (위 "🚨 최우선" 절 — 모든 잔여 항목의 선행 조건)
- [x] **N6-1: JSON 스키마 버전 필드(`Version`)** (2026-06-10) — 레거시 폴백 + 테스트 2건.
- [x] N1(패키징, SourceLink 포함) + N2(최소 공개 API/문서) + N3(Windows 동작 보장, 타 플랫폼 명시) 완료
- [ ] `dotnet pack -c Release` 성공, 빈 앱에서 설치·호스팅 성공
- [ ] README의 사용 예제가 실제로 컴파일/동작
- [ ] LICENSE·저작권·서드파티(HtmlAgilityPack) 라이선스 고지
- [ ] 버전 `0.1.0-alpha`, 변경 이력(CHANGELOG) 시작
- [ ] (권장) NuGet 푸시 전 별도 테스트 계정/프리릴리스 채널로 1차 공개

### ✅ `1.0` 프로덕션 체크리스트
> 새 기능 추가 없이 기존 기능의 **안정성·성능·문서화**를 프로덕션 수준으로 끌어올린다.

**안정성 (성능보다 먼저 — N6-2의 안전망):**
- [x] 테스트 커버리지 확대 — **핵심 편집 경로 완료(2026-06-10, 37→64)**: 오프셋 모델·멀티문단 삭제/스타일·부분 서식 분할 10건 + 키 입력 파이프라인(Backspace/Delete 병합, Enter 분할, Undo 코얼레싱) 11건 + 붙여넣기 구성요소(CF_HTML, InsertHtml) 6건. **N6-2 착수 조건 충족.** 잔여(낮은 우선순위): async 클립보드 획득 체인·레이아웃 의존 키·렌더 픽셀 단언.
- [ ] CI 3-OS 매트릭스 그린 확인 (GitHub 푸시 후 — alpha 체크리스트에서 선행 처리됨)

**성능 (테스트 보강 후 착수):**
- [ ] N6-2: `byte[]` 이미지 모델 전환 (원본 바이트 보존, 지연 Bitmap 캐시, 외부 의존성 없음)
- [ ] N6-3: 직렬화 비동기화 (저장/열기 백그라운드 스레드)
- ~~N6-1: JSON 스키마 버전 필드~~ → **`0.1.0-alpha` 체크리스트로 이동** (2026-06-10)

**문서화·API:**
- [ ] 공개 멤버 XML 문서 주석 완성 (기존 `ToggleBold` 등 포함)
- [ ] API 동결 가드: `Microsoft.CodeAnalysis.PublicApiAnalyzers` 도입

**1.0 이후 (2.0+) 후보:**
- N6-4 이미지 중복 제거, N6-5 렌더링 가상화, 블록 여백 제어, DOCX 파싱, 마크다운, 델타 Undo, 동시편집, 페이지네이션, 플러그인 시스템.

### ❗ 출시 전 결정 필요 (Open Decisions)
- 라이선스 종류(MIT 권장?), 패키지 ID 최종(`AvaloniaRichEditor` 선점 여부 확인), 지원 Avalonia 버전 범위, 크로스플랫폼 보장 수준(알파에서 Windows-only로 갈지).

---
**마지막 업데이트**: 2026년 6월 10일 (5차) — **테스트 갭 마저 처리**: 키 입력 파이프라인 11건(라우티드 이벤트로 OnKeyDown/OnTextInput 실구동 — Backspace/Delete 문단 병합, Enter 분할·제목 리셋·리스트 탈출, Undo 코얼레싱, ReadOnly) + 붙여넣기 구성요소 6건(CF_HTML 추출 — `ExtractHtmlFragment` internal 승격, `InsertHtml` 인라인 병합/블록 삽입 계약). 총 47→64. **1.0 안정성 "테스트 커버리지 확대" 완료 — N6-2 착수 조건 충족.** / (4차) — **테스트 보강(N6-2 안전망)**: `TextRangeOffsetTests.cs` 10건 추가(인라인 이미지 오프셋 모델·멀티문단 삭제/스타일·표 횡단·부분 서식 분할). 총 37→47 통과. 남은 갭은 `RichEditor` private 편집 경로·붙여넣기 폴백(클립보드 페이크 필요)으로 기록. / (3차) — **N1 마무리 + N6-1 + CHANGELOG**: SourceLink/RepositoryUrl(SDK in-box, nuspec에 repo+branch+commit 확인), N6-1 JSON 스키마 버전 필드(레거시 폴백, 테스트 35→37), `CHANGELOG.md` 시작, `v0.1.0-alpha` 태그 → CI `Pack` 잡 그린(아티팩트 생성). **결정: `PackageIcon`+nuget.org 게시는 API 안정화/키 발급 시점까지 함께 보류**(게시 비가역성·아이콘 의미 시점 일치). 현재 `0.1.0-alpha`는 자기완결적 마일스톤. / (2차) — **최우선 항목 실행**: GitHub 저장소 `centwon/AvaloniaRichEditor`(Private) 생성·푸시. 초기 커밋의 ~240MB 빌드 산출물(`bin`/`obj`)을 `filter-branch`로 히스토리에서 제거. **CI 3-OS 매트릭스 첫 실행 그린**(windows/ubuntu/macos) → N3 mac/Linux 스모크 + N4 CI 그린 동시 해소. 남은 차단: Public 전환 → SourceLink/nuget 게시. / 같은 날 (1차) — **로드맵 적정성 점검 반영(4건)**: ① GitHub 저장소 푸시를 "🚨 최우선" 독립 항목으로 승격(단일 차단점 — N1/N3/N4 잔여+alpha 체크리스트 전체가 이것에 막힘, CI는 미실행 상태). ② N6-1(JSON 스키마 버전)을 1.0 → `0.1.0-alpha` 체크리스트로 이동(alpha에서 JSON 저장이 시작되면 스키마 사실상 동결). ③ N6-2 착수 조건으로 테스트 보강 선행 명시(1.0 체크리스트도 안정성→성능 순서로 재배열). ④ N3.6 툴바는 0.1.0 미포함·0.2.0 확정, `PublicApiAnalyzers`를 0.2.0 진입 조건으로. / 이전: 2026년 6월 9일 — **N3.6 추가**: 라이브러리 툴바 승격 + 모드 연동(3계층 `RichEditor`/`RichEditorToolbar`/`RichEditorView`, `Target` 연결, 모드/플래그→버튼 가시성, 선택상태 반영·스크롤러 주의점). N3.5 모드 표에 "툴바" 열 추가(의도, 미구현). / **N6-7 추가**: `.ardx` 패키지 파일 포맷(ZIP 컨테이너, JSON 문자열 계약 유지 + 파일 저장 API 추가, 이미지 무압축 Stored, N6-2 의존). / **N3.5 에디터 모드 완료**: `EditorMode` 프리셋(ReadOnly/Basic/Full)+기능 플래그 4종(`AllowImages`/`AllowTables`/`AllowRichPaste`/`AllowFindReplace`), 붙여넣기·드롭·삽입명령·컨텍스트메뉴·찾기바꾸기 가드, ReadOnly 최적화(undo/IME/캐럿타이머 비활성). 테스트 27→35건. / 이전: 2026년 6월 8일 — **N6 성능 최적화 로드맵 추가**: 이미지 저장 모델 전환(`Bitmap`→`byte[]` 중심, 원본 바이트 보존, 지연 Bitmap 캐시), JSON 스키마 버전, 직렬화 비동기화, 이미지 중복 제거(해시), 렌더링 가상화. 백로그에 사용성 후보(블록 여백·DOCX 파싱·마크다운) 및 구조 기반(테스트 보강·크로스플랫폼 실검증) 정리. 외부 의존성(SkiaSharp/ImageSharp) 추가 없이 Avalonia 내장만으로 진행 결정. / 이전: N0~N5 + Phase 1~6 완료 상태
