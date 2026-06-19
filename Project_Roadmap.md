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
- **✅ 블록 여백(Margin) 조정 — 해소(2026-06-12)**: 백로그 "블록 여백 제어" 항목으로 구현 완료(`Block.MarginTop/Bottom` 승격 + `Paragraph.MarginRight` + 컨텍스트 메뉴 프리셋 UI). 상세는 백로그 절 참고.

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

#### 🔍 2026-06-12 전수 점검 백로그 (코드 리뷰로 발견 — 미착수)
> 핵심 소스 ~5,000줄 직접 검토 결과. 항목별 파일:줄 위치 명시. P1부터 처리 권장.

**P1 — 버그, 저비용·고체감 — ✅ 전부 해소(2026-06-12, 테스트 133→134 그린)**
- [x] **B1. `SplitRunAtOffset`이 `FontFamily`/`Background` 누락** — `TextRange.cs`. 선택 경계 분할의 수동 new Run을 `run.Clone()`으로 교체(같은 역할의 `RichEditor.SplitInlinesAt`과 통일). 회귀 테스트 `ApplyPropertyValue_SubRange_SplitKeepsFontFamilyAndBackground` 추가.
- [x] **B2. `CopySelectionToClipboard`가 `async void`** — `RichEditor.cs`. 다른 프로세스가 클립보드 점유 시 `SetTextAsync` 예외 → 프로세스 크래시이던 것을 try/catch로 보호(이미지 경로와 동일 관례).
- [x] **B3. 히트테스트 배경 2,000px 하드코딩** — `RichEditor.Rendering.cs`. y>2000 빈 영역 클릭이 안 잡히던 투명 fill을 `new Rect(Bounds.Size)`로.
- [x] **B4. "서식 지우기"가 글꼴·형광펜 미초기화** — `ClearFormatting`에 `Background=null`, `FontFamily=null` 추가.

**P2 — 버그, 중간 작업량**
- [x] **B5. HTML 붙여넣기 원격 이미지를 UI 스레드 동기 다운로드 (2026-06-12)** — 정적 `HttpClient` 공유(소켓 누수 제거) + **붙여넣기(ParseHtml 1회)당 총 5초 예산**(`[ThreadStatic]` 데드라인, 초과분 이미지는 생략하고 나머지 콘텐츠는 유지). 종전엔 이미지당 5초 × N이라 UI가 수십 초 멈출 수 있었음. 완전 async화는 모델 객체 UI 스레드 제약(핵심 규칙 8) 때문에 보류.
- [x] **B6. 서로게이트 쌍(이모지) 미처리 (2026-06-12)** — `PrevCharBoundary`/`NextCharBoundary` 헬퍼로 Backspace/Delete/←/→가 쌍을 1글자로 취급(반쪽 서로게이트 잔류 → 깨진 글리프 방지). 회귀 테스트 2건(총 136). ZWJ 시퀀스 등 완전한 그래핌 클러스터 단위는 추후(현재는 쌍 단위로 손상만 방지).
- [x] **B7. 표 셀 횡단 선택 삭제 시 셀 내용 병합 (2026-06-12)** — `TextRange.Delete`: 양 끝점 중 하나라도 셀이면 `MergeParagraphs` 생략(그리드 횡단 텍스트 이동 방지), 대신 끝점 사이의 완전 포함 문단(셀)을 비움. 셀 구조 보존. 회귀 테스트 1건.
- [x] **B8. 공개 API의 ReadOnly 가드 불일치 (2026-06-12)** — `InsertText`/`PasteFromClipboardAsync`/`ApplyStyleToSelection`(ToggleBold 계열 전부)/`Indent`/`SetTextAlignment`/`SetLineHeight`/`SetListType`/`SetHeading`/`SetHyperlink`에 `IsReadOnly` 가드 — `InsertImage`/`InsertTable`과 일관화.
- [x] **B9. TSV 휴리스틱 과민 (2026-06-12)** — `LooksTabular`(internal 승격): 모든 비어있지 않은 줄에 탭 필수 + 한 줄 이상이 비공백 셀 2개 이상이어야 표 판정. 탭 들여쓰기 코드(`"\tfoo"`) 오판 제거. 테스트 2건.
- [x] **B10. 자동 리스트 접두사 잔여물 (2026-06-12)** — `DeleteLocalText`를 다중 런 횡단 삭제로 재작성(원본 좌표 기준 구간 삭제, 단일 런 조기 반환 제거). 부수 효과: Backspace/Delete 경로도 런 경계 안전. 테스트 1건. **P2 전체 해소 — 테스트 136→140 그린.**

**P3 — 성능 — ✅ 전부 해소(2026-06-12, 테스트 140→142 그린)**
- [x] **F1. Backspace/Delete 언두 코얼레싱** — `_typingRun` 불리언을 `EditRunKind`(None/Typing/Backspace/Delete) 런 추적으로 일반화. 단순 1글자 Backspace/Delete 연타는 런당 클론 1개(`PushUndoDeleting` + `_editRunRearm` — 삭제 키는 캐럿이 움직이므로 핸들러가 ResetCaretBlink 전에 런을 재무장). 구조적 삭제(선택/병합/블록/Enter)는 종전대로 키당 체크포인트. 회귀 테스트 2건.
- [x] **F2. 입력/렌더 경로 할당 제거** — Cursor 7종 정적 캐시(`OnPointerMoved`가 마우스 이동마다 native 자원을 생성하던 것), Render 고정색 브러시/펜 9종을 정적 `ImmutableSolidColorBrush`/`ImmutablePen`으로(2Hz 캐럿 블링크마다 재할당 제거). `CaretBrush` 펜은 동적 속성이라 유지.
- [x] **F3. `ParagraphSig`의 지연 디코드 강제 제거** — 인라인 이미지 식별을 `RawBytes` 참조 해시 우선으로(Image 게터는 RawBytes가 없을 때만 — 그 경우 디코드 없음).

**P4 — 기능 후보 (소형) — 3건 구현(2026-06-12, 테스트 142→144), 2건 보류**
- [x] **Shift+Enter 소프트 줄바꿈** — 최상위 문단에서 `\n` 삽입(셀 안 Enter와 동일 경로). 문단 분할 없이 한 문단 여러 줄.
- [x] **Ctrl+Shift+V 서식 없이 붙여넣기** — `PastePlainTextAsync`(private): 평문만, TSV→표 휴리스틱도 미적용("평문 붙여넣기"는 구조를 만들지 않음).
- [x] **URL 자동 링크화** — `TryAutoLink`: URL 뒤 공백 입력 시 직전 토큰(http/https + 호스트 `.` 필요)에 `NavigateUri`. 공백 삽입 *후* 토큰 범위에만 적용해 공백이 링크를 상속하지 않음. 이미 링크면 무시.
- [x] **pending caret format — Word식 채택(사용자 결정, 2026-06-12)**: 선택 없는 서식 토글은 ① 캐럿이 단어 안이면 그 단어에 적용(`WordBoundsAt` 재사용), ② 빈 위치면 보류 서식(`_pendingCaretStyles`)으로 다음 입력에 적용(캐럿 이동 시 해제, `GetCaretFormat`이 클론 프로브로 미리 반영 — 툴바 즉시 표시). 보류 상태는 문서 무변경이라 undo 체크포인트는 적용 시점(타이핑 코얼레싱)에 합류. 기존 "문단 전체 적용" 제거. 테스트 3건.
- [x] **HTML `file:` 이미지 로드 옵션화 — 기본 허용 채택(사용자 결정, 2026-06-12)**: `AllowLocalFileImages` StyledProperty(기본 true, 프리셋 번들 미포함 — 보안 플래그) + `ParseHtml(html, allowLocalFileImages=true)` 파라미터(LoadImage까지는 ThreadStatic으로 전달 — 원격 데드라인과 동일 패턴). 붙여넣기/`LoadHtml`/`InsertHtml` 3개 인제스천 경로 모두 적용. PublicAPI 4건 갱신. 테스트 1건(1×1 PNG 실파일 허용/차단). **점검 백로그 전 항목 완료 — 테스트 144→148 그린.**

**문서 후속**
- [x] **명세-코드 표류 감지 (2026-06-14)** — `--roundtrip` CLI 대신 **테스트**로 구현(CI는 `dotnet test`만 돌리므로 표류를 CI에서 잡으려면 테스트가 맞는 그릇). `docs/DOCUMENT_FORMAT.md` §2.6 예제를 자체 일관성 있는 로드 가능 JSON으로 확정(1×1 PNG 실바이트 + 매칭 SHA-256 풀 키)하고, `DocumentFormatSpecTests`가 문서에서 ```json 펜스를 추출→`Deserialize`→문서화된 구조(제목 문단·이미지 풀 해석·1×2 표) 단언. 필드명/판별자 표류 시 실패.

**🔍 2026-06-15 추가 리뷰 (코드 전수 재점검 — 핵심 엔진+포매터)**
> 성능/정합성 수정과 기능 3건 반영(테스트 195→199 그린), 기능 후보 3건 보류.

- [x] **성능**: 드래그선택 hit-test 캐시 신뢰(마우스 이동마다 전체 `ParagraphSig` 재해시 제거), `MeasureOverride`가 편집無일 때 캐시 신뢰(캐럿 이동마다 전체 재해시 제거), `GetStatus` 단일패스(전체 문서 문자열+`Split` 할당 제거), `FindCore` 조기종료(전체 매치 리스트 materialization 제거), `LayoutTable` 2단계 캐시(같은 startX·다른 top일 때 셀 재측정 생략 → 페이지뷰 표 스래싱 해소).
- [x] **정합성**: `TextRange`를 `LogicalCells()` 기준 통일(병합표 인덱스 일치), `ToHtml` 속성값(NavigateUri/FontFamily) 이스케이프, `TableBlock.InsertColumn` 너비 인덱스 정합, JSON 표 로드 직사각형 보장(짧은 행 패딩).
- [x] **텍스트 추출 줄바꿈**: `GetPlainText`/클립보드 평문이 플랫폼 줄바꿈(LF→CRLF on Windows) + 선행 빈 단락 보존, `InsertText`가 붙여넣기 CRLF를 `\n`으로 정규화. (Windows 소비자에서 한 줄로 보이던 문제 해소. 회귀 테스트 2건.)
- [x] **복사 시 HTML 서식 내보내기** — `CopySelectionToClipboard`가 평문 + Windows **CF_HTML**(`DataFormat.CreateBytesPlatformFormat("HTML Format")`, 시스템명 검증)을 함께 올림 → Word/브라우저로 붙여넣을 때 서식 보존. 기존 `ToHtml()` 재활용, `BuildCfHtml` 봉투(UTF-8 바이트 오프셋) 단위 테스트 2건(round-trip + 한글 멀티바이트). 입출력 비대칭(읽기는 HTML 파싱, 쓰기는 평문) 해소.
- [x] **리스트 마커가 항목 서식 따름** — `DrawListMarker`가 고정 14pt/검정 대신 항목 첫 런의 크기/폰트/굵기/색 사용.
- [x] **접근성** — `IsReadOnly` 토글 시 자동화 피어가 상태 변경 통지(`RaisePropertyChangedEvent`). **단 Avalonia 12 공개 자동화 모델에 `ITextProvider`/`ITextRangeProvider`가 없어 캐럿/선택/줄 단위 노출은 불가**(`IValueProvider`가 천장). 키 입력마다 Value 통지는 전체 문서 재낭독을 유발하므로 의도적 생략. → **프레임워크 한계로 기록.**

**보류 (3건, 2026-06-15 — 양 대비 가치 낮음 또는 대형)**
- **단락 경계 넘는 Find**: 전체 문서를 `\n`으로 이어붙인 문자열 + "인덱스→(단락,오프셋)" 역매핑 필요(소~중, ~50–80줄). 그러나 Enter가 단락을 나누므로 *줄바꿈을 포함한* 검색어만 해당 — 실사용 빈도 극저. 가성비 낮아 보류.
- **ReplaceAll 진정한 O(n)**: 현재 `FindCore` 조기종료로 일반 문서는 충분히 빠름. 진짜 O(n)은 매치 일괄수집+역순 치환 또는 단락 in-place 재작성(중, ~80–120줄, 서식 보존 유지가 까다로움). "초대형 문서 + 수천 매치"라는 드문 조건에만 이득 — 보류.
- **벡터(선택 가능) PDF**: Avalonia가 PDF DrawingContext 백엔드 미제공 → content stream 직접 생성 + **폰트 서브셋팅**(CJK 글리프 수천 개 = 난제). "무의존성 + AOT" 방침과 충돌. 자체 구현 비추천 — 필요 시 외부 PDF 라이브러리 도입 검토. 현 래스터 PDF는 합리적 v1. 보류.

**🔍 2026-06-15 후속 (글루 파일 전수 + 복사 HTML 실앱 튜닝 + 쪽 윤곽 여백)**
- [x] **미정독 글루 파일 리뷰 — 버그 없음**: `PdfWriter`(xref 오프셋/객체번호/zlib 정확), `DocumentPackage`(.flow zip, MIME 폴백/예외처리), `ContextMenu`(표 그리드·이미지·병합 가드), 작은 모델(Block/ImageBlock/TextPointer/ImageMime) 전수 확인. (대형 순수 UI인 `RichEditorView`/`Toolbar`는 폭맞춤 로직만 확인.)
- [x] **복사 HTML 실앱 튜닝(Word/HWP 반복 검증)**: 큰따옴표 속성 + 글꼴명 인용(다단어 CSS 유효화) + `pt` 크기 + `<s>`/`<u>` 태그 + `list-style-type` 명시 + 문단속성/표/인라인이미지 보존. **결론: Word와 HWP의 클립보드 CSS 지원이 상충**(Word는 font-family/pt 수용·색/취소선 무시, HWP는 반대)해 단일 CF_HTML로 양쪽 완벽 재현 불가 → **알려진 한계로 기록**, 추가 튜닝(`<font>` 태그 동원 등)은 가치 대비 비용으로 보류. 굵게/기울임/표/이미지/리스트/정렬은 양쪽 정상.
- [x] **인라인 이미지 복사 stale 버그 수정**: 이미지만 선택 시 평문이 비어 시스템 클립보드를 안 set → 이전 복사본이 붙던 것을, text/html 중 하나라도 있으면 set하도록. (in-app 인라인이미지 붙여넣기 자체는 `InsertRuns`/`InsertParsedDocument`가 런만 처리하는 기존 한계로 별개 — 이미지 빠짐, stale은 해소.)
- [x] **쪽 윤곽 회색 여백 축소(사용자 요청)**: `PageGap` 24→3(≈2pt) — 상/하/페이지 사이. 좌우 데스크는 `RichEditorView.ApplyFitWidth`의 하드코딩 `deskGap=24`가 원인이라 `RichEditor.PageGap` 직접 참조로 연동(주석은 "mirrors PageGap"인데 실제로 안 따라가던 것). 상수 1개로 사방 일괄 조정 가능.

**🔍 2026-06-18 전 소스 정독 리뷰 (0.7.0 후속, 버그 5 + 성능 4 — 테스트 256→260 그린)**
> 라이브러리 전 파일(`src/AvaloniaRichEditor`) 정독 후 발견 항목을 위험 대비 효용으로 묶어 일괄 처리. 크래시급 없음.
- [x] **버그**: ① HTML 가져오기 폰트 크기 — `ParseInlines` 기본값이 pt 전환 후에도 옛 px 본문값 `14`에 멈춰 있어 **표 셀·인라인 래핑 텍스트가 14pt로** 들어오던 것을 본문 기본 10pt로(`HtmlDocumentFormatter`). ② RTF 왕복 이모지 소실 — 서로게이트 쌍을 `\u` 둘로 쓰는데 리더가 `ConvertFromUtf32`로 각 반쪽을 디코드하다 예외→소실하던 것을, `\u`를 UTF-16 코드 유닛으로 그대로 누적해 재결합. ③ 인라인 이미지 NaN 직렬화 — 블록 이미지와 달리 `NanToNull` 미적용이라 NaN이 `System.Text.Json`에 닿으면 예외 가능 → 대칭화. ④ 접근성 `SetValue` 줄바꿈 보존(한 줄로 합쳐지던 것을 문단별 `<p>`로). ⑤ `Ctrl+Del`/`Ctrl+Back`이 단락 경계에서 삭제할 게 없어도 빈 undo 체크포인트를 쌓던 것 수정.
- [x] **성능(유휴/입력 핫패스)**: ① 캐럿 펜 프레임마다 `new Pen` → 캐싱(`CaretBrush` 변경 시만 재생성). ② 선택 하이라이트가 Render마다 `IndexOf` 2회 → 단일 스캔(페이지뷰에선 보이는 페이지 수만큼 반복되던 부담). ③ `GetStatus`가 캐럿 이동마다 문단별 `BuildPlain` 문자열 할당 → 인라인 직접 순회로 할당 제거(대형 문서 방향키). ④ hover의 표 테두리 판정 `GetBlockAtPoint`+`GetTableRect` 두 번 순회 → 단일 순회(`TableLeftOrTopBorderAtPoint`).
- 회귀 테스트 4건(표 셀/인라인 10pt, RTF 이모지 왕복, NaN 인라인 이미지 직렬화). 성능 4건은 동작 보존이라 기존 스위트가 회귀 방지. README/`DOCUMENT_FORMAT.md`는 영향 없음(문서화된 동작·공개 API 무변경).

- **✅ 사용성(UX) 제안 — 전부 구현 완료** (2026-06-12 점검에서 확인, 항목별 완료 시점은 이전 작업들):
  - ✅ 더블클릭=단어 선택 / 세 번 클릭=문단 선택 (`RichEditor.Input.cs` ClickCount 분기)
  - ✅ 자동 목록: `- `/`* `/`N. ` + 공백 → 리스트 전환 (`TryAutoList`)
  - ✅ 서식 페인터 (`StartFormatPainter`/`IsFormatPainterActive`, 툴바 버튼)
  - ✅ 상태바: 글자/단어/줄/칸 (`GetStatus()`, 데모 상태바)
- **✅ 블록 캐럿 정비 완료(2026-06-12)**: 그림/표 앞·뒤 블록 캐럿 + Space 앞 여백(기존 동작 유지). 묵은 "↓로 표 뒤 진입 안 됨" 버그의 실체는 ① 표 뒤 캐럿이 표 **왼쪽** 모서리에 그려져 "표 앞"으로 보였던 렌더 문제 + ② →가 셀에서 나갈 때 표 뒤 캐럿을 건너뛰던 비대칭(`AdjacentBlock`이 최상위 블록 기준이라 셀에서 null). 수정: 표 뒤 캐럿은 오른쪽 아래 모서리에 렌더, **←/→는 셀을 통과**(표 앞 ↔ 첫 셀 … 마지막 셀 ↔ 표 뒤), **↑/↓는 표를 한 단위로 건너뜀**(표 앞 캐럿에서 ↓=아래 문단, 셀 진입은 →·Tab·클릭). 회귀 테스트 10건(`BlockCaretTests`) — 사용자 검증 완료.
- ~~**A4 페이지 레이아웃(추후)**~~ → **🖨️ P-마일스톤으로 승격(2026-06-12)** — 아래 "A4 페이지 보기 + 인쇄/PDF" 절 참고. (사용자 결정: 인쇄가 목표, 편집 뷰도 워드식 페이지, 출력=프린터+PDF 둘 다.)
- **IME 한글 기본 입력(시도→실패, 보류)**: IMM32 `ImmSetConversionStatus(IME_CMODE_NATIVE)` P/Invoke를 GotFocus + 시작시 자동포커스와 함께 시도했으나 **한국어 Win11에서 한글 전환 안 됨**(예상대로 TSF가 IMM32 변환모드 무시). 코드 제거함. 남은 대안: `SendInput(VK_HANGUL)`(토글이라 현재 상태 확인 필요·위험) 또는 TSF 인터롭(복잡). 실용성 대비 비용이 커서 보류. (시작시 에디터 자동포커스는 유지 — 클릭 없이 바로 입력 가능.)
- **✅ 글자처럼 취급(HWP식) 토글(2026-06-11)**: 이미지 우클릭 메뉴 체크 항목 "글자처럼 취급" — 블록 이미지 ↔ 인라인(1글자) 상호 전환(`ConvertImageBlockToInline`/`ConvertInlineImageToBlock`, internal). 블록→인라인은 이전 문단 끝에 앵커(없으면 다음 문단 앞), 인라인→블록은 문단 뒤 형제 삽입. 표 셀 안은 블록 형제 불가라 해제 비활성화. 바이트/MIME/크기 보존, 양방향 Undo. 테스트 4건 — 총 102건.
- **표 글자처럼 취급(보류, 2026-06-11 — 의향 있음)**: HWP식 인라인 표. 이미지와 달리 표는 내부 상호작용(셀 편집·캐럿·히트테스트·열 리사이즈)이 있어 원자적 `DrawableTextRun`으로 안 끝남. 정식 구현은 인라인 객체 일반화(중첩 히트테스트/캐럿 라우팅, 핵심 불변식 1·2·4 재작업) 필요 — 대형. 착수 시 별도 마일스톤으로 설계부터.
- **사용성(기능) 개선 후보**:
  - **✅ 블록 여백(Margin) 제어(2026-06-12)**: `MarginTop`/`MarginBottom`을 `Block`으로 승격(이미지·표·구분선 포함, 기본값=기존 룩), 왼쪽=기존 `Indent` 재사용, 오른쪽=`Paragraph.MarginRight`(줄바꿈 폭 축소 — 어울림이 없어 문단 전용). 레이아웃 워커 7곳 + 줄폭 7곳 일괄 반영(렌더-히트 일치), JSON nullable 필드로 레거시 호환, 우클릭 "여백" 서브메뉴(문단 4방향/이미지·표 3방향, 프리셋 라디오). 테스트 3건 — 총 133건.
  - ~~**DOCX/벡터 도형 클립보드 파싱**~~ → **조사 후 강등(2026-06-14, 실클립보드 덤프+붙여넣기 검증)**. 당초 전제("워드/한글이 OOXML `<w:tbl>`을 클립보드 텍스트로 올린다 → 파싱해 표/그림 보존")는 **부분적으로 틀림**. 실측 결과:
    - **EMF 디코더(당초 후보)는 불필요** — 도형은 `CF_ENHMETAFILE`이 아니라 **`Bitmap`(CF_BITMAP)으로 클립보드에 동봉**되고, 기존 비트맵 분기가 이미 처리. (HWP 글상자·Word 스마트아트=그림으로 정상 붙음.)
    - **표·일반 서식은 RTF/HTML이 이미 커버**(27차 RTF 포매터). OOXML 직접 파싱의 추가 가치 없음.
    - **HWP는 `DOCX Format` 패키지를 클립보드에 직접 올림**(OLE2 불필요) — "편집 가능 임포트"를 원하면 가능한 길이나 대형이고 현 갭과는 별개.
    - **유일하게 남은 실손실 = HWP 글맵시가 빈 결과로 붙어 화면에 안 보임**(RTF가 공백 문단을 만들어 비트맵 폴백 전에 return). Word 글상자/워드아트가 "텍스트만" 되는 건 글자 보존되는 우아한 강등이라 손실 아님. → **알려진 한계로 기록**(드문 케이스, 수정은 [RichEditor.Clipboard.cs:50](src/AvaloniaRichEditor/Controls/RichEditor.Clipboard.cs:50)의 `empty` 판정을 공백-only까지 좁히면 비트맵 폴백 가능 — 미착수).
  - ~~**마크다운 입출력**~~ → **제외(사용자 결정 2026-06-13)**. Export 손실성(표 병합·인라인 이미지·글자색 표현 불가)이 커서 가치 대비 우선순위 낮음.
- **구조적 기반** (성능 개선의 전제):
  - **테스트 보강**: 27개 테스트는 4,000줄+ 에디터 대비 낮은 수준. N6 이미지 모델 전환 등 구조 변경의 안전망 확보 필요.
  - **크로스플랫폼 실검증**: mac/Linux 스모크 테스트 미실행. GitHub 푸시 후 CI 3-OS 매트릭스로 확인.
- **남은 보류 항목**:
  - ~~정밀 인쇄(페이지네이션/PDF)~~ → 🖨️ P-마일스톤으로 승격(2026-06-12)
  - 외부 앱 실통합 (기능 플래그 롤아웃)

---

## 🖨️ P-마일스톤: A4 페이지 보기 + 인쇄/PDF (착수 2026-06-12)

> **사용자 결정(2026-06-12)**: 인쇄가 목표. 편집 뷰도 워드식 페이지 단위, 출력은 프린터 직접 인쇄 + PDF 둘 다.
> **설계 핵심**: 페이지네이터 **1개**를 만들어 4용도(편집 뷰·인쇄 미리보기·프린터·PDF)에서 공유한다.

### 설계 원칙 — "리플로우가 아니라 갭 주입"
편집 뷰 페이지화를 페이지별 재레이아웃(전면 리플로우)으로 하지 않는다. **기존 연속 레이아웃을 유지한 채 페이지 경계 위치에 세로 갭(페이지 사이 여백+크롬)을 주입하는 y-좌표 리매핑**으로 구현:
- 페이지 분할 위치는 **줄 경계**에서만(기존 `BuildTextLayout`의 줄 메트릭 사용) — 줄이 반으로 잘리지 않음. 문단이 페이지보다 길어도 줄 단위로 넘어감.
- 이미지·표는 원자 단위(다음 장으로 밀기). 페이지보다 큰 표는 v1에서 오버플로 허용(행 경계 분할은 후속).
- `MapDocToView(y)`/`MapViewToDoc(y)` **단일 choke point**를 렌더 + 히트테스트 3곳(`GetPositionFromPoint`/`GetBlockAtPoint`/`GetLinkRunAtPoint`) + 캐럿/선택 기하 + `BringIntoView`가 공통 사용 → 핵심 불변식 1(단일 TextLayout=진실의 원천) 유지, off-by-one 원천 차단.
- 페이지 보기 off(기본)면 리매핑이 항등함수 — 기존 호스트 동작 불변.

### 제약(조사 완료)
- **Avalonia 12에 인쇄 API 없음** → 페이지를 300DPI `RenderTargetBitmap`으로 렌더해 출력.
- **벡터 PDF는 v1 제외**: Avalonia `DrawingContext`를 PDF 캔버스로 백킹할 공개 API 없음. v1=래스터 PDF(300DPI, 인쇄 품질 충분 / 텍스트 선택·검색 불가, 파일 큼). 벡터화는 별도 후속 검토.
- **경계 규칙**: 페이지네이터·페이지 뷰·PDF 라이터(무의존 이미지 전용 PDF, 손작성 가능)=라이브러리. 프린터 전송(`System.Drawing.Printing`, Windows 전용)=데모/호스트 쪽 — 라이브러리 의존성 0 유지.

### Phases
- [x] **Phase 0 — "비율 줌 잘림" 실앱 검증 → 버그 아님(2026-06-12)**: 사용자 실앱 확인 결과 세로 스크롤·익스텐트·마지막 문단 도달 전부 정상. 불만의 실체 = **페이지 구분 부재**(한 장짜리 무한 종이) — 즉 Phase 2 그 자체. 별도 수정 없음. (부산물: 헤드리스 테스트 앱은 테마가 없어 ScrollViewer 템플릿이 안 붙음 → extent 검증류는 헤드리스 불가, 실앱 검증 필요 — 향후 참고.)
- [x] **Phase 1 — 페이지네이터 코어(2026-06-12)**: `RichEditor.Pagination.cs` — `ComputePageBreaks(contentWidth, pageContentHeight)`(internal) + A4 상수(794×1123@96DPI). `MeasureContentHeight` 워크를 정확히 미러링(동일 폭 식·동일 블록 높이), 원자=문단 한 줄(`TextLayout.TextLines`)/이미지/표/구분선 통째. 페이지 초과 원자는 단독 페이지+오버플로(v1 계약). 이미지 분기는 `Image` 게터 미접촉(디코드 프리, N6-2 규약). 테스트 6건(`PaginationTests`: 빈 문단 LineHeight 고정으로 산술 정확 4건 + 셰이핑 불변식 2건) — **총 148→154 그린.**
- [x] **Phase 2 — 편집 뷰 페이지 모드(2026-06-12, 사용자 검증 완료)**: `PageView` StyledProperty(기본 off=기존 동작 불변, PublicAPI 3건). 구현 = 설계대로 갭 주입: ① 렌더 걷기를 `DrawDocumentBlocks`로 추출(코드 이동, 내부 무변경) 후 페이지 모드에선 회색 데스크+흰 A4를 그리고 보이는 페이지마다 클립+이동변환 아래 걷기 재생(슬라이스=컬링 창, N6-5 그대로 작동) — 페이지에 걸친 문단은 클립이 줄 경계 분할. ② `MapDocToView`/`MapViewToDoc` 단일 choke point — 포인터 진입 2곳 view→doc 1회 매핑(이후 전부 문서 좌표), IME 후보창·BringIntoView·캐럿 바만 doc→view 역매핑, `_lastCaretPoint`는 항상 문서 좌표. ③ `ContentLayoutWidth`(페이지 모드=698 고정)를 렌더·측정·히트테스트 3곳·BlockAtY가 공유. 데모 "페이지" 체크박스(ko/en). **버그 2건 수정**: 줄 반토막(클립이 콘텐츠 박스 전체라 슬라이스가 짧게 끝난 페이지의 남는 공간에 다음 페이지 첫 줄이 비집고 들어옴 → 클립을 슬라이스 끝에서 절단) + 표 아래 여백 렌더만 하드코딩 10(블록 여백 마일스톤 누락분, `MarginBottom`으로 정렬). 테스트 4건(매핑 항등/왕복/갭 클램프/페이지 스택 측정) — **총 154→158 그린.** 잔여 한계(v1 계약): 페이지보다 큰 표/이미지는 종이 여백에서 클립(다운스케일 상한 1080px 이미지가 1043px 용량을 37px 초과하는 에지 포함).
- [x] **Phase 3 — 페이지 렌더 + 미리보기(2026-06-12, 사용자 검증 완료)**: 공개 API `GetPrintPageCount()`(인쇄는 PageView와 무관하게 항상 페이지네이션) + `RenderPrintPage(pageIndex, dpi=96)`(A4 한 장→`RenderTargetBitmap`, 300DPI≈2480×3508/~35MB라 장 단위 렌더·해제 권장을 XML 문서에 명시, Phase 2와 동일한 슬라이스 클립 규칙). `DrawDocumentBlocks`에 `chrome` 파라미터 — 인쇄 출력에서 선택 하이라이트·캐럿·IME preedit·이미지 테두리/리사이즈 핸들·핸들 레지스트리 기록 제외(표 격자선·셀 배경·리스트 마커·인용 바 등 콘텐츠는 유지). 데모 🖨 버튼 + `PrintPreviewWindow`(회색 데스크 페이지 스택, ko/en). PublicAPI 2건. 테스트 2건(페이지 수·96/300DPI 픽셀 크기+인덱스 가드 — 헤드리스 RTB 동작 확인) — **총 158→160 그린.**
- [x] **Phase 4 — 출력(2026-06-12)**: ① 공개 API `SavePdf(Stream, dpi=300)` — 자체 래스터 PDF 라이터(`Formatters/PdfWriter`, PDF 1.4 직접 작성, 페이지=풀블리드 RGB 이미지 1장, .NET 내장 `ZLibStream` Flate, 외부 의존성 0, 장 단위 렌더·해제로 메모리 1장분). ② 데모 미리보기 창에 출력 툴바 — 프린터 콤보(설치 목록+기본 자동 선택)+인쇄(`System.Drawing.Printing` 9.0, **데모 전용 의존성** — 장마다 300DPI 렌더 전송, 비Windows는 PlatformNotSupported 안내, CA1416 버전 가드)+PDF 저장(파일 피커). PublicAPI 1건, 테스트 1건(PDF 구조 파싱: 헤더/xref/페이지 수/Flate) — **총 160→161 그린, 경고 0.**
- [x] **후속 2건(2026-06-13, 사용자 검증 완료)**: ① **머리말/꼬리말/쪽번호** — `PageHeader`/`PageFooter`/`ShowPageNumbers` StyledProperty 3종(PublicAPI 9건). 종이 **여백 띠에만** 그려 페이지 분할 무영향(`DrawPageMarginChrome` — 페이지 뷰·`RenderPrintPage` 양쪽 호출, 11px 회색, 헤더/푸터=왼쪽·쪽번호="N / 총수" 오른쪽). 데모는 쪽번호 기본 on. ② **표 행 경계 페이지 분할** — 페이지네이터의 표 원자를 표 전체→**행 단위**로(워드 기본 동작, `LayoutTable.RowY` 차분). 렌더는 Phase 2 클립+리플레이 구조 덕에 무수정 자동 분할. 테스트 2건(행 분할 산술·여백 크롬 스모크) — **총 161→163 그린.** CHANGELOG 갱신 + 푸시·CI 3-OS 그린(2026-06-12).
- **벡터 PDF(텍스트 선택·검색 가능) — 보류, 결정 기록(2026-06-12)**: 현 PDF는 래스터라 글자 선택 불가(스캔 문서와 동일). 벡터화의 현실 경로는 **SkiaSharp `SKDocument.CreatePdf`**(폰트 서브셋 임베딩 자동 — 한글 포함). 손작성 벡터(CID+ToUnicode+TTF 서브셋)는 미니 PDF 라이브러리 수준이라 기각, 투명 텍스트 레이어도 한글은 임베딩이 필요해 동일 문제. **선결 결정**: SkiaSharp는 Avalonia.Desktop의 전이 의존성이라 실질 새 바이너리는 없지만 라이브러리 "선언 의존성 0" 원칙은 깨짐. 구현은 Skia용 문서 걷기 한 벌 추가(줄 위치는 기존 TextLayout 메트릭 재사용으로 페이지 분할 일치, 셰이핑 차이로 줄 내 미세 간격 차 가능) — 중대형, 착수 시 별도 마일스톤.

---

## 📦 NuGet 배포 계획 (NuGet Publication Plan)

> **목표**: `AvaloniaRichEditor`(src/) 라이브러리를 **NuGet에 배포 가능한 수준**으로 끌어올린다.
> 현실적 출시 기준선은 **`0.1.0-alpha`**(실험적·기능 한정 공개)이며, 그 위에 **`1.0`**(프로덕션) 로드맵을 둔다.
> 평가 근거: 코드는 탄탄하나(표 병합·HTML 왕복·IME·레이아웃 캐싱) 패키징·공개 API·테스트·크로스플랫폼·접근성이 부재.

### 출시 품질 기준선 (Release Tiers)
- **`0.1.0-alpha`** = 패키지로 설치·참조 가능 + 최소 공개 API/문서 + Windows에서 동작 보장 + LICENSE/README. "써볼 수 있다."
- **`0.x`** = 크로스플랫폼 검증 + 공개 API 안정화 + 테스트 + CI + 에디터 모드(읽기전용/최소/전체). "실무에 조심스럽게 쓸 수 있다."
- **`1.0`** = 기존 기능의 안정성·성능·문서화를 프로덕션 수준으로. 새 기능 추가 없이 품질 집중. "프로덕션."

#### 🧭 버전 전략 결정 (2026-06-16) — "1.0은 숫자가 아니라 약속"
> `0.6.0-beta` 게시 후 합의한 진행 순서. **1.0은 "API 동결 + 프로덕션 보증" 신호**이므로, 남은 게이트가 *기능이 아니라 검증*인 현 상태(기능 A−, 검증 B−, 프로덕션 준비 C+)에서 1.0/1.0-beta로 점프하지 않는다.
> 1. **(현재) `0.6.0-beta` 유지** — 사용자 피드백 + 검증 게이트 진행. 추가 작업은 `0.7.0-beta`/`0.6.x`로 베타 반복.
> 2. **안정되면 `0.6.0` 정식 승격**(`-beta` 제거) — SemVer 0.x라 "써도 됨, 단 마이너 범프에서 API 변경 가능"을 정직하게 신호. NuGet 정식(비-prerelease) 노출.
> 3. **3개 검증 게이트를 모두 닫은 뒤에만 `1.0.0-rc.1` → `1.0.0`.** "1.0 베타"의 버전 문자열은 `1.0.0-beta.1`이며, 이 라벨은 1.0 푸시(아래 게이트 착수)를 시작할 때만 붙인다.
>
> **1.0 게이트(전부 검증, N4/N5/G1 잔여)**: ① 렌더 **픽셀** 테스트(헤드리스 기본 드로잉 no-op 우회 — 실 Skia 인프라), ② mac/Linux **기능** 실검증(현재 CI는 build+test만 그린), ③ 대형 문서 **성능 실측**(수백 페이지 타이핑/스크롤 지연·메모리 상한). 기능 추가가 아니라 *증명*.
>
> **[착수] 게이트 ① (2026-06-16)**: 별도 테스트 프로젝트 `AvaloniaRichEditor.Tests.Render` — `UseHeadlessDrawing=false` + `.UseSkia()` + 번들 Inter 폰트로 **실제 글리프 래스터**. 구조적 픽셀 테스트 **5건**(골든 이미지 아님 — AA/폰트가 OS마다 달라 비휴대적): 글리프 실제 래스터(스모크), 제목>본문 ink 높이(C1 픽셀 검증), 구분선 가로줄, **페이지 경계 분할**(P5가 재배선한 페이지-스택 replay가 page 2에 실제 콘텐츠를 그리는지 — 최고위험 경로 검증, A5 2페이지), **선택 하이라이트**(액센트 파랑 픽셀). 페이지 뷰는 불투명 회색 데스크라 alpha 대신 *어두운 글리프* 검출. **3-OS CI 통합**(Linux는 `libfontconfig1` 설치). 픽셀 읽기 배관은 기존 `RenderPrintPage`/`BitmapToRgb24`(`RenderTargetBitmap`+`CopyPixels`) 재사용. → **게이트 ① 실질 충족**(인프라+핵심 경로). 추가 커버(인라인 이미지·표 그리드 등)는 선택.
>
> **[실측] 게이트 ③ (2026-06-16)**: `--bench-text`(데모 하니스 확장 — 대형 텍스트 문서, 실 창/Skia/ScrollViewer)로 1000/3000/6000 문단(~70/210/420페이지, 최대 134만 자) 측정. **결과: 선형 스케일링, O(n²)·메모리 누수 없음.** 관리 힙 19→37→57MB(문단당 ~10KB 선형), 타이핑 rest 2.8→14→21ms, 스크롤 42→46→28fps, Render() median 5.9→12.6→26.7ms. **수백 페이지까지 사용 가능**(~200p 쾌적, 420p 극단도 ~47타/초·30fps로 기능). **병목 데이터**: 극단의 타이핑·재측정 비용은 키 입력마다 `ComputePageBreaks`가 전 문서 순회(=B1/P2 지목점) — 일반 문서 무관, 수백 페이지 극단에서만. P2(BlockBox 캐싱)의 *조건부 가치*를 수치로 확인(여전히 일반 사용엔 비병목). 회귀 가드: 타이밍은 CI 변동이 커 단언 부적합 → **메모리 상한** 헤드리스 테스트로 결정적 가드(누수/폭발 차단). 재현: `Demo.exe --bench-text` → `bench-text-results.txt`.
>
> **[보류·수동] 게이트 ② (2026-06-16 처리 방침)**: **자동화 불가 항목**이라 의도적으로 사용자/기여자 수동 검증으로 넘김(무한 차단 방지). 이유: macOS는 Apple 하드웨어에서만 가상화 가능 + CI macos 러너는 **헤드리스**(클립보드·IME·파일피커 같은 인터랙티브 동작 불가), Linux IME(IBus/Fcitx)는 WSLg에서 포워딩이 불완전. **이미 자동 검증된 것**: 3-OS(win/ubuntu/mac) CI build+test+**렌더 픽셀**(게이트 ①)+**메모리**(게이트 ③). **남은 수동 스모크**(실 Linux 데스크톱 / 실 Mac에서 1회): ① 앱 실행·렌더, ② 파일 피커 열기/저장(.flow·JSON·HTML), ③ 클립보드 복/붙(인앱 + 네이티브 앱 간 HTML/평문), ④ 한글/CJK IME 조합. Windows 11이면 **WSLg+Ubuntu로 ①②③(IME 제외)는 무료 검증 가능**(IME·mac은 실 하드웨어). **1.0 처리**: 실 하드웨어 스모크 1회를 받거나, 받기 전까지 README대로 "**best-effort, 빌드/렌더는 CI 검증**"으로 명시하고 출하(현재 README/로드맵에 이미 best-effort로 문서화됨).

### 🟢 [대부분 완료] 최우선: GitHub 저장소 생성 + 푸시 (단일 차단점 해소, 2026-06-10)
> **N1 잔여·N3 잔여(mac/Linux 스모크)·N4 잔여(CI 그린)·`0.1.0-alpha` 체크리스트 전체가 이 하나에 막혀 있었다.** → 저장소 생성·푸시·CI 그린으로 핵심 차단 해제.
- [x] 푸시 전 정리: `test.json`(스크래치) 추적 해제 + `.gitignore` 추가. (`tests/out`·`roundtrip-out`·`test.html`·corpus `real_*`는 이미 ignore/미추적 확인.)
- [x] 히스토리 정리: 초기 커밋에 박혀 있던 ~240MB 빌드 산출물(`bin/`·`obj/`)을 `git filter-branch`로 전체 히스토리에서 제거 후 force-push. 결과: 깨끗한 저장소.
- [x] GitHub 저장소 생성 + 푸시 — **`centwon/AvaloniaRichEditor`**.
- [x] **CI 3-OS 매트릭스 첫 실행 그린** — windows/ubuntu/macos 전부 ✓ (Linux 헤드리스 폰트 이슈 없음). → **N3 mac/Linux 스모크 + N4 CI 그린 동시 해소.**
- [x] **Public 전환 완료** (2026-06-10) — 전환 전 스캔(시크릿·1MB+ 파일·개인경로 0건, LICENSE/corpus 합성 확인). https://github.com/centwon/AvaloniaRichEditor
- [ ] N1 미결 해소: `RepositoryUrl`/`PackageProjectUrl` + SourceLink 채움 (Public 전환 후).
- [x] (유지보수) CI 액션 Node 20 → Node 24 대응 (2026-06-11) — `checkout@v6`·`setup-dotnet@v5`·`upload-artifact@v7`로 갱신.

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
- [x] **🚀 nuget.org 게시 완료(2026-06-13) — `AvaloniaRichEditor 0.2.0-alpha`**: https://www.nuget.org/packages/AvaloniaRichEditor + [GitHub Release v0.2.0-alpha](https://github.com/centwon/AvaloniaRichEditor/releases/tag/v0.2.0-alpha). 절차: 전수 검증(Release 빌드 경고 0·163테스트·왕복·pack 내용물·AOT) → 버전 범프·`PackageIcon`·CHANGELOG 절 확정·**PublicAPI Shipped 승격(368건, 이후 변경은 동결 가드 추적)** → ci.yml Trusted Publishing(OIDC, 시크릿 없음) → 태그 푸시. 시행착오 2건 기록: ① `NuGet/login`의 `user:`는 GitHub 소유자가 아니라 **nuget.org 로그인 계정명(kanu)**, ② 정책의 Repository owner는 반대로 **GitHub 소유자(centwon)** — 둘을 바꿔 넣으면 각각 401. nupkg+snupkg 양쪽 push Created 확인. (이전 보류 결정 2026-06-10은 해제 — alpha 프리릴리스가 API 변동을 커버.)
- ~~**의도적 보류(2026-06-10 결정)**~~: `PackageIcon` + nuget.org 실제 게시를 **함께 미룸**. 근거: ① 게시는 비가역(unlist는 되나 삭제 불가, 버전 영구 예약)인데 alpha API가 아직 변할 수 있음. ② 지금도 태그 CI의 `Pack` 아티팩트(`.nupkg`)를 로컬 피드/GitHub Release로 소비 가능 — nuget.org는 "더 넓은 배포"일 뿐 alpha 성립 조건 아님. ③ 아이콘이 의미를 갖는 시점이 곧 게시 시점이라 둘을 한 묶음으로 처리. **재개 조건**: API 안정화 → **Trusted Publishing**(2026-06-12 갱신: nuget.org가 장수명 API 키 대신 OIDC 기반 신뢰 게시를 권장 — nuget.org에서 게시 정책에 GitHub 저장소/워크플로 등록 → ci.yml에 `permissions: id-token: write` + `NuGet/login` 액션으로 단기 토큰 교환 → `dotnet nuget push`. 시크릿 저장 불필요) + push 스텝 작성 + `PackageIcon` 추가 + GitHub Release 작성.
- [x] **후속 릴리스(태그 푸시 → CI Trusted Publishing 자동 게시)**: `0.3.0-alpha`/`0.4.0-alpha`(2026-06-13~14, 클립보드 RTF·UX), **`0.5.0-alpha`(2026-06-14)** — 자체 완결형 `RichEditorView`(페이지/줌/파일액션 툴바 + 상태바·`FitToWidth`·`PrintRequested`), 폰트 콤보 자기-글꼴 렌더, 컨텍스트 메뉴 폰트 고정 + 표 드래그 크기 피커, 유휴 렌더 성능(신뢰-캐시 `_trustLayoutCache`/`_tableLayoutCache` + 가지치기), 페이지 레이아웃 재설계(`PageSize`/`PageOrientation`/`ShowPageBoundaries`)·`.flow` 확장자. 매 릴리스 PublicAPI Unshipped→Shipped 승격으로 API 동결. https://www.nuget.org/packages/AvaloniaRichEditor + [Release v0.5.0-alpha](https://github.com/centwon/AvaloniaRichEditor/releases/tag/v0.5.0-alpha).
- **참고**: `AvaloniaRichEditor` ID는 nuget.org 미등록(사용 가능). `Avalonia.` 점 프리픽스는 예약이라 회피.

### 🟡 N2: 공개 API 설계 & 문서화 (대부분 완료 2026-06-08)
- [x] **표면 정리**: 직렬화 DTO·`UndoManager`/`UndoState`·`InputDialog`를 `internal`로(중첩 레이아웃 타입은 이미 private). `[InternalsVisibleTo("AvaloniaRichEditor.Tests")]` 추가.
- [x] **표준 이벤트**: `TextChanged`, `SelectionChanged`, `DocumentChanged` 추가. 변이 신호를 `PushUndo()` 단일 choke point로 집약, Render에서 `Dispatcher.Post`로 비재진입 플러시.
- [x] **스타일 가능 속성(StyledProperty)**: `SelectionBrush`, `CaretBrush`, `DefaultFontFamily`, `DefaultFontSize` 추가(선택색/캐럿색 하드코딩 제거, 기본 글꼴 외부화).
- [x] **편의 API**: `ToHtml`/`LoadHtml`(기존 Get/SetHtml 개명), `ToJson`/`LoadJson`, `Clear`, `CanUndo`/`CanRedo`.
- [x] `NativeEditor`(웹 에디터 호환 래퍼) 라이브러리→`samples` 이동.
- [x] **공개 멤버 XML 문서 주석 완성 (2026-06-10)** — 전체 공개 API(240개) `<summary>` 완료. CS1591 NoWarn 제거. 경고 0개.
- [x] **API 동결 가드: `Microsoft.CodeAnalysis.PublicApiAnalyzers` 도입 (2026-06-10)** — `PublicAPI.Shipped.txt`/`Unshipped.txt` + nullable 주석 269개 선언. RS0016이 새 공개 멤버 추가 시 선언 강제.
- [x] **(선택) 데모 코드비하인드를 새 이벤트/속성으로 마이그레이션 (2026-06-14)** — `MainWindow`가 레거시 `StatusChanged`(coarse, "새 코드는 TextChanged/SelectionChanged 선호" 명시) 단일 구독을 표준 이벤트 둘로 분리: 캐럿 카운트는 `SelectionChanged`, 페이지 수·이미지 소프트 제한 경고(O(blocks) 워크)는 콘텐츠 전용 `TextChanged`로 이동. 부수 효과로 페이지/이미지 수 계산이 캐럿 이동마다 돌던 것을 편집 시에만 돌도록 정리(`_lastChars` 가드 핵 제거).

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

### 🟢 N3.6: 라이브러리 툴바 승격 + 모드 연동 (완료 2026-06-11)

> **배경**: 거의 모든 소비 앱이 서식 툴바를 필요로 한다. 서식 툴바(B/I/U·글꼴·목록·정렬 등)는 컨트롤 *자신의 공개 명령*만 호출하므로 "에디터의 일부"이지 앱 셸이 아니다. 현재는 N0 분리(2026-06-08) 때 툴바가 데모 쪽(`NativeEditor.BuildToolbar`/`MainWindow`)에 남아 라이브러리 밖에 있다. → N3.5 모드 표의 "툴바" 열이 미구현인 근본 원인. 이를 라이브러리로 되돌려 **모드가 동작·컨텍스트 메뉴·툴바를 일관되게 지배**하도록 한다.
>
> **경계(중요)**: "서식 툴바"는 라이브러리(선택 계층), "앱 셸"(창·저장/열기·메뉴바·파일 다이얼로그)은 앱. 이 선을 지켜 비대화를 막는다.

- [x] **3계층 구조** (소비자가 추상화 수준 선택, 셋 다 같은 패키지):
  | 계층 | 타입 | 용도 | 상태 |
  |------|------|------|------|
  | ① 코어 | `RichEditor` (현행 유지) | 명령+상태+이벤트만. 완전 커스텀 UI를 만드는 소수용 | ✅ |
  | ② 툴바 | `RichEditorToolbar` | 선택적 서식 툴바. `Target`으로 ①을 가리켜 명령 호출+모드 반영. 레이아웃은 소비자가 배치 | ✅ 2026-06-11 |
  | ③ 번들 뷰 | `RichEditorView` | ①+②+스크롤러를 묶은 한 줄 drop-in. 가장 편한 기본값 | ✅ 2026-06-11 |
- [x] **연결 고리 = `Target` 속성**: `RichEditorToolbar.Target`(`StyledProperty<RichEditor?>`) 하나로 세 방향 연결 — 구현 완료.
  - 버튼 → 명령: `Target.ToggleBold()` 등 *기존 공개 명령* 호출.
  - 모드/플래그 → 가시성: `Target.PropertyChanged` 구독으로 `AllowImages`/`AllowTables`/`IsReadOnly` 반영. ReadOnly(또는 Target 없음)=툴바 숨김, Basic(플래그 off)=삽입 버튼 숨김, Full=전체.
  - 선택 상태 → 버튼 표시: `Target.StatusChanged` + **기존** `GetCaretFormat()`/`IsFormatPainterActive`/`CanUndo·CanRedo` 구독 → B/I/U/S·목록·글꼴·크기·제목·정렬 콤보 반영. (설계 주의점에서 우려한 `CurrentFormat` 신설은 불필요했음 — N3.5 때 이미 `GetCaretFormat` 공개됨.)
- [x] **`NativeEditor`/데모 승격**: 데모 `MainWindow`의 서식 줄(색상 팔레트·표 격자 플라이아웃 포함)과 `NativeEditor.BuildToolbar`를 `RichEditorToolbar`로 대체. 한글 폰트 가정 제거 — 글꼴 콤보는 `Target.FontFamilyChoices`에서 채움(데모가 한국어 폰트를 주입). 데모에는 앱 셸(저장/열기/HTML/줌/찾기바)만 남음.
- [x] **현지화**: `RichEditorLocalization` 신설(공개 정적 클래스) — 키 기반 ko/en 내장 테이블, OS UI 컬처로 자동 선택, `Register(lang, dict)`로 제3자 언어 추가/부분 오버라이드(키 단위 병합, 영어 폴백), `Language` 런타임 전환(`LanguageChanged`로 툴바 리빌드, 메뉴는 매번 새로 빌드라 자동). AOT 안전(순수 dictionary, resx 없음). 컨텍스트 메뉴·다이얼로그·툴바·데모 셸 전부 적용. 버튼 구성 커스터마이즈(아이콘 교체 등)는 미구현 — ①만 쓰는 길은 열려 있음.
- **설계 주의점**:
  - **선택 상태 반영엔 소폭 신규 API 필요**: 버튼이 명령을 *호출*하는 건 기존 명령으로 끝나지만, 현재 선택의 서식을 *반영*(B 눌림)하려면 "지금 선택이 Bold인가?" 조회 표면이 필요(현재 `SelectionChanged` 이벤트는 있으나 상태 조회 API 없음 → 예: `CurrentFormat` 신설). 비용 중간.
  - **스크롤러 소유권**: 스크롤은 ③(번들 뷰)만 품고, ①②는 스크롤 비소유로 분리(경계 명확화). 현재 `NativeEditor`가 스크롤러를 품고 있으므로([NativeEditor.cs](samples/AvaloniaRichEditor.Demo/NativeEditor.cs)) 승격 시 ③으로만 이전.
- **✅ 결정(2026-06-10): `0.1.0-alpha`에는 미포함, `0.2.0`으로.** 근거: ① alpha의 독자는 정의상 얼리어답터(부품 조립형 개발자)이고 데모에 동작하는 툴바 프로토타입이 참고 코드로 존재. ② 툴바에 필요한 `CurrentFormat` 등 신규 공개 API를 API 동결 가드 도입 전에 서두르면 동결 전에 표면만 넓히는 꼴. **`PublicApiAnalyzers` 도입(N2 잔여)을 0.2.0 진입 조건으로** 하여 "0.x = API 안정화" 선언과 순서를 맞춘다. → **이행 확인(2026-06-11)**: PublicApiAnalyzers 가동 중 상태에서 구현, 신규 표면(`RichEditorToolbar`, `RichEditorLocalization`)은 `PublicAPI.Unshipped.txt` 등재 완료.
- **구현 메모(2026-06-11)**: `Controls/RichEditorToolbar.cs`(코드 컨트롤, XAML 없음), `RichEditorLocalization.cs`. 테스트 11건 추가(현지화 6 + 툴바 헤드리스 5) — 총 85건 통과. 잔여였던 버튼 구성 커스터마이즈는 아래 아이콘 훅으로 일부 해소.
- **✅ 아이콘 커스터마이즈 훅(2026-06-11)**: `RichEditorIcons.Provider`(`Func<RichEditorIcon, Control?>`, 전역 정적 — `RichEditorLocalization` 패턴 미러링) + 슬롯 enum `RichEditorIcon` 41종(툴바 16 + 컨텍스트 메뉴 25). 팩토리 계약: 호출마다 새 Control 반환(부모 단일 제약), null=내장 텍스트 글리프 유지. 툴바 `Btn`/색상 버튼/표 버튼(아이콘+▾)·컨텍스트 메뉴 `Mi`(신규 `MenuItem.Icon`)에 배선 — 메뉴는 우클릭마다 리빌드라 즉시 반영. **라이브러리 의존성 0 유지** — 데모만 `FluentIcons.Avalonia` 2.1.328(Avalonia 12 타깃, MIT) 참조해 `FluentIconProvider.Install()`로 41종 매핑 시연. PublicAPI 44건 등재, 테스트 2건(교체/폴백) — **총 120건 통과.**
- **③ `RichEditorView` 완료(2026-06-11)**: `Controls/RichEditorView.cs` — 에디터+툴바(Target 사전 연결)+수직 스크롤러(③만 스크롤 소유, 경계 규칙 준수) 묶음. 공개 표면은 `Editor`/`Toolbar` get 프로퍼티 2개뿐(문서/명령/플래그는 `Editor.*`로). 커스텀 레이아웃·스크롤이 필요한 호스트는 ①/② 직접 조합. 테스트 4건 — 총 92건 통과. 데모 `MainWindow`는 자체 페이지/줌 레이아웃이라 ①+② 조합 시연을 유지(③은 헤드리스 테스트로 검증).
- **기본 글꼴 = OS UI 글꼴(2026-06-11)**: `DefaultFontFamily` 기본값을 Windows 메시지 글꼴(`SystemParametersInfo(SPI_GETNONCLIENTMETRICS).lfMessageFont`, 한국어 Windows 실측 "맑은 고딕" — 현지화된 이름으로 반환되어 글꼴 콤보 항목과 일치)로 변경(`SystemFontInfo` internal). 비Windows/실패 시 `FontFamily.Default` 폴백. 명시 글꼴 없는 런의 툴바 콤보는 유효 기본 글꼴을 PlaceholderText로 표시(거짓 선택 안 함).
- **글꼴 목록 = 시스템 글꼴(2026-06-11)**: `FontFamilyChoices` 기본값을 설치된 시스템 글꼴(`FontManager.Current.SystemFonts`, UI 컬처 정렬)로 변경. 빈 목록=시스템(센티널), 비어있지 않은 목록 할당=큐레이션(기존 오버라이드 의미 유지, 신규 공개 API 없음). **OS가 글꼴 이름을 UI 언어로 현지화해 보고**(한국어 Windows 실측: "맑은 고딕" 등 238개)하고 그 이름으로 DirectWrite 매칭도 되므로 표시명 매핑 불필요. 주의: 현지화된 이름이 문서에 저장되므로 비Windows 간 이동 시 해석 안 될 수 있음(영문명 필요 시 호스트가 큐레이션). 헤드리스 등 열거 불가 플랫폼은 기존 5종 폴백.

### 🟡 N4: 테스트 & CI (기반 완료 2026-06-08)
- [x] `tests/AvaloniaRichEditor.Tests`(xUnit) 신설 — **19개 테스트 통과**.
- [x] 단위 테스트: 표 병합/해제·행열 삽입삭제(`MergeCells`/`SpanOf`/`AnchorOf`/`IsCovered`), `TextRange`(GetText/Delete/ApplyPropertyValue), JSON 왕복(텍스트·서식·정렬·제목·표 병합 + 멱등), HTML 왕복(bold/list/table). 헤드리스 없이 순수 단위테스트로 동작.
- [x] **GitHub Actions**(`.github/workflows/ci.yml`): build+test **3-OS 매트릭스**(ubuntu/windows/macos) → N3의 mac/Linux 스모크 겸함. 태그(`v*`) 푸시 시 `dotnet pack` → 아티팩트 업로드(nuget push는 시크릿 추가 후 주석 해제).
- [x] **컨트롤 헤드리스 테스트**: xUnit **v3** 전환(테스트 프로젝트 Exe, 병렬화 off) 후 `Avalonia.Headless.XUnit`로 8개 추가 — InsertText, ToggleBold+Undo, InsertTable+Undo+Redo, LoadHtml/ToHtml, ToJson/LoadJson, GetPlainText, Clear. **총 27개 통과**. (렌더·히트테스트 픽셀 단언은 향후.)
- [x] CI 실제 실행 확인 — **저장소 푸시 후 3-OS 매트릭스 그린 확인 완료(2026-06-10)**. windows/ubuntu/macos 전부 build+test ✓.
- [x] **오프셋 모델 + 멀티문단 회귀 테스트(2026-06-10, N6-2 안전망)**: `TextRangeOffsetTests.cs` 10건 — 인라인 이미지=1글자(GetText 플레이스홀더 드롭+오프셋 보존, Delete가 이미지 제거/보존, ApplyPropertyValue 양측 스타일, GetRichRuns 이미지 스킵), 부분 서식 Run 분할, 멀티문단(GetText 개행 조인·Delete 중간 제거+양끝 병합·표 횡단 블록 제거·중간 문단 스타일). `TextRange`가 public이라 `InlineLen`/`BuildPlain`과 같은 오프셋 규칙을 모델 레벨에서 검증. **총 37→47.**
- [x] **컨트롤 편집 경로 + 붙여넣기 구성요소 테스트(2026-06-10)**: `RichEditorKeyInputTests.cs` 11건 — 라우티드 이벤트로 실제 OnKeyDown/OnTextInput 파이프라인 구동(Backspace 문자삭제·문단병합, Delete 전방병합+Undo, Enter 분할·제목→본문 리셋·빈 리스트 항목 탈출, 타이핑 Undo 코얼레싱·캐럿이동 시 런 분리, ReadOnly 차단). 레이아웃 의존 키(Home/Up/Down)는 회피, Ctrl+Home/Left/Right로 캐럿 제어. `RichEditorClipboardTests.cs` 6건 — CF_HTML 헤더 제거(`ExtractHtmlFragment` internal 승격) 3변형, `InsertHtml` 단일문단=인라인 병합/다중문단=블록 삽입+Undo. **총 47→64.**
- [x] **레이아웃 의존 키 커버 (2026-06-14)**: `RichEditorCaretNavigationTests` 5건 — Home/End/Up/Down은 `_lastCaretPoint`(Render 중에만 채워짐)와 히트테스트에 의존하므로, top-level Window 없이 `RenderTargetBitmap.Render(ed)`로 Render 패스를 강제(인쇄 테스트와 동일 경로, 헤드리스 no-Window 규칙 준수)해 레이아웃 캐시·캐럿 기하를 실제화. 캐럿 위치는 마커 입력 위치로 간접 단언. **변별 테스트**: 3문단 바닥에서 Up이 *가운데* 문단에 떨어져야(클램프-투-탑 아님) 통과 — Render가 `_lastCaretPoint.Y`를 실제로 채웠음을 증명. **총 184→190.**
- [ ] **잔여 갭(여전히 차단)**: ① async 클립보드 획득 체인(`TryGetDataAsync` 포맷 순회·폴백 순서) — `TopLevel.Clipboard` + 페이크 `IAsyncDataTransfer` 필요(헤드리스 no-Window 규칙과 상충). ② 렌더 **픽셀** 단언 — 헤드리스 기본 드로잉이 no-op(`UseHeadlessDrawing=true`)이라 글리프가 실제 래스터되지 않음(RenderPrintPage 테스트도 PixelSize만 단언하는 이유). 실 Skia(`UseHeadlessDrawing=false`)로 바꾸면 가능하나 테스트 앱 전역 설정 변경이라 보류.

### 🔵 N5: 견고성·성능 — **`1.0` 목표** (우선순위 5)
- [~] **Undo 입력 코얼레싱**: 연속 타이핑을 단일 체크포인트로(`PushUndoTyping`, 타이핑 1런=클론 1개. 캐럿 이동/선택/이산 편집 시 런 종료). 키 입력마다 전체 복제하던 최악 케이스 해소. (완전 델타/명령 기반 전환은 향후 — 이산 편집·삭제·서식은 여전히 op당 클론, 50벌 상한 유지.)
  - **❌ 델타 Undo 불필요 — 실측으로 기각(2026-06-12)**: `--bench`에 순수 `Document.Clone()` 단독 측정 추가(웜업 후 중앙값 20회). **클론은 100장에서도 0.28ms**(10/20/50/100장 = 0.11/0.08/0.23/0.28ms — N6-2 참조 공유 덕에 항상 sub-ms). 종전 "첫 키스트로크 162ms = undo 클론" 귀속은 **오측정**이었음 — 그 수치는 문서 크기에 비례하지 않고(61→20→37→144ms 들쭉날쭉) 타이핑 경로 **첫 호출 JIT 웜업**의 시그니처. 델타 Undo는 0.28ms를 없애자고 전 편집 경로를 재작성하는 셈이라 가치 없음. (부가: 강제 full layout 87.7ms지만 실제 타이핑 키 rest는 0.7ms — 레이아웃 캐싱이 이미 키스트로크당 전체 재측정을 막음. 편집 경로에 실병목 없음.)
- [x] **접근성(프레임워크 천장 도달)**: `RichEditorAutomationPeer : ControlAutomationPeer, IValueProvider` — 컨트롤 타입 Edit, 값=문서 평문(`GetPlainText`), `IsReadOnly`/`SetValue`, `GetNameCore` 기본 이름. 스크린리더 내용 읽기/쓰기 가능. **전체 `ITextProvider`(캐럿/범위/속성)는 불가** — Avalonia 공개 automation 모델에 ITextProvider/ITextRangeProvider가 없음(Win32 COM interop 전용). Avalonia 내장 `TextBox`도 동일하게 IValueProvider만 노출. → Avalonia가 TextPattern을 추가하면 그때 확장.
- [x] **God-class 분해(2026-06-12 완료)**: `RichEditor` 본체 3,595→**1,273줄**(65%↓), 전부 동작 불변 파일 이동(4커밋, 단계마다 120테스트 그린). 분리 결과: `ContextMenu`/`Clipboard`/`Rendering`/`Modes`(기존) + 신규 `FindReplace`(찾기/바꾸기), `Tables`(셀 탐색·Tab 내비·행/열 연산), `Images`(크기/교체/저장·블록↔인라인 변환), `DocumentApi`(HTML/JSON/.ardx 입출력·Clear·GetPlainText), `Formatting`(서식 명령·리스트 분할·하이퍼링크·포맷 페인터), `HitTesting`(GetPositionFromPoint/GetBlockAtPoint/GetLinkRunAtPoint·LayoutTable 공유 기하·인라인 이미지 오프셋 헬퍼), `Input`(포인터·키보드·IME·블록 횡단 캐럿). 남은 본체 = 속성/이벤트·undo 기계·편집 코어(InsertText/Delete/Split)·레이아웃 캐시(BuildTextLayout)·클립보드 내부 헬퍼. **인라인 표(글자처럼 취급) 착수 시 건드릴 HitTesting·Input이 단독 파일로 격리됨.**
- **검증**: 수백 페이지 문서에서 타이핑/스크롤 지연 측정, 메모리 상한 확인.

#### 🧭 2026-06-15 종합 평가 (코드 전수 리뷰 기준)
> 기능 충실도 A−, 코드 품질 B+, 견고성/검증 B−, 프로덕션 준비도 C+(알파로는 견고). 혼자 만든 Avalonia 리치 에디터로는 상위권. 1.0 결정 과제 셋: ① 기하 워커 통합(G1, 아래), ② 테스트 깊이(렌더 픽셀·페이지 분할·복잡 표 — N4 잔여 갭), ③ 상호운용 한계 명시(클립보드 앱별 편차/벡터 PDF/in-app 인라인이미지 — 대부분 기록 완료). **정정**: 3-OS CI 매트릭스는 이미 존재·그린(N4) — 남은 건 픽셀/async-클립보드 단언과 mac/Linux *기능* 실검증.

#### 🔵 G1: 기하 워커 통합 — 단일 수직 레이아웃 패스 (1순위 리스크, 미착수)
> **문제(1순위 구조 리스크)**: 7곳이 `Document.Blocks`를 각자 걸으며 `yOffset += MarginTop … += height + MarginBottom`을 중복 계산한다 — `DrawDocumentBlocks`(Rendering), `MeasureContentHeight`, `GetPositionFromPoint`/`GetBlockAtPoint`/`GetLinkRunAtPoint`/`GetTableRect`(HitTesting), `BlockAtY`(Input), `ComputePageBreaks`(Pagination). 블록 높이·여백 계산이 한 곳만 어긋나도 캐럿·히트테스트·페이지가 미묘하게 틀어진다(주석에 남은 "하드코딩 10" MarginBottom 버그가 그 사례). 공유 헬퍼(`LayoutTable`/`ParaLeft`)로 일부 완화했을 뿐, **수직 누적은 여전히 분산**.
>
> **목표**: 블록별 (top, height, layout 객체)의 **단일 출처**를 만들어 모든 소비자가 공유 → "워커 드리프트" 버그 클래스를 구조적으로 제거. (핵심 불변식 1 "단일 TextLayout"의 수직 버전.)
>
> **단계(각 단계 독립 출하·테스트 그린 유지)**:
> - **P0 안전망**: 헤드리스로 검증 가능한 *논리* 기하 특성화 테스트 추가 — `GetPositionFromPoint`/`GetBlockAtPoint`/`BlockAtY`(알려진 점), `MeasureOverride` DesiredSize, `ComputePageBreaks` 개수·위치. 문단/표/이미지/구분선/여백/빈문단 혼합 문서로 현 동작 고정.
> - **P1 공유 advance 추출**: `BlockExtent(Block, width, out TextLayout? paraLayout)` 한 곳이 높이+레이아웃 객체를 캐시 통해 반환. 모든 워커의 블록별 높이 계산을 이걸 호출하도록 교체 → **높이 불일치 제거**(가장 작은 변경으로 드리프트 차단). Render는 그리기 유지, 높이만 공유.
> - **P2 블록박스 열거자**: `IReadOnlyList<BlockBox> LayoutDocument(width)`(`BlockBox = Block, Top, Height, layout`), 레이아웃 캐시처럼 캐싱(`_trustLayoutCache` 존중). 수직 위치만 필요한 소비자(GetBlockAtPoint/BlockAtY/GetTableRect/Measure)가 이 리스트를 순회.
> - **P3 히트테스트**: `GetPositionFromPoint`/`GetLinkRunAtPoint`가 point.Y로 BlockBox를 찾고 박스의 캐시 레이아웃으로 X/오프셋만 히트테스트(재측정·재순회 없음).
> - **P4 페이지네이션**: `ComputePageBreaks`가 열거자 소비, 문단 줄 top은 박스의 TextLayout에서.
> - **P5 렌더(최고 위험)**: `DrawDocumentBlocks`가 열거자로 top/height/layout 취득, 그리기·캐럿·선택·컬링·핸들 등록·페이지 replay(clip+translation)는 유지. 컬링은 박스를 visTop/visBottom로 필터. **데모 수동 검증 필수**(헤드리스 픽셀 불가).
> - **P6 정리**: 죽은 per-워커 yOffset 코드 삭제, `_trustLayoutCache` 의미 보존 확인.
>
> **규모/가치**: 중-대형(다세션). 단계마다 200+ 테스트 그린 유지. 인라인 표 등 향후 기능의 전제이기도 함.
>
> **진행(2026-06-16)**: [x] **P0** — `GeometryConsistencyTests` 3건(측정이 마지막 블록까지 도달, 혼합문서>빈문서, 표 추가 시 높이 증가 — Continuous 모드로 `MeasureContentHeight` 직접 검증). [x] **P1+읽기전용 워커 통합** — `BlockExtent(block,width,top,out paraLayout,out tableLayout)` 도입, **6개 읽기 전용 워커**(`MeasureContentHeight`·`GetBlockAtPoint`·`GetTableRect`·`GetLinkRunAtPoint`·`GetPositionFromPoint`·`BlockAtY`)를 전부 이 단일 출처로 라우팅. 기존 200 테스트 그린 = 동작 보존. [x] **P4 페이지네이션 통합(2026-06-16)** — `ComputePageBreaks`의 블록별 높이/레이아웃 계산을 `BlockExtent`로 라우팅(중복 `LayoutTable`/`BuildTextLayout`/`switch` 제거). 페이지네이션 고유 로직(테이블 행·문단 줄 atom 분할)만 남음. MarginBottom 처리도 `block.MarginBottom`으로 통일 → 수직 누적이 `MeasureContentHeight`와 라인 단위로 동일. 207 테스트 그린(Pagination 18 + Geometry 등). [x] **P5 렌더 통합(2026-06-16)** — `DrawDocumentBlocks`가 블록별 높이·레이아웃을 직접 계산하던 것을 루프 상단 단일 `BlockExtent` 호출에서 취득(그리기·컬링·캐럿/선택·핸들·페이지 replay·셀별 IME preedit은 render에 유지). 동일 캐시 객체·동일 높이라 동작 불변, 223 논리 테스트 그린 + **데모 육안검증 통과**(본문/제목/표/이미지/구분선/선택/캐럿/페이지뷰/IME). → **G1 사실상 완료**(워커 드리프트 버그 클래스 구조적 제거). **남음**: P2(`BlockBox` 캐싱 열거자 — 성능 선택, 측정상 비병목이라 미착수).

#### 🧹 2026-06-16 코드 전수 리뷰 패스 (엔진 ~12k줄 정독)
> P4 마무리 후 전 소스 정독으로 버그·성능·정리 항목을 도출하고, **위험 대비 효용이 높은 것만** 처리. 크래시급 버그 없음. 테스트 **190→222(+32)**, 전부 그린.
>
> **처리 완료**:
> - [x] **A1 — `InsertHtml` ReadOnly 가드**: 공개 변형 API 중 유일하게 `IsReadOnly` 미확인 → 읽기 전용 문서가 호스트 호출로 변경되던 결함. 회귀 테스트 추가.
> - [x] **C1 — 제목 렌더 타임 스타일 전환(데이터 손실 수정)**: `SetHeading`이 모든 런의 `FontSize`/`FontWeight`를 구워넣어 본문으로 되돌리면 사용자 지정 크기/굵기가 소실. 이제 `HeadingLevel`만 설정하고 큰/굵은 모양은 `BuildTextLayout`에서 본문 기본값(≤0/14px) 런에만 적용 → 토글 왕복 비파괴. `ParagraphSig`에 `HeadingLevel` 포함, `DrawListMarker` 일치, `SetHeading` measure 무효화(높이 변경 잠복 결함 동시 해결). **부수효과**: 툴바는 제목 텍스트의 내부 런 크기(14)를 표시(시각 24 아님); HTML 가져오기 경로는 여전히 크기를 런에 굽음(파서 미변경, 렌더 동일).
> - [x] **C2(+확장) — 동일서식 인접 런 합류**: 병합/삭제/서식토글이 동일 서식 경계 런을 파편으로 남겨 `ParagraphSig`·메모리가 누적되던 것을, `TextRange.CoalesceRuns`로 모든 편집 경로에서 자동 합류(서식 8필드 일치 시만, 오프셋 불변). 인라인 이미지는 경계.
> - [x] **B2 — `TextPointer.CompareTo` 단일 패스**: 문단이 다른 두 포인터 비교가 문서를 두 번 완전 순회하던 것을 한 번의 순회(둘 다 찾으면 조기 종료)로. 부재(stale) 시맨틱까지 동일 보존(특성화 테스트 4건). 데드 `GetGlobalIndex` 제거. *worst-case O(n)은 유지* — 표 셀까지 추적하는 무효화 캐시는 측정상 비병목 대비 위험 과함.
> - [x] **D1 — 데드 코드 `ApplyInlinesToFormattedText` 제거**(호출처 0; `FormattedText`는 레이아웃 미사용 타입). [x] **D2 — 화살표/PageUp·Down 7분기 선택갱신 중복을 `ApplyCaretSelection`로 통일**(외형 정리).
>
> **의도적 보류(근거)**: B1=P2(`ComputePageBreaks` 매 측정 재계산) — N5 실측 "편집 경로 실병목 없음", 캐시 무효화 위험 과함 → 벤치 선행. B3(HTML `HasBlockOrMedia` O(n²))·B4(`ReplaceAll` O(N²))·B6(`FindCell`) — 희귀 경로 + 안전망/무효화 위험. B5(`CheckImageLimit` 매 flush 카운트) — 비용 미미. C3(RTF 셀 병합 미파싱) — 문서화된 서브셋 한계.
>
> **베타 게이트 마무리(2026-06-16)**: [x] **A2** — 이미지 디코드 실패 시 `RawBytes` 보존(소실→저장 시 그림 누락 수정). 실패 분기는 `[Fact]`(플랫폼 없음→`new Bitmap` 실제 예외)로만 재현 가능(헤드리스 로더는 1×1 더미). [x] **G1 P5** 렌더 통합(위 G1 절). [x] **표/셀 리사이즈 끊김 수정** — `_tableLayoutCache`가 `ColumnWidths`/`RowHeights`를 키로 안 잡아 drag 중 옛 치수 반환하던 것을, 리사이즈 이동마다 캐시 무효화로 라이브 반영. → **`0.6.0-beta` 게시**(alpha 0.1~0.5 이후 첫 beta; API 안정화 신호 + `GetRichInlines` Shipped 승격). **남은 1.0 게이트는 기능이 아니라 검증**: 렌더 픽셀 테스트(헤드리스 no-op 한계), mac/Linux 기능 실검증, 대형 문서 성능 실측.

### 🟢 [코어 완료] 마일스톤 A: 셀 안에 블록 (착수 2026-06-18, 코어 완료 2026-06-19)
> **목표(2중)**: ① 표 셀이 단일 `Paragraph`가 아니라 **블록 리스트**(여러 문단·블록이미지·구분선·중첩 표)를 담는다. ② 이를 **"경계 박스 안에서 블록 리스트를 레이아웃/렌더/히트테스트하는 재귀 프리미티브"**로 구현해 문서 워크와 셀 워크를 통합 → 후속 **B(인라인 표=HWP식 글자처럼 취급)**가 그대로 재사용.
>
> **핵심 통찰**: 표는 *이미* 중첩 편집 컨텍스트다(각 셀이 자기 `BuildTextLayout`로 히트테스트/캐럿 하강 — [HitTesting.cs:102](src/AvaloniaRichEditor/Controls/RichEditor.HitTesting.cs:102)). A는 셀 *안쪽* 깊이를, B는 부모 문단 줄의 *바깥 경계* 라우팅을 다루므로 **A→B는 난이도를 가중하지 않는다**(다른 축). 캐럿 모델 불변(`TextPointer(Paragraph,Offset)` 유지). A는 B의 전제조건이 아니라, 재귀 프리미티브를 챙기는 디딤돌.
>
> **확정 결정(사용자 2026-06-18)**: ① 셀 타입 = **`TableCell` 클래스 신설**(`Blocks`+`Background`; `Paragraph.Background` 셀 해킹 승격). ② 셀 안 Enter = **진짜 문단 분할**(기존 `\n` 폐기, 불변식 3 셀 한정 해제). ③ v1 범위 = **중첩 표까지 전부**(재귀 프리미티브 완전 구현 필수, RTF `\nesttbl` 출력/병합도 v1).
>
> **단계(각 출하·테스트 그린)**:
> - [x] **P0 안전망(2026-06-18)**: `TableCellBehaviorTests` 4건 — JSON 왕복이 2×2 셀 그리드 텍스트 + 셀 배경(P1에서 `Paragraph.Background`→`TableCell.Background` 이전 예정) 보존, Tab/Shift+Tab 셀 간 캐럿 이동 + 마지막 셀 Tab=행 추가(Parent 사슬 의존), 셀 콘텐츠가 표 높이 구동(래핑 셀이 단일문자 셀보다 큼). 260→**264 그린**. (셀 참조 `Cells[r][c]`를 쓰는 사이트라 P1에서 함께 마이그레이션됨.)
> - [x] **P1 모델 전환(2026-06-19)**: `TableCell` 도입(`Blocks`+`Background`+`Para` 편의 게터), `Cells: List<List<TableCell>>`, 셀은 블록 1개 유지(동작 100% 동일). 13파일 `Cells[r][c]`→`.Para`, Parent 사슬(`Run→Paragraph→TableCell→TableBlock`, `UpdateParents`가 셀 블록 순회), `LogicalCells()` 반환 타입 `TableCell`로. **셀 배경 = `TableCell.Background`로 승격하되 JSON 스키마 불변**(셀의 단일 블록 DTO에 배경을 싣고 읽을 때 `TableCell.Background`로 복원 → 레거시 문서 무료 호환). `TextPointer.CompareTo`/HTML/RTF 직렬화도 `.Para` 경유. **잠복 버그 클래스 확인**: `ReferenceEquals(cell, paragraph)`·`==` 비교가 `TableCell` vs `Paragraph`로 *컴파일은 통과하나 항상 false* → `BlockCaretTests`(←/→ 표 경계)가 정확히 잡아냄(`.cell.Para`로 수정), Rendering 컬링의 `Parent==tb`도 `as TableCell)?.Parent==tb`로. PublicAPI: `TableBlock.Cells.get`/`LogicalCells()` 시그니처 변경 `*REMOVED*`+신규, `TableCell` 9멤버 Unshipped 등재. **264 그린(P0 포함), 라이브러리 0 경고.** (동작 무변경이라 기존 스위트가 회귀 가드 — GUI 육안검증은 P3/P4에서.)
> - [x] **P2 측정 프리미티브(2026-06-19)**: `MeasureCellContentHeight(cell, innerWidth)` 도입 — 셀 높이 = 셀 블록 리스트의 높이 합(문서 측정 워크와 같은 형태, 셀 콘텐츠 박스로 스코프). `LayoutTable`의 셀 측정 2곳(base/rowspan)을 이걸 통하도록. 단일 문단이라 `BuildTextLayout(cell.Para,w).Height`와 동일 → **264 그린, 동작 무변경, 공개 API 무변경**. 셀은 자체 폭 규약(innerWidth 직접)이라 문서 `BlockExtent`(ParaLeft/MarginRight) 경유 안 함 — 셀 전용 유지. **렌더 쪽 `DrawBlockList` 일반화는 P3로 이관**(캐럿/선택/preedit/인라인이미지가 단일 문단에 얽혀 있어, 단일 블록만으로는 동작 동일성을 GUI로 검증할 수단이 없음 → 다중 블록 콘텐츠+GUI 검증이 함께 있는 P3에서). `_tableLayoutCache` 부모 전파는 중첩표가 생기는 P4로(현재 무의미).
> - [~] **P3 다중 블록 편집(진행 중 2026-06-19)**: [x] **렌더 일반화** — 셀 렌더 루프가 `cell.Blocks`를 위→아래 순회, 각 블록을 쌓아 그리고 캐럿/선택/preedit/인라인이미지를 캐럿이 든 문단에 라우팅(단일 블록=픽셀 동일, 회귀 안전). [x] **셀 Enter=문단 분할** — `SplitParagraphAtCaret`을 컨테이너 일반화(`p.Parent` → `Document.Blocks` 또는 `TableCell.Blocks`)해 셀 안에서 sibling 문단 생성, 표는 `InvalidateMeasure`로 행 높이 재계산. 회귀 테스트 1건(265 그린). [x] **셀 내 내비/병합** — `ParagraphsInOrder()`가 셀의 모든 문단을 열거(←/→ 횡단), `GetNext/PreviousParagraph`가 이를 사용. Backspace(셀 비-첫 문단 시작) → 셀 내 이전 문단으로 병합, Delete(셀 비-마지막 문단 끝) → 다음 문단 흡수(각 `InvalidateMeasure`). ↑/↓는 셀 내 비-첫/마지막 문단이면 셀을 떠나기 전에 인접 문단으로 스텝(블록-aware 히트테스트가 안착). 히트테스트(`GetPositionFromPoint`)가 셀의 스택 블록 중 포인트가 든 문단으로 하강(클릭/기하 ↑↓ 안착). "표 뒤"/← 진입 경계는 마지막 셀의 *마지막* 문단(`LastParaOf`)으로 정정. 회귀 테스트 4건(Enter 분할·←횡단·Backspace/Delete 병합) → **268 그린.** [x] **P3 마무리(2026-06-19, GUI 검증 완료)**: ① **링크 히트테스트 갭 해소** — `GetLinkRunAtPoint`이 셀의 스택 블록으로 하강(`GetPositionFromPoint`와 동형), 2번째+ 문단의 링크도 호버/클릭 동작. ② **셀 우클릭 메뉴 = 셀 밖 텍스트 메뉴와 동일**(표 관련만 차이) — 셀 편집 중엔 `BuildCaretMenu`(인라인이미지/링크/텍스트 분기 공유)로 셀 밖과 같은 메뉴, 단 표-삽입 픽커는 빼고(중첩표 미지원) 행/열/병합은 "표" 서브메뉴로. 표/셀 *선택* 상태(`_cellSelMode`/표 블록캐럿/셀-블록 드래그)는 기존 표 구조 메뉴. `BuildCellTextMenu` 제거(데드). ③ **셀 내용 복사 버그** — `CaptureBlockStructure`가 셀 내부 선택을 표 전체로 클론하던 것(양 끝점이 같은 셀이면 `null` 반환 → 인라인 클립보드). ④ **붙여넣기 위치 버그** — 다중블록 붙여넣기가 항상 top-level(표 뒤)로 새던 것을, `InsertBlocksAtCaret`이 캐럿 문단을 분할해 캐럿 위치에 삽입(첫 문단=캐럿 줄 이어붙임, 마지막=캐럿 뒤 이어붙임, 셀/본문 공통; 표 포함 붙여넣기는 중첩표 미지원이라 after-block 폴백). HTML/RTF/내부 리치 경로 통합. 회귀 테스트 4건(셀 내부 복사 null·셀 횡단 non-null·셀 분할 붙여넣기·셀 표 폴백) + 기존 top-level 붙여넣기 테스트 갱신 → **276 그린.**
> - [~] **P4 풍부한 셀(진행 중 2026-06-19)**: [x] **직렬화(P4-1)** — JSON/.flow 셀 인코딩을 다중 블록 지원으로 확장. 하위호환: 평범한 1문단 셀은 레거시 단일-문단 DTO 그대로(구 판독기 호환), 다중 블록/비문단 셀만 `Type="Cell"` 래퍼(`Blocks` 리스트, 재귀 — 중첩 표는 `BlockToDto`/`DtoToBlock` 재귀로 자동). **다중 문단 셀 영속성 데이터 손실 해소.** 테스트 2건(다중문단 왕복·평문셀 레거시 형식 유지) → 270 그린. [~] **P4-2a 블록 이미지/구분선 in 셀(2026-06-19)**: 셀 렌더 루프가 `ImageBlock`(셀 폭에 맞춰 비율 축소 `CellImageSize`)·`DividerBlock`을 그림, `MeasureCellContentHeight`·히트테스트 루프가 동일 높이로 전진(`CellImageSize` 공유). `InsertBlockAtCaret`이 캐럿이 셀일 때 이미지/구분선을 셀 블록으로 삽입(표는 P4-2b라 top-level 유지) + 뒤에 문단 보장. export 데이터 손실 보정: 평문/HTML(`<br>`)/RTF(`\par`)가 셀 전 문단 내보냄, 이미지 카운트가 셀 블록 이미지 포함. 테스트 2건(셀 이미지 삽입·왕복) → 272 그린. **셀 이미지 선택/리사이즈/삭제 chrome은 미구현**(후속). [x] **P4-2b 중첩 표(2026-06-19, GUI 검증 완료)** — 셀 안에 `TableBlock`을 재귀적으로 레이아웃/렌더/히트테스트. **재귀 프리미티브 추출**: 렌더 `DrawCellBlockList`(블록 리스트를 박스 안에 그림)↔`DrawNestedTable`(셀별 콜백)·히트테스트 `HitTestBlockList`/`LinkRunInBlockList`가 상호 재귀(임의 깊이), `MeasureCellContentHeight`는 `LayoutTable`과 이미 상호 재귀라 케이스만 추가(높이는 startX·top 독립이라 `(0,0)`로 측정). 모델 재귀: `UpdateParents`→`WireBlockParents`(Run→…→중첩셀 Parent 사슬), `ParagraphsInBlocks`(네비/Find/SelectAll 평면 열거), `FindCell`/`IsCellOf` 내부 표 인식. `TableCell.Para`가 첫 문단을 깊이 탐색(첫 블록이 표여도 안전). 삽입: `InsertBlockAtCaret`/`InsertTable`이 캐럿이 셀일 때 표를 셀에 중첩(캐럿=내부 첫 셀), 열 너비를 **셀 내부 폭에 맞춤**(하한 15px로 깊은 중첩도 셀 안에 fit). 네비: 셀 경계 블록캐럿 생성 4곳을 **top-level 표 한정**(중첩 표는 `MoveCaretLeft/Right`·기하 이동으로 빠져나옴 — 갇힘 해소). 셀 우클릭 "표 삽입" 재활성. 직렬화는 P4-1 재귀로 무료. 테스트 8건(왕복·Parent 사슬·높이·셀 삽입·셀맞춤 크기·← 탈출 등) → **282 그린.** **미구현(후속)**: 중첩 표 리사이즈 핸들, 중첩 경계 Tab. **[ ] P4-3**: 삽입 UI 다듬기.
> - [x] **P5 정리(2026-06-19)**: 셀 `\n` 특례는 P3에서 이미 코드상 제거됨(셀 Enter=문단 분할) — 남은 건 문서 표류 정정. `CLAUDE.md` 규칙3·4(셀=재귀 블록 컨테이너, 블록 캐럿 top-level 한정), `docs/DOCUMENT_FORMAT.md`(셀=블록 리스트, `Type:"Cell"` 래퍼 스키마, 트리/불변식), `CHANGELOG.md`([Unreleased] 마일스톤 A 항목) 갱신. 282 그린. **마일스톤 A 코어 완료** — 잔여는 선택적 후속(아래).
> - [x] **셀 이미지 chrome(2026-06-19, GUI 검증 완료)**: 셀 안 블록 이미지에 top-level과 동일한 선택 오버레이+테두리+우하단 리사이즈 핸들(`DrawCellBlockList`가 문서 좌표로 그려 `_imageHandles` 공유 — 리사이즈/호버 커서 무변경 재사용). 클릭 선택용 `_cellImageRects` 레지스트리(인라인 이미지 패턴), 삭제는 컨테이너 인식 `RemoveBlockAnywhere`(Delete/Backspace·Ctrl+X·우클릭 삭제 3경로, 재귀 셀 탐색)로 셀에서 제거 후 `InvalidateMeasure`. 테스트 1건(셀 이미지 선택→Delete) → **283 그린.**
> - [x] **셀 블록 정규화 + 인접 삭제(2026-06-19)**: `NormalizeBlocks`를 셀 블록 리스트까지 재귀(`NormalizeBlockList`)해 셀 안 표/이미지 앞뒤에도 캐럿 문단 보장(top-level 규칙). Backspace/Delete 셀 분기가 인접 비문단 블록(표/이미지/구분선) 삭제하도록 top-level과 일치 → 셀 안 표 위 Delete/아래 Backspace로 삭제(중첩 표 삭제 수단 확보). 테스트 +3 → 286.
> - [x] **중첩 표 리사이즈 핸들(2026-06-19, GUI 검증 완료)**: `DrawNestedTable`이 행·열 경계 핸들을 문서 좌표로 등록(top-level 리사이즈 경로 공유 — 핸들러가 TableBlock 기준이라 무변경 재사용). 바깥-오른쪽 열 드래그는 `EnclosingCellInnerWidth`로 전체 폭을 **셀 내부 폭에 클램프**(넘침 방지, 축소 자유). 행 높이는 셀이 따라 늘어 부모 표 reflow.
> - **후속(선택)**: 중첩 경계 Tab 이동, P4-3 삽입 UI 다듬기.
>
> **위험**: 새 아키텍처 없음(캐럿·표=블록 유지, 기존 워크 일반화). 폭은 넓음(13파일+Parent 스윕, P1에서 가장 조심). 헤드리스 약점으로 P3·P4는 RenderTargetBitmap 강제 패스+GUI 검증.

### 🔵 N6: 이미지 저장 모델 전환 및 성능 최적화 (미착수)

> **배경**: 현재 이미지는 `Bitmap` 객체가 데이터 주체이며, 저장 시 매번 PNG로 재인코딩된다. 원본이 JPEG(~80KB)여도 PNG(~500KB)로 부풀고, 직렬화마다 인코딩 비용이 발생한다. 외부 의존성 추가 없이(Avalonia 내장 + .NET 내장만) 용량·속도·화질을 동시에 개선한다.

#### 🟢 [완료] N6-1: JSON 스키마 버전 필드 (2026-06-10, alpha 선행)
- [x] `FlowDocumentDto`에 `Version` 필드 추가(`CurrentSchemaVersion=1`, Serialize가 기록).
- [x] 역직렬화 시 버전 미존재 → 초기값 `1`로 폴백(기존 문서 하위 호환). 테스트 2건(쓰기 포함·레거시 로드) 추가, 총 35→37.
- **목적**: 이후 스키마 변경(RawBytes, MimeType, 이미지 해시 참조 등)의 마이그레이션 경로 확보.
- **티어 변경 사유**: alpha 사용자가 `ToJson()`으로 문서를 저장하기 시작하는 순간 스키마는 사실상 동결된다. "NuGet 배포 전 필수"이므로 1.0이 아니라 **첫 공개(alpha) 전**에 있어야 한다. 30분 작업.

#### 🟢 [완료] N6-2: `byte[]` 중심 이미지 모델 (2026-06-10)
> 착수 조건(테스트 안전망)은 같은 날 선행 완료(37→64). 구현 후 72개 테스트 + 왕복 하네스 리포트 **이전과 완전 동일**(회귀 0) 확인.

- [x] `ImageBlock`/`InlineImage`에 `byte[] RawBytes` + `string MimeType` 추가(`SetImageData(bytes, mime, decoded?)`).
- [x] `Bitmap`은 렌더 캐시로 격하 — `Image` getter가 `RawBytes`에서 지연 디코드(실패 시 바이트 폐기로 매 렌더 재시도 방지). **`Image` setter 직접 대입은 RawBytes 무효화**(소비자 Bitmap-only 경로는 저장 시 PNG 폴백 유지).
- [x] **인제스천 경로 전부 바이트 캡처**: 클립보드 이미지(`TryGetImageAsync`가 (Bitmap, bytes) 반환), 파일 드롭, `InsertImageFromFileAsync`, 이미지 교체(블록/인라인), HTML 파서(`LoadImage`가 bytes 반환), 데모 삽입 버튼. 신규 공개 API **`InsertImageBytes(byte[])`** (원본 인코딩 보존 권장 경로).
- [x] **리사이즈**: 1920×1080 초과 시 인제스천에서 1회 다운스케일→PNG 바이트, 이하면 원본 바이트 그대로. 드래그 핸들은 Width/Height만 변경(세대 손실 없음, 기존 동작).
- [x] **Clone/Undo**: RawBytes·캐시 Bitmap 참조 공유(스냅샷당 추가 메모리 0).
- [x] **직렬화**: RawBytes→base64 직행(재인코딩 제거), DTO에 `MimeType` 추가, 레거시 문서(`MimeType` 없음)는 `image/png` 폴백. **역직렬화도 지연** — 문서 열기 시 Bitmap 디코드 0회.
- [x] **HTML 출력**: `data:{MimeType};base64,` 원본 포맷 반영, RawBytes 우선 검사로 export 시 디코드 회피. MIME 스니핑(`ImageMime.Detect`: png/jpeg/gif/bmp/webp).
- [x] **테스트 8건**(`ImageRawBytesTests`): 가짜 JPEG 바이트(디코드 불가)로 "재인코딩 없음"을 구조적으로 증명 — JSON 왕복(블록/인라인), 레거시 png 폴백, ToHtml 원본 mime, Clone 참조 공유, setter 무효화, 디코드 실패 무해성, MIME 스니핑. **총 64→72.**

| 항목 | 현재 | 개선 후 |
|------|------|---------|
| 저장 속도 | 이미지당 PNG 인코딩 수십~수백ms | base64 변환만 (~1ms) |
| 저장 용량 (사진 10장) | ~6.5MB (전부 PNG) | ~1.3MB (JPEG 원본 유지) |
| 문서 열기 | 모든 이미지 즉시 Bitmap 디코딩 | 화면 표시 시 지연 디코딩 |
| 리사이즈 화질 | 세대 손실 가능 | Width/Height만 변경, 원본 보존 |
| Undo 메모리 | Bitmap 참조 공유 (양호) | byte[] 참조 공유 (동일) |
| 외부 의존성 | 없음 | 없음 (Avalonia 내장만) |

#### 🟢 [완료] N6-3: 직렬화 비동기화 (2026-06-10)
- [x] 공개 API `ToJsonAsync()`/`LoadJsonAsync()` — `Task.Run` 백그라운드 직렬화/파싱. 기존 동기 `ToJson`/`LoadJson`은 유지.
- [x] **스냅샷 의미론**: `ToJsonAsync`는 호출 스레드에서 `Document.Clone()` 후 백그라운드 직렬화 — 직렬화 중 사용자 편집이 출력에 섞이지 않음(N6-2 덕에 클론이 바이트 참조 공유라 저비용). `LoadJsonAsync`는 파싱만 백그라운드(이미지 디코드는 N6-2로 이미 첫 렌더까지 지연), 문서 교체는 호출 컨텍스트에서.
- [x] 데모 저장/열기 버튼 비동기 API 전환. 테스트 2건(왕복+스냅샷 격리) — **총 72→74.**

#### 🟢 [완료] N6-4: 이미지 중복 제거 (해시 참조) (2026-06-11)
- [x] `SHA256(RawBytes)` 해시로 동일 이미지 식별 (`Convert.ToHexString(SHA256.HashData(bytes))`).
- [x] **JSON 스키마 v2**: 문서 루트 `Images` 풀(해시 키 → base64+MimeType), 블록/인라인은 `ImageRef`로 참조. 바이트 없는 Bitmap은 PNG 인코딩 후 풀 합류. `CurrentSchemaVersion` 1→2 (PublicAPI의 const 값 갱신 포함).
- [x] **레거시 호환**: v1 인라인 `ImageBase64`는 읽기 폴백 유지(MimeType 없으면 PNG). 로드 시 풀 항목당 한 번만 디코드 — 같은 해시를 참조하는 블록들은 **동일 byte[] 인스턴스 공유**(디스크+메모리 중복 제거).
- [x] 테스트 6건(`ImagePoolTests`: 중복 1회 저장, 왕복 복원, byte[] 공유, 상이 이미지 분리, v1 레거시 로드, 표 셀 이미지 풀 합류) — **총 98건 통과.**

#### 🟢 [완료] N6-5: 렌더링 Draw 컬링 (2026-06-11)
- [x] **뷰포트 밖 블록의 Draw 호출 생략** — 보수적 구현: yOffset 누적·레이아웃·캐럿 좌표는 전부 유지하고 draw 명령만 생략(히트테스트 3곳 무관). `Render`가 조상 `ScrollViewer`의 뷰포트를 `TranslatePoint`로 에디터 좌표 변환(줌/LayoutTransform 자동 처리), 상하 1뷰포트 여유. ScrollViewer 없으면 컬링 안 함(테스트/단독 호스팅 동작 불변).
- [x] **예외 보존**: 캐럿 문단·캐럿 블록(`_caretBlock`)·선택 블록(`_selectedBlock`)·표 내 캐럿(셀 `Parent` 체크)은 화면 밖이어도 그림 → `_lastCaretPoint`/`BringIntoView` 정상. 번호 리스트 카운터는 컬링된 문단에서도 증가(가시 영역 번호 연속성). 컬링된 이미지는 `Image` getter 자체를 건너뛰어 **지연 디코드도 회피**(N6-2 시너지).
- [x] **재그리기 계약 명시화**: 스크롤 시 Render 재실행을 우연(플랫폼 동작)에 맡기지 않도록 `OnAttachedToVisualTree`에서 호스트 `ScrollChanged` 구독 → `InvalidateVisual`. PublicAPI 2건 등재.
- [x] **검증**: 테스트 74개 전부 통과(동작 불변) + `--bench` 전/후 비교 — **100장 스크롤 29→51fps(컴포지트) / 33→59fps(무효화)**, Render() 중앙값 4.0→2.2ms(p95 12.9→4.5ms). 50장 이하는 원래 50fps+라 변화 없음(정상).

#### 🟢 [완료] N6-6: 대용량 문서 소프트 제한 (2026-06-11)
- [x] **실측 완료 (2026-06-11)** — 데모 `--bench` 하네스 신설(`BenchHarness.cs`, 실제 창+Skia+ScrollViewer, 결과 `bench-results.txt`·gitignore). 10/20/50/100장(800×600 PNG ~737KB + 장당 문단 5개) 자동 측정:
  | 장수 | 스크롤 fps (컴포지트/매프레임무효화) | Render() 중앙값 | 타이핑(코얼레싱 후) | 첫 키(undo 클론) | 저장 | 로드 | JSON |
  |---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
  | 10 | 57 / 60 | 1.4 ms | 0.2 ms | ~15 ms* | 60 ms | 64 ms | 10.5 MB |
  | 20 | 57 / 59 | 2.2 ms | 0.2 ms | 15 ms | 182 ms | 124 ms | 20.9 MB |
  | 50 | 51 / 58 | 3.1 ms | 0.3 ms | 49 ms | 196 ms | 402 ms | 52.3 MB |
  | 100 | **29 / 33** | 4.0 ms (p95 12.9) | 0.7 ms | **162 ms** | 397 ms | 585 ms | 104.6 MB |
  > \*10장 첫 키 69ms는 JIT 웜업 포함(20장 14.6ms가 실제 경향). **해석**: ① 편집(타이핑)은 100장에서도 sub-ms — 병목 아님. ② 스크롤은 50장까지 50fps+, **100장에서 29fps로 드랍** — 관리 Render는 4ms뿐이므로 병목은 렌더 스레드 래스터화(씬 크기) → Draw 컬링(N6-5)이 정확히 이 지점을 침. ③ 첫 키스트로크 undo 클론이 100장에서 162ms 히치(타이핑 런당 1회). ④ 저장/로드는 N6-3 비동기로 흡수 가능, JSON 100MB는 base64 고유 비용(N6-7 `.ardx`의 근거).
- [x] **임계값 결정 (2026-06-11)**: 지표는 문서 길이가 아니라 **이미지 개수**(실측상 타이핑은 100장에서도 sub-ms — 텍스트 길이는 병목 아님). 단일 임계값 **기본 50장**(N6-5 컬링 후 50장까지 50fps+ 근거), 모드별 자동 기본값은 채택 안 함 — 프리셋 오버라이드 의미론이 복잡해지는 대비 이득이 작아, ReadOnly 뷰어 호스트는 직접 100+로 올리도록 XML 문서로 안내.
- [x] **`MaxRecommendedImages` StyledProperty** (계획명 `MaxRecommendedLength`에서 개명 — 위 지표 결정 반영, int, 기본 50, 0 이하=비활성) + **`RecommendedImageLimitExceeded` 이벤트**(에지 트리거 — 초과 순간 1회, 한도 이하 복귀 시 재무장) + **`GetImageCount()`**(블록+인라인+표 셀 이미지 집계). 검사는 TextChanged 플러시 경로(`RaisePendingChangeEvents`)에서 수행. 데모는 상태바 옆 경고 라벨(현지화 ko/en, 한도 복귀 시 자동 해제)로 시연. PublicAPI 5건 등재, 테스트 5건(`ImageLimitTests`: 집계·1회 발화·재무장·이하 무발화·0=비활성) — **총 118건 통과.**
- **방침**: 하드 제한(입력 거부)이 아니라 **소프트 제한(경고)**. 데이터 손실 없음.
- **배경**: 가상화 없는 현재 아키텍처에서 편집 모드의 병목은 Undo Clone·입력 처리·이미지 저장 인코딩 3가지인데, ReadOnly에서는 전부 사라지고 Draw 호출만 남는다. 레이아웃 캐싱이 적용되어 있으므로 뷰어 용도는 상한이 훨씬 높음. 경쟁 비교 결과 무료/내장 에디터(웹 기반, WPF RTB 등)도 100장 이상에서 동일하게 고전.

#### 🟢 [완료] N6-7: `.flow` 패키지 파일 포맷 (2026-06-11, 확장자 `.ardx`→`.rdx`→`.flow` 변천 — 30차 최종)

> **방침**: 기존 JSON **문자열** 계약(`ToJson()`/`LoadJson()`)은 **그대로 유지**(이식·임베드·DB TEXT 컬럼·diff 용도). 그 **위에** 파일 저장용 ZIP 컨테이너 포맷을 **추가**한다. 치환이 아니라 계층 추가.

- [x] **파일 포맷 `.flow`** (ZIP 컨테이너 — `System.IO.Compression.ZipArchive`, 외부 의존성 없음·AOT 호환): `document.json`(스키마 v2, 풀 항목은 MimeType만) + `images/<sha256>`(원본 바이트 — N6-4 해시 키 재사용, 중복 제거 그대로). meta.json은 생략(스키마 버전이 document.json에 있음).
- [x] **추가 API**: `RichEditor.SavePackageAsync(Stream)`/`LoadPackageAsync(Stream)`(스냅샷·백그라운드 — ToJsonAsync 패턴) + `Formatters.DocumentPackage.Save/Load`(동기). PublicAPI 등재. 데모 저장/열기 피커에 .flow 추가(로드는 ZIP 매직 "PK" 스니핑).
- [x] **이미지 엔트리 무압축(Stored)** 확인 — document.json만 Deflate. 테스트 5건(왕복=JSON 동치, 바이트/MIME 복원+byte[] 공유, 1회 저장·무압축·base64 부재, 깨진 입력=빈 문서, 용량<JSON) — **총 113건 통과.**
- [x] **(보너스) 저장 시 강제 디코드 회귀 수정**: N6-4 풀 리팩터가 `PoolImage` 인자로 `.Image`(지연 디코드 게터)를 무조건 평가하던 문제 — RawBytes 있으면 게터를 건드리지 않게 복원(저장이 다시 디코드-프리, 디코드 실패 시 RawBytes 소실 가능성도 제거).
- 잔여(낮은 우선순위): 지연 로딩(뼈대 먼저 렌더 → 이미지 바이트 백그라운드 채움) — 현재도 디코드는 첫 렌더까지 지연되므로 바이트 복사 비용만 남음.
- **참고**: DB(SQLite) 저장 용도라면 `.flow`보다 `ToJson()` 문자열을 TEXT 컬럼에 넣는 편이 검색 텍스트 분리·쿼리에 유리. `.flow`는 **파일로 주고받는** 시나리오용.

---

### ✅ 배포 전 최종 체크리스트 (`0.1.0-alpha`)
- [x] **GitHub 저장소 푸시 + CI 첫 실행 그린** (2026-06-10, 3-OS 그린 — "🚨 최우선" 절에서 완료 처리)
- [x] **N6-1: JSON 스키마 버전 필드(`Version`)** (2026-06-10) — 레거시 폴백 + 테스트 2건.
- [x] N1(패키징, SourceLink 포함) + N2(최소 공개 API/문서) + N3(Windows 동작 보장, 타 플랫폼 명시) 완료
- [x] `dotnet pack -c Release` 성공, 빈 앱에서 설치·호스팅 성공 (2026-06-11) — 저장소 밖 빈 Avalonia 앱(`%TEMP%\AreSmokeApp`)에서 로컬 피드로 0.1.0-alpha 설치 → `RichEditorView` 호스팅 빌드·실행 확인.
- [x] README의 사용 예제가 실제로 컴파일/동작 (2026-06-11) — 모든 API 참조를 `PublicAPI.Unshipped.txt`(분석기 생성)와 대조 검증.
- [x] LICENSE·저작권·서드파티(HtmlAgilityPack) 라이선스 고지 (2026-06-11) — `THIRD-PARTY-NOTICES.md`(Avalonia·HtmlAgilityPack, 둘 다 MIT) 추가 + 패키지 동봉 + README 링크.
- [x] 버전 `0.1.0-alpha`, 변경 이력(CHANGELOG) 시작 (`CHANGELOG.md` 2026-06-10 작성, 2026-06-11 N3.6 항목 추가)
- [ ] (권장) NuGet 푸시 전 별도 테스트 계정/프리릴리스 채널로 1차 공개 — **보류 결정(2026-06-11)**: 버전은 `0.1.0-alpha` 유지(툴바·현지화 포함 — 첫 공개라 번호 부담 없음, 0.2.0 승격 안 함), NuGet 공개는 나중에. 공개 시: API 키 발급 후 `dotnet nuget push` 또는 CI 시크릿 등록 + `v0.1.0-alpha` 태그 푸시.

### ✅ `1.0` 프로덕션 체크리스트
> 새 기능 추가 없이 기존 기능의 **안정성·성능·문서화**를 프로덕션 수준으로 끌어올린다.

**안정성 (성능보다 먼저 — N6-2의 안전망):**
- [x] 테스트 커버리지 확대 — **핵심 편집 경로 완료(2026-06-10, 37→64)**: 오프셋 모델·멀티문단 삭제/스타일·부분 서식 분할 10건 + 키 입력 파이프라인(Backspace/Delete 병합, Enter 분할, Undo 코얼레싱) 11건 + 붙여넣기 구성요소(CF_HTML, InsertHtml) 6건. **N6-2 착수 조건 충족.** 잔여(낮은 우선순위): async 클립보드 획득 체인·레이아웃 의존 키·렌더 픽셀 단언.
- [x] CI 3-OS 매트릭스 그린 확인 (2026-06-10, alpha 체크리스트에서 선행 처리됨)

**성능 (테스트 보강 후 착수):**
- [x] N6-2: `byte[]` 이미지 모델 전환 (2026-06-10 — 원본 바이트 보존, 지연 Bitmap 캐시, 외부 의존성 없음, 테스트 72개+왕복 하네스 회귀 0)
- [x] N6-3: 직렬화 비동기화 (2026-06-10 — `ToJsonAsync`/`LoadJsonAsync`, 스냅샷 의미론, 테스트 74개) → **1.0 성능 항목 전부 완료**
- ~~N6-1: JSON 스키마 버전 필드~~ → **`0.1.0-alpha` 체크리스트로 이동** (2026-06-10)

**문서화·API:**
- [x] **문서 형식 명세서 (2026-06-12)** — [`docs/DOCUMENT_FORMAT.md`](docs/DOCUMENT_FORMAT.md): JSON 스키마 v2(버전 이력·필드 표·레거시 v1 폴백·색상 형식·셀 병합 마커 규약)+`.flow` ZIP 구조+호환성 정책(판독기/작성기 의무, 스키마 변경 절차). README 링크 추가. 외부 소비자에게 저장 포맷이 공개 계약이 되는 alpha 시점의 필수 문서.
- [x] **공개 멤버 XML 문서 주석 완성 (2026-06-10)** — CS1591 경고 0개.
- [x] **API 동결 가드: `Microsoft.CodeAnalysis.PublicApiAnalyzers` 도입 (2026-06-10)**

**1.0 이후 (2.0+) 후보:**
- N6-4 이미지 중복 제거, 블록 여백 제어, DOCX 파싱, 마크다운, 동시편집, 페이지네이션, 플러그인 시스템. (~~N6-5 렌더링 가상화~~ → Draw 컬링으로 2026-06-11 완료. ~~델타 Undo~~ → 클론 0.28ms 실측으로 2026-06-12 기각)

### ❗ 출시 전 결정 필요 (Open Decisions)
- 라이선스 종류(MIT 권장?), 패키지 ID 최종(`AvaloniaRichEditor` 선점 여부 확인), 지원 Avalonia 버전 범위, 크로스플랫폼 보장 수준(알파에서 Windows-only로 갈지).

---
**마지막 업데이트**: 2026년 6월 18일 (33차) — **📏 글자 크기 pt 전면 통일 (item 6, A안 — 인계 작업)**: 32차에서 확정한 A안대로 모델·공개 API·직렬화 전부를 **pt로 통일**, 렌더 경계 한 곳에서만 px 변환. 메인 233 + 렌더 6 그린(테스트 수 불변), 빌드 경고 0. **변경 핵심**: ① `Run.FontSize`·`DefaultFontSize`·`CaretFormat.FontSize`·JSON/`.flow`/HTML/RTF 직렬화가 모두 pt. ② 단일 헬퍼 `RichEditor.PtToPx(pt)=pt×4/3`를 **렌더 경계 4곳만** 적용(BuildTextLayout 런 props·defaultProps·preedit + DrawListMarker FormattedText + 캐럿 높이 `CaretTextHeight`). 그 외 엔진은 전부 pt로 말함. ③ 본문 기본값 14px→**10pt**(`const BodyFontSizePt=10`, `DefaultFontSize` 기본·`Run.FontSize` 기본·`RunSizeIsBodyDefault` 매직넘버·`GetCaretFormat`/`ClearFormatting` 폴백 일괄). ④ 제목 래더 pt로(h1~6 = 20/16/14/12/11/10) — 레이아웃(`HeadingFontSize`)·HTML(`HeadingSize`)·RTF(`HeadingSize`) **3곳 동일 값**으로 통일(드리프트 방지). ⑤ HTML: 가져오기 px→pt(×0.75)·pt 통과, 내보내기 pt 직접(종전 ×0.75 제거)·기본 스킵 14→10. RTF `\fs=pt×2`는 모델이 이미 pt라 자동 정확(폴백 14→10만). 클립보드 HTML 래퍼 10.5pt→10pt. ⑥ 툴바 크기 목록 pt로(8~72, 14개), 컨텍스트 메뉴 목록(10~36)은 이미 pt-유효라 유지. **픽스처/문서 갱신**: `docs/DOCUMENT_FORMAT.md`(FontSize 필드=pt·예제 24→20·14→10·스키마 노트에 "단위 pt, 버전 범프 없음" 명시), `DocumentFormatSpecTests`(24→20), `HtmlFormatterTests`(15pt→20pt), `RichEditorHeadingTests`(본문 14→10), `CHANGELOG`에 **BREAKING**(px→pt) 명시. **버전 범프·런타임 마이그레이션 없음**(스키마 v2 유지) — 구 px 문서는 같은 숫자를 pt로 읽어 ~33% 크게 보이나 베타·사용자 없음. **🔜 다음: item 5만 남음**(글머리표·번호 스타일 + 툴바 아이콘 — `DrawListMarker` `"•"`/`"N."` 하드코딩 교체 [RichEditor.cs ~877], 모델 enum, 직렬화/HTML/RTF/UI). 첫 줄 들여쓰기는 Avalonia 미지원으로 계속 보류(메모리 `avalonia-no-firstline-indent`). **➕ 줄 간격 비례(%) 추가(같은 세션, 사용자 요청)**: 표준 조사 결과 "글자 크기 비례 배수/%가 1차 표준"(HWP %·Word 배수·CSS 무단위 — %=배수×100), 절대값은 2차("고정값"). 기존 `Paragraph.LineHeight`는 **주석은 "multiplier"인데 구현은 절대 px**였고 PaginationTests가 그 절대 px를 픽스처로 의존 → **덮어쓰기 대신 추가(비파괴)**: `LineHeight`(절대 px="고정값", 주석 정정) 유지 + **`LineSpacing`(비례 배수, NaN=미설정) 신설**, 우선순위 LineSpacing>LineHeight>자동. 렌더 경계에서 `배수×최대런pt→px×NaturalLineFactor(1.2)`, 단 ≤1.0은 NaN(폰트 자연높이=클리핑 방지). `ParagraphSig`·Clone·직렬화(nullable, 레거시 호환)·Clipboard 선택복사·빈문단 히트테스트 반영. 툴바 줄간격 콤보를 HWP식 **%(100~300%, 기본 콤보 100%)**로 — `SetLineSpacing(%/100)`. 공개 API 순수 추가 3건(PublicAPI.Unshipped). 테스트 2건(직렬화 왕복=배수/절대 독립, 렌더=2배가 단일보다 크고 글자 클수록 증분 큼) → 메인 233→235·렌더 6 그린. 문서: `DOCUMENT_FORMAT.md` 필드 2개(LineHeight 절대·LineSpacing 비례)·CHANGELOG Added. **➕ 문서 포맷 버전 SemVer "1.0" 전환(같은 세션, 사용자 요청)**: 사용자가 "JSON 포맷 자체 버전을 1.0으로"(NuGet 릴리스와 별개) 원함. **동기**: pt 변경이 `FontSize` 의미(px→pt)를 v2 안에서 바꿔 "버전 동일·의미 상이" 잠복 문제가 있었음 → 1.0을 안정 기준선으로 명명. **구현**: `CurrentSchemaVersion` int 2 → **string "1.0"**, DTO `Version` int→string + **`SchemaVersionConverter`**(숫자/문자열 둘 다 허용 — 레거시 정수 `1·2` 문서 계속 읽음, 로직은 Version 미사용=쓰기전용 스탬프라 기능영향 0). PublicAPI const 타입 교체(`*REMOVED*` int + 신 string). **`.flow`에 `meta.json` 컨테이너 마커 추가**(`{"format":"flow","version":"1.0"}`, DocumentSerializer 버전과 동일 출처, 판독기 부재 허용=하위호환). 테스트 1건(레거시 숫자 버전 읽기→"1.0" 재직렬화), 메인 235→236. 문서: `DOCUMENT_FORMAT.md` 버전 이력 표·루트 예제·`.flow` 구조에 meta.json·CHANGELOG Changed. **주의**: NuGet 패키지 버전은 여전히 별개(현 0.6.0-beta) — 사용자 의도는 "문서 포맷 버전"만 1.0. **➕ item 5(글머리표·번호 스타일 + 툴바 아이콘) + 줄간격 아이콘화(같은 세션, 인계 잔여 완료)**: **줄간격 아이콘** — 콤보를 **아이콘 드롭다운 버튼**으로(줄간격 글리프 + **현재 % 라벨**(캐럿 반영, `CaretFormat.LineSpacing` 추가) + 셰브론 → "100%/130%…" 메뉴, `BuildTableButton` 패턴 재사용), `RichEditorIcon.LineSpacing` 슬롯 **enum 끝에 추가**(중간 삽입 시 API-추적 ordinal 71개 어긋남 → 끝에 append 필수). **item 5** — `ListMarkerStyle` enum 신설(Default/Disc/Circle/Square/Dash/Decimal/DecimalParen/LowerAlpha/UpperAlpha/LowerRoman) + `Paragraph.ListMarker` 속성. `DrawListMarker`의 하드코딩 `"•"`/`"N."`을 **`ListMarkerText(kind,style,num)`** 로 교체(글머리표 글리프 + 번호 포맷), 번호 헬퍼 `ToAlpha`(bijective base-26)/`ToRoman`. `SetListStyle(style)` 공개 API(스타일이 kind 함의, 항상 on) — `SetListType`에 `marker?` 스레드(스타일 픽은 토글 안 함). 직렬화: JSON `ListMarker`(nullable, Default=생략), HTML `list-style-type` 매핑(disc/circle/square·decimal/lower-alpha/upper-alpha/lower-roman, dash·")"접미는 무손실 불가=lossy), RTF 리터럴 마커(`ListMarkerText` 재사용·비ASCII `\u`). 툴바: 글머리표/번호 버튼 옆 **▾ 드롭다운**(`BuildListStyleDropdown`)으로 스타일 선택 + **BulletList/NumberedList 벡터 아이콘** 추가(점+선/숫자+선). 컨텍스트 메뉴 List 하위에 글머리표모양·번호모양 서브메뉴. 현지화 2키(ko/en). `ParagraphSig`·Clone·Clipboard 선택복사 반영. 테스트: 형식/직렬화/HTML왕복 6건(theory 포함 +19) → 메인 236→255·렌더 6 그린, 빌드 경고 0. 문서: `DOCUMENT_FORMAT.md` ListMarker 필드·CHANGELOG Added 2건. **🔜 잔여 없음** — 32차 인계분(item 5)·줄간격 % 모두 완료. 첫 줄 들여쓰기만 Avalonia 미지원으로 계속 보류. **(사용자 요청)** 인용(Quote) **툴바 버튼 제거** — 기능(`ToggleQuote`·우클릭 List 메뉴·blockquote 모델/직렬화)은 유지, 툴바에서만 뺌(CHANGELOG의 [Unreleased] Quote 항목도 "툴바" 문구 제거). **🐛 큰 글자/줄간격 캐럿·선택 기하 수정(커밋 15798a4, 사용자 검증)**: pt·줄간격 작업에서 드러난 2건 — ① 큰 줄간격(300%)에서 캐럿이 글자와 어긋남 → 줄 박스 안 캐럿을 **가운데 정렬**(`CaretYInLine`, Avalonia가 늘어난 줄높이를 위/아래 절반씩 분배; 인라인 이미지 줄만 바닥정렬 유지). ② 선택 후 글자 크기 변경 시 하이라이트가 한 프레임 늦게 갱신("커지다 멈춤") → `ApplyStyleToSelection`이 `InvalidateVisual`만 호출하고 **`InvalidateMeasure` 누락**(편집 경로 `NotifyStatus`는 호출)이라 측정/배치가 stale → 측정도 무효화하도록 수정. **🎨 툴바 UI 전면 다듬기(커밋 9f32a44, 사용자 반복 피드백)**: 줄간격을 **콤보형 박스**([아이콘|편집% 칸|▾ 프리셋|▲▼ 스피너], 숫자 직접입력+Enter·스피너±10%), 글머리표·번호도 같은 **콤보형 박스**([아이콘 토글|현재 마커|▾ 스타일], `CaretFormat.ListMarker` 추가로 현재 마커 실시간 표시·리스트 밖이면 흐리게). 공통: 드롭다운 메뉴를 박스 하단에 펼침(`SetAttachedFlyout`+`BottomEdgeAlignedLeft`), 셰브론 얇고 연하게(직접 Path #70757A 1.1px), 스피너 벡터 셰브론(드롭다운보다 작게), 컨트롤 높이 28 통일(툴바·뷰 콤보·박스), 벡터 아이콘 16→20px(버튼 크기 유지·여백 7,3, 선 2→1.5px), 서식복사 툴바 버튼 제거(API 유지). **공개 API 추가**: `CaretFormat.LineSpacing`/`ListMarker`(PublicAPI Unshipped). (시행착오: 아이콘 일괄 16→20 교체에 PowerShell `Set-Content` 사용 → PS5.1 인코딩 함정으로 `ToolbarIcons.cs` 주석 비ASCII(×·—) 깨짐 → Write로 재작성 복구. 비ASCII 파일에 PowerShell `Get/Set-Content` 금지.) **🚀 NuGet 정식 게시: `AvaloniaRichEditor 0.7.0`(사용자 결정: 베타 제거)**: 0.6.0-beta 이후 누적분(pt breaking·줄간격·리스트마커·RTF내보내기·포맷버전1.0·툴바UI·캐럿/선택수정) 묶어 게시. **버전 평가→`0.7.0`**: 0.x breaking→minor 범프 + **-beta 제거**(0.x 자체가 1.0 전 신호라 -beta 중복, NuGet 정식 노출). 게시 전 정리: CHANGELOG `[0.7.0]` 절(미기록분 보강·pt "스키마2유지" 모순 정정), PublicAPI Unshipped→Shipped(신규30·제거2), README/csproj 0.7.0, Release pack·256테스트 검증. 태그 `v0.7.0` 푸시→CI(3-OS+렌더픽셀 통과)→Trusted Publishing(OIDC) Push 성공 + GitHub Release. **문서 포맷 버전("1.0")과 패키지 버전(0.7.0)은 별개 축**(정상). / (32차) — **✍️ 에디터 서식 기능 묶음 (사용자 요청, 3/6 완료·1 보류·2 인계)**: 사용자가 요청한 6개 서식 개선을 1~3 위험 낮은 순으로 처리, 각 독립 커밋·테스트 그린(메인 233 + 렌더 6). **① 이미지 메뉴(커밋 84d0f26)**: 크기 프리셋(원본/½/⅓/¼)을 "크기" 하위메뉴로, 분수는 **현재 표시 크기 기준**(누적, 종전 원본 기준)으로, 블록 이미지 **좌클릭 선택**(파란 테두리, 우클릭·인라인과 일관). **② 문단 스타일(커밋 5a87f27)**: 스타일 콤보 제목1~3→**제목1~6**, **인용(Quote) 토글** 공개 API `ToggleQuote()`(불릿/번호처럼) + 툴바 버튼 + 컨텍스트 메뉴, `CaretFormat.Quote` 추가(PublicAPI `*REMOVED*` ctor 패턴). **③ 양쪽 정렬(커밋 b643ee8)**: 툴바/컨텍스트 메뉴에 Justify 추가, HTML 가져오기·내보내기 보강(JSON·RTF `\qj`는 이미 처리), **실-Skia 렌더 테스트로 Avalonia 12가 양쪽 정렬을 실제 렌더함을 검증**. **④ 첫 줄 들여/내어쓰기 — 보류(되돌림)**: 모델/JSON/HTML/RTF/UI 배선까지 만들었으나 **Avalonia 12.0.1 `TextLayout`이 문단 `indent`(첫 줄 들여쓰기) 파라미터를 완전 무시**함을 `TextLine.Start` 프로빙으로 확정(메모리 `avalonia-no-firstline-indent` 기록). in-app 렌더는 첫 줄만 수평 오프셋하는 커스텀 구현(렌더 per-line + 선택·캐럿·인라인이미지·히트테스트 3곳 보정 = G1 통합 기하 전부 건드림, 회귀 위험)이 필요 → 사용자 결정으로 전부 revert. **결정 사항(인계)**: 글자 크기는 **포인트로 전면 통일**(UI만 pt 아닌 모델·공개 API·직렬화 모두 pt, px 변환은 Avalonia 렌더 경계 한 곳), 기본 **10pt**, 목록 ~6~72pt. **🔜 다음 세션 남은 작업 2건**: **(5) 글머리표·번호 스타일 + 툴바 아이콘** — 글머리표 •/◦/▪/–, 번호 1./1)/a)/A)/i) 선택지(모델에 스타일 enum 추가, `DrawListMarker`가 현재 `"•"`/`"N."` 하드코딩 [RichEditor.cs ~876], 직렬화/HTML/RTF/컨텍스트·툴바 UI + `ToolbarIcons` 벡터 아이콘 교체). **(6) pt 전면 통일 + 글자 크기 목록/기본값** — 가장 큼. **방식 확정(사용자 결정 2026-06-17): A안 — 버전 범프·런타임 마이그레이션 둘 다 없이 모델·공개 API·직렬화 전부 pt로 통일**(JSON도 pt 저장 = 완전 일관, B안의 "모델 pt/JSON px 경계 변환"은 기각 — 베타라 호환 부담 없고 A가 더 단순·일관). 작업: 하드코딩 `14px` 리터럴 감사(`HeadingFontSize`·`RunSizeIsBodyDefault`·preedit·`GetCaretFormat` 폴백 등 — pt 기준값으로), `BuildTextLayout` 렌더 경계에서 pt→px ×4/3, RTF `\fs=pt×2`(자동 정확), HTML pt→px 내보내기·px→pt 가져오기, 툴바/컨텍스트 크기 목록 pt로(6~72, 기본 10pt). **런타임 마이그레이션 없음** 대신 **착수 1회 정리**: 저장소에 px로 커밋된 샘플/픽스처 문서를 pt로 갱신(특히 `docs/DOCUMENT_FORMAT.md` 예제 JSON + `DocumentFormatSpecTests`), `DOCUMENT_FORMAT.md`에 "글자 크기 단위=pt(이전 px)" 명시, CHANGELOG에 **breaking**(글자 크기 px→pt) 명시. (기존 px 저장 문서는 33% 커져 보이지만 베타·사용자 없음이라 픽스처 갱신으로 충분.) **참고**: 첫 줄 들여쓰기는 Avalonia가 지원하기 전엔 보류(메모리 참조). / (31차) — **🎯 1.0 검증 게이트 3개 처리 + RTF 내보내기 (30차 이후 공백 정합화)**: 30차(6/14) 이후 푸터에 미기록이던 작업 묶음을 정리. ① **`0.6.0-beta` 게시**(첫 베타 — API 안정화 신호, `GetRichInlines` Shipped 승격, 본문 N1/350행). ② **G1 기하 워커 통합 완료**(P5 렌더까지 `BlockExtent` 단일 출처로 — 워커 드리프트 버그 클래스 구조적 제거, 본문 G1/336행). ③ **1.0 검증 게이트 3개**(버전 전략 line 192–203, "1.0은 기능이 아니라 *증명*"): **게이트 ① 렌더 픽셀 테스트** — 별도 프로젝트 `AvaloniaRichEditor.Tests.Render`(`UseHeadlessDrawing=false`+`.UseSkia()`+번들 Inter), 구조적 픽셀 5건(글리프 래스터·제목>본문 ink·구분선·**페이지 경계 분할**·**선택 하이라이트**, 선택은 채널 순서 무관 채도 검출로 macOS 수정), 3-OS CI 통합(Linux `libfontconfig1`). **게이트 ③ 성능 실측** — `--bench-text`로 1000/3000/6000문단(~70/210/420p) 측정: **선형 스케일링·O(n²)·누수 없음**(힙 19→37→57MB, 타이핑 2.8→21ms, 스크롤 28–46fps), 수백 페이지까지 사용 가능, 회귀 가드는 **메모리 상한** 헤드리스 테스트. **게이트 ② mac/Linux 기능 실검증** — 자동화 불가(macOS CI 헤드리스·Linux IME 포워딩 불완전)라 **수동·하드웨어 의존으로 스코프**(README best-effort 명시, WSLg로 일부 무료 검증 가능). ④ **RTF 내보내기**(`ToRtf()`/`LoadRtf()`, `RtfDocumentFormatter.Write` — 27차 가져오기와 대칭): 문단·런 서식·정렬·들여쓰기·제목·리스트·표·PNG/JPEG 이미지, 비ASCII는 `\u` 이스케이프(코드페이지 독립). `RichEditorView` Export/Import에 `.rtf` 추가, 의존성 0. **현 상태**: 검증 게이트 3개 사실상 닫힘(①③ 자동, ② 문서화된 수동) → **릴리스 방향 결정 분기점**(0.6.0 정식 승격 / 1.0.0-rc.1 착수 / RTF를 0.7.0-beta로). RTF 내보내기는 `CHANGELOG.md` `[Unreleased]`에 대기. / (30차) — **🔍 DOCX/벡터 도형 클립보드 파싱 조사 → 강등(코드 변경 없음)**: 백로그 "DOCX 클립보드 파싱" 항목을 실측 검증. 데모에 임시 클립보드 포맷 덤프 버튼을 넣어 HWP/Word 도형 복사 시 실제 올라오는 포맷을 확인(검증 후 버튼 제거). **핵심 발견**: ① 당초 전제(워드/한글이 OOXML `<w:tbl>`을 클립보드 텍스트로 올림)는 틀림 — 도형은 `CF_ENHMETAFILE`이 아니라 **`Bitmap`(CF_BITMAP)으로 동봉**되어 기존 비트맵 분기가 이미 처리, **EMF 디코더 불필요**. ② HWP는 `DOCX Format` 패키지를 클립보드에 직접 올림(OLE2 불필요, "편집 가능 임포트"는 가능하나 대형·별개). ③ 붙여넣기 행동 검증: HWP 글상자·Word 스마트아트=그림으로 정상, Word 글상자·워드아트=텍스트만(우아한 강등), **HWP 글맵시만 빈 결과로 안 보임=유일한 실손실**(RTF 공백 문단이 비트맵 폴백 전 return). → 백로그 항목을 "조사 후 강등(EMF/OOXML 불요)"으로 정정, 글맵시는 알려진 한계로 기록(수정 시 `RichEditor.Clipboard.cs`의 `empty` 판정을 공백-only까지 좁힘, 미착수). 코드 변경 0, 테스트 195 유지. **추가: 패키지 확장자 `.rdx`→`.flow` 확정**(사용자 결정 — `FlowDocument` 모델 연상, 4자, 충돌 없음). `.rdx`는 Unreleased에만 있어 미게시 → 하위호환 불요. 포맷·Stream API 불변, 데모 피커/스니핑·문서 주석·문서(README/DOCUMENT_FORMAT/CHANGELOG)·테스트(`RdxPackageTests`→`FlowPackageTests`)만 교체. / (29차) — **📄 페이지 레이아웃 재설계 + 용지/방향**: 단일 `PageView` bool을 직교 2축으로 분해 — **`PageSize`**(`RichEditorPageSize`: Continuous/A4/A3/A5/B4/B5/Letter/Legal/Tabloid, JIS B) + **`ShowPageBoundaries`** + **`PageOrientation`**(Portrait/Landscape). `ContentLayoutWidth`·`MapDocToView/ViewToDoc`·Render·Measure를 3상태(연속 / 용지+윤곽 페이지스택 / 용지+무윤곽 중앙 고정폭)로 분기. **기본값 변경: A4+윤곽**(기존 연속 → 호스트는 `PageSize=Continuous`로 복원). 무윤곽 모드는 쪽 사이 **여백(`NoChromePageGap`=40px) 주입 + 가운데 점선 구분선**(윤곽 모드의 갭주입·클립·리플레이 기계를 데스크/종이/여백 빼고 일반화). 용지 치수 인스턴스 게터(방향 스왑) + 공개 **`GetPaperPixelSize()`**. 인쇄/PDF가 선택 용지·방향 따름(Continuous→A4 폴백). **버그 수정**: 윤곽 `MapViewToDoc`가 `A4PageHeight` 하드코딩 → `PaperHeight`(비A4 용지 히트테스트 어긋남). 데모: 용지 콤보(9종)+방향 콤보+쪽윤곽 체크(연속 시 비활성), fit를 `종이+양쪽 데스크갭` 기준으로(우측 잘림/비대칭 수정). **용어**: "자유"→"연속"(enum `Free`→`Continuous`, 코드의 continuous 용어와 일치). **패키지 확장자 `.ardx`→`.rdx`**(더 범용적, 포맷·Stream API 불변). `PageView` 공개 API 제거(알파, PublicAPI `*REMOVED*`). 테스트 184→195(페이지 3상태·용지폭·방향스왑·무윤곽갭 등). 빌드 경고 0. / (28차) — **🧹 소규모 잔여 3건 처리**: ① **명세-코드 표류 감지** — `docs/DOCUMENT_FORMAT.md` §2.6 예제를 로드 가능한 자체 일관 JSON으로 확정(1×1 PNG 실바이트 + 매칭 SHA-256 풀 키), `DocumentFormatSpecTests`가 문서에서 ```json 추출→`Deserialize`→문서화 구조 단언(필드명/판별자 표류 시 실패). 로드맵의 "왕복 하네스에 추가" 대신 **테스트**로 — CI는 `dotnet test`만 돌므로 표류를 CI에서 잡으려면 그게 맞는 그릇. ② **데모 표준 이벤트 마이그레이션** — `MainWindow`가 레거시 coarse `StatusChanged` 단일 구독을 `SelectionChanged`(캐럿 카운트)+`TextChanged`(페이지 수·이미지 제한 경고, O(blocks) 워크는 편집 시에만)로 분리, `_lastChars` 가드 핵 제거. ③ **레이아웃 의존 키 커버** — `RichEditorCaretNavigationTests` 5건(Home/End/Up/Down): top-level Window 없이 `RenderTargetBitmap.Render`로 Render 강제(no-Window 규칙 준수)해 `_lastCaretPoint`·레이아웃 캐시 실제화, 마커 입력 위치로 캐럿 간접 단언, 변별 테스트(바닥 Up→가운데 문단). **잔여 갭 2건은 여전히 차단**: async 클립보드 체인(TopLevel+페이크 필요)·렌더 픽셀 단언(헤드리스 기본 드로잉 no-op). 테스트 184→190, 빌드 경고 0. / (27차) — **🚀 NuGet 게시: `AvaloniaRichEditor 0.4.0-alpha`** (Trusted Publishing, GitHub Release). **클립보드 상호운용 + 에디터 UX 라운드**: ① **RTF 붙여넣기**(`RtfDocumentFormatter`, 의존성 0) — Word/HWP의 "Rich Text Format"을 CF_HTML보다 먼저 파싱(이미지 바이트 내장 → 임시파일 참조 유실 회피). 토크나이저+그룹상태 스택: 문단·b/i/ul/strike·`\fs`·`\cf`(색상표)·**CJK는 `\ansicpg` 코드페이지로 `\'hh` 바이트 묶음 디코드**(CodePagesEncodingProvider, .NET10 프레임워크 제공이라 의존성 0)·이미지(`\pict` png/jpeg, `\*\shppict` 우선)·표(`\trowd/\cell/\row` + `\cellx` 원본 열너비)·중첩표/글상자(`\nestcell`/`\shptxt`) 평탄화. 실 HWP 검증(클립보드 덤프). **한계**: HWP가 글상자/도형을 `\wmetafile`(WMF/EMF 벡터)로 내보내면 디코드 불가 → 백로그(의존성 결정). ② **HTML 비동기**(`ParseHtmlAsync`/`LoadHtmlAsync`) — 원격 이미지를 UI 스레드 밖에서 동시 선반입(이슈 #1과 동일 thread-affine 제약, 같은 분리 패턴). ③ 에디터 UX: 표 생성=문서폭 균등열, 이미지 삽입=문서폭 제한(비율), 삽입 후 캐럿(표→첫셀/이미지→다음문단)+포커스+BringIntoView, 표 선택 표현(좌상단 테두리 SizeAll 커서 + 프레임/채움), 행/열 리사이즈 멈칫(`InvalidateMeasure` 누락) 수정, RichEditorView 좌상단 클리핑/우측 스크롤바 여백(에디터 Margin). ④ **헤드리스 플레이키 근본 수정**: `RichEditor` 정적 cctor의 커서 정적 초기화(`new Cursor`=`ICursorFactory` 요구)를 지연 생성(`Cur(t)` 캐시)으로 → 플랫폼 미초기화 시 cctor throw로 macOS CI 무더기 실패하던 원인 제거. ⑤ 데드 파일(`FluentIconProvider`·`NativeEditor`)·진단코드 제거. 버전 범프·CHANGELOG·PublicAPI Shipped 승격. 테스트 169→184. (실수: `git add -A`로 사용자 WinUI3 WIP가 커밋에 딸려가 force-push로 히스토리에서 제거 — `git add -A` 주의.) / (26차) — **🚀 NuGet 게시: `AvaloniaRichEditor 0.3.0-alpha`** (Trusted Publishing OIDC, GitHub Release 작성). 25차 작업분(툴바 벡터 아이콘·줄바꿈, `RichEditorView.ZoomFactor`, `RichEditorToolbar.LeadingItems`/`TrailingItems`, RichEditorView 상단 정렬+페이지 뷰 가로 스크롤) + 이슈 #1 수정 포함. 버전 범프·PublicAPI Shipped 승격(374건)·CHANGELOG. 시행착오: 회귀 테스트가 `new Window().Show()/Close()`로 헤드리스 플랫폼을 띄웠다 닫아, **macOS CI에서 이후 테스트의 `RichEditor` 정적 cctor가 `ICursorFactory` 못 찾아 무더기 실패→Pack 차단**. 윈도우 없이 직접 Measure/Arrange로 환원해 재태그→게시 성공. (교훈: 헤드리스 테스트에서 top-level Window 생성·종료 금지.) / (25차) — **🎨 툴바 개선 + View 줌 + 데모 정리**: ① `RichEditorToolbar` 내장 벡터 Path 아이콘(의존성 0, `ToolbarIcons` — 서식롤러·들여쓰기·표·이미지·구분선·실행취소/재실행·형광펜; B/I/U/S·색상A는 만국공통 글자 유지). ② **창이 좁으면 툴바 줄바꿈(WrapPanel)** — 종전 가로 스크롤바 대체. (당초 `»` 오버플로우 드롭다운을 시도했으나, OS 인터랙티브 드래그-리사이즈 모달 루프에서 디스패처 재진입 중 리페어런트가 레이아웃 패스에 끼어들어 크래시 — 프로그램적 리사이즈로는 재현 불가. 트리 변경·플라이아웃·디스패처가 전혀 없는 WrapPanel로 전환해 근본 차단.) ③ **`RichEditorView.ZoomFactor`** 공개 API(에디터를 `LayoutTransformControl`로 감싸 문서만 스케일, 툴바 무영향, 0.2~5.0 클램프). ④ 데모를 손조립(①+②+A4액자)에서 **`RichEditorView`(③) 호스팅**으로 전환 — 줌/페이지 전 기능을 `view.Editor`/`ZoomFactor`/`PageView`로 재배선, FluentIcons provider 제거(내장 아이콘이 기본값). ⑤ **`RichEditorToolbar.LeadingItems`/`TrailingItems`** 공개 API — 호스트가 앱 셸 버튼(저장/열기/인쇄·줌·페이지)을 서식 툴바와 **같은 단일 strip**에 넣어 함께 줄바꿈. 데모는 이를 써서 **창에 view만** 두고 기능+서식을 하나의 툴바로 통합(줌 +/- 제거, 콤보만). (테스트 윈도우 누수로 헤드리스 세션이 오염돼 후속 LocalizationTests가 깨지던 위생 버그도 `win.Close()`로 수정.) 테스트 164→169. / (24차) — **🐛 이슈 #1 수정: 색상 텍스트 비동기 저장 크래시**: `ToJsonAsync`/`SavePackageAsync`가 스레드풀에서 DTO를 빌드하며 mutable `SolidColorBrush.Color`(thread-affine StyledProperty)를 읽어 "calling thread cannot access this object" 예외. 권장안 #1 적용 — DTO 빌드(브러시 읽기)는 UI 스레드 동기로, 백그라운드엔 순수 데이터만(`DocumentSerializer.BuildDto`+`SerializeDto`, `DocumentPackage.WriteDto`로 분리). 완성된 DTO가 값 스냅샷이라 기존 `Clone()` 스냅샷도 제거(역설적으로 더 가벼움). 회귀 테스트 1건(`ToJsonAsync_ColoredText_DoesNotThrowOffThread`), 총 164. / (23차) — **🚀 NuGet 첫 게시: `AvaloniaRichEditor 0.2.0-alpha`** (nupkg+snupkg, Trusted Publishing OIDC). GitHub Release 작성. 시행착오: NuGet/login user=nuget 계정명(kanu) vs 정책 Repository owner=GitHub 소유자(centwon) — 교차 입력 시 401. 다음 단계: 외부 피드백 수집 → 0.x 반복(인라인 표/벡터 PDF 결정) → API 동결 → 1.0. / (22차) — **NuGet `0.2.0-alpha` 게시 준비 완료(사용자 결정: 0.2.0)**: 게시 전 전수 검증(Release 빌드 경고 0·163테스트·왕복 하네스·pack 내용물·데모 AOT 28.9MB) 통과. 버전 범프 + PackageIcon 생성 + CHANGELOG 0.2.0-alpha 절 + PublicAPI Shipped 승격(368건) + ci.yml Trusted Publishing(OIDC) 스텝. 잔여=사용자 nuget.org 정책 등록 → 태그 푸시. / (21차) — **P-마일스톤 후속 2건 완료(사용자 검증)**: 머리말/꼬리말/쪽번호(`PageHeader`/`PageFooter`/`ShowPageNumbers`, 여백 띠 전용이라 분할 무영향) + 표 행 경계 페이지 분할(원자=행, 렌더 무수정). CHANGELOG에 P-마일스톤 절 추가, 푸시·CI 3-OS 그린. 테스트 163. / (20차) — **P-마일스톤 Phase 4 출력 완료 → 마일스톤 전체(Phase 0~4) 종결**: `SavePdf`(자체 래스터 PDF 라이터, 무의존) + 데모 프린터 인쇄(System.Drawing, 데모 전용)/PDF 저장. 벡터 PDF(글자 선택)는 보류 — SkiaSharp 경로·의존성 원칙 선결 결정과 함께 백로그 기록. 테스트 161 그린. / (19차) — **P-마일스톤 Phase 3 페이지 렌더+미리보기 완료(사용자 검증)**: `GetPrintPageCount`/`RenderPrintPage(dpi)` 공개 API, chrome-free 렌더(선택/캐럿/핸들 제외), 데모 인쇄 미리보기 창. 테스트 160 그린. / (18차) — **P-마일스톤 Phase 2 편집 뷰 페이지 모드 완료(사용자 검증)**: `PageView` 속성 + 갭 주입 렌더(페이지별 클립+변환 리플레이, 걷기 로직 무변경) + doc↔view 매핑 choke point(포인터 1회 매핑·IME/BringIntoView 역매핑) + `ContentLayoutWidth` 워커 폭 통일. 줄 반토막 버그(클립이 슬라이스 끝을 넘음) + 표 여백 렌더 하드코딩 누락 수정. 데모 "페이지" 토글. 테스트 158 그린. NuGet 게시 재개 조건을 Trusted Publishing(OIDC)으로 갱신. / (17차) — **델타 Undo 기각 + 🖨️ P-마일스톤 착수**: ① `--bench`에 순수 `Document.Clone()` 단독 측정 추가 → 100장에서도 0.28ms(종전 "첫 키 162ms=클론" 귀속은 JIT 웜업 오측정) — 델타 Undo를 실측으로 기각, 2.0+ 후보에서 제거. ② A4 페이지/인쇄 요구 확정(사용자 결정: 편집 뷰도 워드식 페이지, 출력=프린터+PDF) → "A4 페이지 레이아웃"·"정밀 인쇄" 보류 항목을 🖨️ P-마일스톤으로 승격(설계: 리플로우 아닌 **갭 주입 y-리매핑**, 페이지네이터 1개를 편집 뷰·미리보기·프린터·PDF 4용도 공유). Phase 0(비율 줌 "잘림") 실앱 검증 → 버그 아님(페이지 구분 부재=Phase 2 그 자체). **Phase 1 페이지네이터 코어 완료**: `ComputePageBreaks`(줄 경계 분할, measure 워크 미러, 디코드 프리), 테스트 148→154. / (16차) — **전수 점검 + 문서 형식 명세서**: 코드 리뷰로 버그 10건(P1 4건: SplitRunAtOffset 서식 소실·async void 크래시·히트테스트 2000px·서식지우기 누락)·성능 3건·기능 후보 5건을 백로그 "🔍 2026-06-12 전수 점검 백로그"로 정리(파일:줄 명시, 미착수). `docs/DOCUMENT_FORMAT.md` 신설(JSON 스키마 v2+`.ardx` 명세, README 링크). / (15차) — **블록 여백 제어 완료**: 위/아래=`Block` 승격(이미지·표·구분선 포함), 왼쪽=`Indent` 재사용, 오른쪽=`Paragraph.MarginRight`(문단 전용 — 어울림 없음). 워커 7곳+줄폭 7곳 일괄, JSON 레거시 호환(nullable), 우클릭 여백 서브메뉴. 테스트 130→133. / (14차) — **블록 캐럿 버그 해소**: 묵은 "↓ 표 뒤 진입 안 됨"의 실체 = 렌더 위치(앞/뒤 캐럿 모두 왼쪽 모서리) + → 비대칭(`AdjacentBlock`이 셀에서 null). 표 뒤 캐럿 오른쪽 아래 렌더, 내비게이션 규칙 확정 — ←/→=셀 통과(표 앞↔첫 셀…마지막 셀↔표 뒤), ↑/↓=표를 한 단위로 건너뜀(셀 진입은 →·Tab·클릭). 회귀 테스트 10건(`BlockCaretTests`), 총 130건. 사용자 검증 완료. / (13차) — **N5 God-class 분해 완료**: 본체 3,595→1,273줄, 신규 partial 7개(FindReplace/Tables/Images/DocumentApi/Formatting/HitTesting/Input), 전 단계 동작 불변+테스트 그린. 인라인 표 마일스톤의 선행 조건 해소. / (12차) — **아이콘 커스터마이즈 훅**: `RichEditorIcons.Provider` + `RichEditorIcon` 41슬롯(툴바·컨텍스트 메뉴), null=내장 글리프 폴백. 라이브러리 의존성 0 유지, 데모만 FluentIcons.Avalonia로 교체 시연. 테스트 118→120. / (11차) — **N6-6 소프트 제한 완료**: 지표=이미지 개수(타이핑은 병목 아님), `MaxRecommendedImages`(기본 50, 0=비활성)+`RecommendedImageLimitExceeded`(에지 트리거)+`GetImageCount()`. 모드별 자동 기본값 대신 단일 기본+문서 안내 채택. 데모 상태바 경고 라벨. 테스트 113→118. → **N6 전체 완료.** / (10차) — **N6-5 Draw 컬링 완료**: 뷰포트 밖 블록 draw 생략(보수적 — 레이아웃/캐럿/히트테스트 불변), 캐럿·선택 블록 예외, 리스트 번호 연속성, 오프스크린 이미지 지연 디코드 회피, ScrollChanged 재그리기 계약. 전/후 실측: 100장 스크롤 29→51fps, Render 4.0→2.2ms. 테스트 74개 통과. / 같은 날 버그픽스: 붙여넣기 후 화면이 캐럿(붙여넣은 내용 끝)을 따라가지 않던 문제 — `PasteFromClipboardAsync` 5개 분기에 `ResetCaretBlink()` 추가(키보드 경로와 관례 통일, 사용자 검증 완료). / (9차) — **N6-6 실측 완료**: 데모 `--bench` 하네스 신설(실창+Skia, 10/20/50/100장 자동 측정, `bench-results.txt`). 결과: 타이핑 sub-ms(전 구간), 스크롤 50장까지 50fps+ / **100장 29fps 드랍**(병목=래스터화, 관리 Render 4ms), 첫 키 undo 클론 100장 162ms, JSON 100장 104MB. → **N6-5 착수 기준 충족**(보수적 Draw-only 컬링 권고), N6-6 임계값 결정 근거 확보(Full 권장 ~50장), N6-7 `.ardx` 정량 근거 확보. / (8차) — **alpha 잔여 빚 정리(3건)**: ① CI 액션 Node 24 대응(`checkout@v6`/`setup-dotnet@v5`/`upload-artifact@v7`), ② README 예제를 공개 API(`PublicAPI.Unshipped.txt`)와 대조 검증, ③ `THIRD-PARTY-NOTICES.md` 추가(Avalonia·HtmlAgilityPack MIT 고지) + 패키지 동봉(pack 검증: nupkg에 포함 확인). / 2026년 6월 10일 (7차) — **N6-3 완료**: `ToJsonAsync`/`LoadJsonAsync`(백그라운드 직렬화, 스냅샷 의미론, 데모 전환, 테스트 72→74). **1.0 성능 항목 전부 완료** — 1.0 잔여는 문서화·API(XML 주석 완성, PublicApiAnalyzers)뿐. / 같은 날 UX: 이미지 컨텍스트 메뉴 1/2·1/3·1/4 크기 프리셋. / (6차) — **N6-2 완료**: `byte[]` 중심 이미지 모델(RawBytes+MimeType, 지연 Bitmap 캐시, 인제스천 6경로 바이트 캡처, 직렬화/HTML 재인코딩 제거, Clone 참조 공유, `InsertImageBytes` 공개 API). 테스트 64→72, 왕복 하네스 리포트 이전과 완전 동일(회귀 0). 1.0 성능 항목 잔여는 N6-3(직렬화 비동기화). / (5차) — **테스트 갭 마저 처리**: 키 입력 파이프라인 11건(라우티드 이벤트로 OnKeyDown/OnTextInput 실구동 — Backspace/Delete 문단 병합, Enter 분할·제목 리셋·리스트 탈출, Undo 코얼레싱, ReadOnly) + 붙여넣기 구성요소 6건(CF_HTML 추출 — `ExtractHtmlFragment` internal 승격, `InsertHtml` 인라인 병합/블록 삽입 계약). 총 47→64. **1.0 안정성 "테스트 커버리지 확대" 완료 — N6-2 착수 조건 충족.** / (4차) — **테스트 보강(N6-2 안전망)**: `TextRangeOffsetTests.cs` 10건 추가(인라인 이미지 오프셋 모델·멀티문단 삭제/스타일·표 횡단·부분 서식 분할). 총 37→47 통과. 남은 갭은 `RichEditor` private 편집 경로·붙여넣기 폴백(클립보드 페이크 필요)으로 기록. / (3차) — **N1 마무리 + N6-1 + CHANGELOG**: SourceLink/RepositoryUrl(SDK in-box, nuspec에 repo+branch+commit 확인), N6-1 JSON 스키마 버전 필드(레거시 폴백, 테스트 35→37), `CHANGELOG.md` 시작, `v0.1.0-alpha` 태그 → CI `Pack` 잡 그린(아티팩트 생성). **결정: `PackageIcon`+nuget.org 게시는 API 안정화/키 발급 시점까지 함께 보류**(게시 비가역성·아이콘 의미 시점 일치). 현재 `0.1.0-alpha`는 자기완결적 마일스톤. / (2차) — **최우선 항목 실행**: GitHub 저장소 `centwon/AvaloniaRichEditor`(Private) 생성·푸시. 초기 커밋의 ~240MB 빌드 산출물(`bin`/`obj`)을 `filter-branch`로 히스토리에서 제거. **CI 3-OS 매트릭스 첫 실행 그린**(windows/ubuntu/macos) → N3 mac/Linux 스모크 + N4 CI 그린 동시 해소. 남은 차단: Public 전환 → SourceLink/nuget 게시. / 같은 날 (1차) — **로드맵 적정성 점검 반영(4건)**: ① GitHub 저장소 푸시를 "🚨 최우선" 독립 항목으로 승격(단일 차단점 — N1/N3/N4 잔여+alpha 체크리스트 전체가 이것에 막힘, CI는 미실행 상태). ② N6-1(JSON 스키마 버전)을 1.0 → `0.1.0-alpha` 체크리스트로 이동(alpha에서 JSON 저장이 시작되면 스키마 사실상 동결). ③ N6-2 착수 조건으로 테스트 보강 선행 명시(1.0 체크리스트도 안정성→성능 순서로 재배열). ④ N3.6 툴바는 0.1.0 미포함·0.2.0 확정, `PublicApiAnalyzers`를 0.2.0 진입 조건으로. / 이전: 2026년 6월 9일 — **N3.6 추가**: 라이브러리 툴바 승격 + 모드 연동(3계층 `RichEditor`/`RichEditorToolbar`/`RichEditorView`, `Target` 연결, 모드/플래그→버튼 가시성, 선택상태 반영·스크롤러 주의점). N3.5 모드 표에 "툴바" 열 추가(의도, 미구현). / **N6-7 추가**: `.ardx` 패키지 파일 포맷(ZIP 컨테이너, JSON 문자열 계약 유지 + 파일 저장 API 추가, 이미지 무압축 Stored, N6-2 의존). / **N3.5 에디터 모드 완료**: `EditorMode` 프리셋(ReadOnly/Basic/Full)+기능 플래그 4종(`AllowImages`/`AllowTables`/`AllowRichPaste`/`AllowFindReplace`), 붙여넣기·드롭·삽입명령·컨텍스트메뉴·찾기바꾸기 가드, ReadOnly 최적화(undo/IME/캐럿타이머 비활성). 테스트 27→35건. / 이전: 2026년 6월 8일 — **N6 성능 최적화 로드맵 추가**: 이미지 저장 모델 전환(`Bitmap`→`byte[]` 중심, 원본 바이트 보존, 지연 Bitmap 캐시), JSON 스키마 버전, 직렬화 비동기화, 이미지 중복 제거(해시), 렌더링 가상화. 백로그에 사용성 후보(블록 여백·DOCX 파싱·마크다운) 및 구조 기반(테스트 보강·크로스플랫폼 실검증) 정리. 외부 의존성(SkiaSharp/ImageSharp) 추가 없이 Avalonia 내장만으로 진행 결정. / 이전: N0~N5 + Phase 1~6 완료 상태
