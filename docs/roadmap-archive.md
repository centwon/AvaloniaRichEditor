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
> - [x] **중첩 경계 Tab(2026-06-19, GUI 검증 완료)**: `AllCellsInOrder`(문서 순서 앵커 셀, 셀 뒤에 그 안 중첩 표 셀이 오도록 재귀)로 Tab/Shift+Tab가 전 구조를 순회 — host 셀에서 Tab=중첩 표 진입, 중첩 마지막 셀 Tab=바깥 다음 셀로 탈출, 중첩 첫 셀 Shift+Tab=host로 복귀. 문서 마지막 셀 Tab=최상위 표(부모 사슬 위로)만 행 추가(중첩 표는 Tab으로 행 추가 안 함 — 우클릭). 테스트 +3 → 289.
> - [x] **P4-3 메뉴 다듬기(2026-06-19, GUI 검증 완료)**: ① 셀 안 블록 이미지 우클릭 → 이미지 메뉴(`CellImageAtPoint`이 `_cellImageRects`로 감지, top-level과 동일 — 복사/삭제/크기/교체/저장/여백). 블록↔인라인 토글은 셀 이미지에서 비활성(`img.Parent is FlowDocument`만). ② 중첩 표 셀 우클릭의 "표" 서브메뉴가 `FindCell`로 찾은 **가장 안쪽 표**를 겨냥(이전엔 `GetBlockAtPoint`의 top-level 표 — 버그). 289 그린. **마일스톤 A 완전 종료.**
>
> **위험**: 새 아키텍처 없음(캐럿·표=블록 유지, 기존 워크 일반화). 폭은 넓음(13파일+Parent 스윕, P1에서 가장 조심). 헤드리스 약점으로 P3·P4는 RenderTargetBitmap 강제 패스+GUI 검증.

### 🟢 [완료] 마일스톤 B: 인라인 표 (HWP식 "글자처럼 취급") (착수·완료 2026-06-20)
> **완료 요약**: P0~P5 전부 + 표 그리기(드래그 크기) + 클립보드/HTML 리치 입출력(셀 내부 중첩 표·이미지·열폭 왕복). 인라인 표 생성·편집·네비·저장/불러오기 완전 동작, GUI 사용자 검증 완료. 테스트 289→323. 상세는 아래 단계별.
> **목표**: 표를 문단 줄 안에 1글자로 배치(HWP식). A의 재귀 프리미티브(`DrawCellBlockList`/`HitTestBlockList`/`LayoutTable`↔`MeasureCellContentHeight`)를 재사용한다. **확정 결정(사용자 2026-06-20)**: ① v1 범위 = **완전 편집**(셀 편집·Tab·열/행 리사이즈). ② 추상화 = **"제3안"** — 선택지1(표 전용 라우팅, `InlineImage` 불변)으로 구현하되 `InlineTable`의 계약 메서드를 *나중에 베이스로 승격 가능한 모양*으로 작성. **지금 `InlineObject` 공통 베이스는 추출 안 함**(세 번째 인라인 타입이 실제로 생길 때 실증 기반 추출). 근거: 공유 표면(U+FFFC 1글자 계약)은 이미 `LayoutSeg`/`BuildPlain`/오프셋 카운팅으로 추출돼 있고, 하드한 부분(이미지=원자적 vs 표=하강 라우팅)은 공유 안 됨.
>
> **핵심 난점**: 인라인 표는 이미지와 달리 내부 상호작용(셀 편집·캐럿 하강·히트테스트)이 있어 원자적 `DrawableTextRun`으로 안 끝남 → 신규 라우팅 계층(히트테스트·캐럿 하강)이 P3의 위험 집중. 불변식 1·2·4 재작업.
>
> **단계(각 출하·테스트 그린)**:
> - [x] **P0 안전망(2026-06-20)**: B가 건드릴 주변 불변식(오프셋=`TextRangeOffsetTests`, 블록 표/셀=`TableCellBehaviorTests`)은 기존 289 스위트에 이미 존재 → 새 P0 테스트 없이 **289+6 그린 베이스라인 확정**(중복 테스트 회피). 진짜 신규 위험(하강 라우팅)은 기능 존재 전 테스트 불가 → P3에서 네트.
> - [x] **P1 모델+오프셋(2026-06-20)**: `InlineTable : Inline`(`TableBlock` 래핑, `Clone`=딥카피) 신설. **오프셋 모델 일반화** — "이미지=1글자"를 "**비-Run 인라인=원자 객체 1글자(U+FFFC)**"로(`InlineLen`/`BuildPlain`/`GetStatus` 워크 in RichEditor.cs, `InlineLen`/`GetParagraphText`/`GetParagraphInlines` in TextRange.cs). `:0` 폴백 잠복버그 동시 제거. `GetRichInlines`가 인라인 표도 캡처(in-app 복사 왕복). PublicAPI 4건 등재, 라이브러리 0경고. 동작 보존(비-Run·비-Image 인라인은 현재 없음)이라 289 그린 유지, 모델 테스트 5건(`InlineTableTests`: 오프셋/삭제/캡처/클론) → **294 그린.** 렌더(P2)·직렬화(P5)·내비/Parent(P3)는 후속.
> - [x] **P2 렌더(2026-06-20)**: `TableTextRun : DrawableTextRun`(Size=표 크기, Baseline=높이) 신설 — `LayoutSeg`에 표 박스+그리기 클로저 필드 추가, `BuildTextLayout`이 `LayoutTable(table,0,0)`로 측정 후 세그 생성, `ParagraphTextSource.GetTextRun`이 표 세그에 `TableTextRun` 반환. 그리기는 `DrawNestedTable`(문서 좌표, **chrome:false** — 캐럿/선택/핸들은 P3/P4)로 위임. 측정은 동일 `TextLayout`이라 문단 높이에 표 높이가 자동 반영(별도 measure 코드 0). 헤드리스 측정 테스트 1건(인라인 표가 문단 줄 높이를 키움) → **295 그린, 0경고.** **GUI 픽셀 검증은 삽입 UI(P4) 후 함께**(현재 인라인 표를 만들 경로가 없어 단독 육안검증 불가 — 마일스톤 A와 동일 패턴).
> - [~] **P3 히트테스트·캐럿 하강(핵심, 진행 중)**: [x] **P3a 하강+렌더(2026-06-20)** — ① 히트테스트: 포인트가 인라인 표 박스(베이스라인 하단 정렬, `RegisterInlineImages`와 동일 기하)에 닿으면 `InlineTableHitDescent`가 `HitTestBlockList`로 셀까지 하강(클릭→셀 캐럿). ② 렌더: `TableTextRun.Draw`를 **위치 기록기**로 전환(`_inlineTableDraws`), 문단 `layout.Draw` 직후 `FlushInlineTableDraws`가 `DrawNestedTable`(full chrome, 캐럿/선택 ref 전달)로 그려 셀 안 캐럿·선택·인쇄 모두 처리(top-level 문단+셀 문단 2경로). ③ 캐시 무효화: `ParagraphSig`가 인라인 표 차원+셀 문단 재귀 sig 반영 → 셀 편집/리사이즈가 호스트 문단 레이아웃 무효화. ④ 열거: `ParagraphsInBlocks`가 문단 인라인 안 표까지 재귀(선택/Find/SelectAll 도달). 동작 보존(인라인 표 없는 문서 불변)이라 295 그린 유지, 테스트 3건(열거·캐시 무효화·클릭 하강) → **298 그린, 0경고.** [x] **P3b 키보드 네비(2026-06-20)**: ① `WireBlockParents`가 인라인 표 Parent 사슬 와이어링(cellPara→TableCell→TableBlock→**InlineTable**→host). ② `MoveCaretRight/Left` 진입/탈출 — →가 호스트의 표 ObjChar 앞에서 첫 셀로 하강, 마지막 셀 끝에서 호스트 ObjChar+1로 탈출(중간 셀은 GetNextParagraph 셀 순회), ←는 대칭(`ImmediateInlineTable`/`InlineTableStartingAt`/`InlineTableEndingAt`/`InlineTableContainsPara` 헬퍼). 블록캐럿 분기는 `Parent is FlowDocument`라 인라인 표 자동 제외. ③ Tab — `FindCellIn`/`CollectCells`(AllCellsInOrder)가 문단 인라인 안 표까지 재귀(Tab/Shift+Tab·병합·메뉴가 인라인 표 셀 인식). ④ ↑/↓ — 블록캐럿 분기 top-level 한정이라 인라인 표는 기하 폴백(`GetPositionFromPoint` 하강)으로 안전 통과, 다중문단 셀 ↑/↓도 `Parent is TableCell`로 적용. ⑤ `TextRange.GetAllParagraphsInOrder` 재귀화(중첩 표+인라인 표) — 컨트롤 `ParagraphsInBlocks`와 순서 일치(선택/삭제/서식 정합). 동작 보존이라 298→**304 그린, 0경고**. 테스트 6건(→/←진입·탈출·셀순회·Tab). **P3 완료.**
> - [~] **P4 완전 편집(진행 중 2026-06-20)**: [x] **삽입/토글/삭제** — 공개 `InsertInlineTable(rows,cols)`(캐럿에 1글자로 삽입), HWP식 토글 `ConvertTableBlockToInline`↔`ConvertInlineTableToBlock`(이미지 토글 대칭, top-level 한정), 표 우클릭 메뉴에 "글자처럼 취급" 체크박스(블록표=미체크→인라인화, 인라인표=체크→블록 승격). 삭제 2버그 수정: `DeleteLocalText`가 `InlineImage`만 지우던 것을 **비-Run 인라인 일반화**(Backspace/Delete로 인라인 표 삭제), 메뉴 "표 삭제"가 인라인 표는 `DeleteInlineTable`(호스트에서 제거 — `RemoveBlockAnywhere`는 블록 리스트만). PublicAPI 1건. 테스트 4건(삽입·양방향 토글·Backspace 삭제) → **308 그린, 0경고.** [x] **열/행 리사이즈·셀 우클릭 메뉴** — P3a `FlushInlineTableDraws`(chrome=true)가 `DrawNestedTable`로 그려 리사이즈 핸들이 문서 좌표로 자동 등록(top-level 핸들러 공유), 셀 우클릭은 `FindCell` 재귀로 인라인 표 셀 인식 → 무료. [x] **StackOverflow 수정(GUI 1차)** — `FlushInlineTableDraws`가 `foreach` 도중 재진입(DrawNestedTable→DrawCellBlockList→flush)해 안 비운 같은 리스트를 무한 재귀 → **스냅샷 후 즉시 Clear**로 수정. 렌더 회귀 테스트 1건(309). [x] **표 앞 글자 입력 버그 수정(GUI 2차)** — `TryInsertTextCore`가 `InlineImage` 앞에서만 새 Run을 끼우던 것을 **비-Run 인라인 일반화** → 문단 맨 앞 인라인 표 앞에 타이핑 시 끝에 붙던 것을 앞에 삽입. 테스트 2건(앞/뒤 입력) → 311. [x] **표-글자 간격/여백(#1·#2, 사용자 요청)** — 인라인 표 사방에 `InlineTablePad`(2px) 숨 트기. geometry 3곳(`BuildTextLayout` 런 Size+그리기 inset, 히트테스트 하강 `docX/docY`)을 공유 상수로 일관 유지(불변식 1). 가로=글자가 테두리에 안 닿음, 세로=인접 줄과 약간의 간격. 313 그린.
> - [x] **표 그리기(드래그 크기 지정, 사용자 요청)** — 그리드 피커에서 NxM 선택 시 즉시 삽입 대신 **"표 그리기 모드"** 무장(`BeginTableDraw`, 십자 커서) → 문서 위 드래그하면 view-space 고무줄 사각형(대시 테두리) → 놓으면 그 크기의 NxM 블록 표 삽입(`InsertTableDrawn`: 열폭=폭/열수, 행높이=높이/행수). 드래그 없이 클릭=기본 크기 폴백, Esc/우클릭=취소. 포인터 3핸들러 + Render 오버레이 + **컨텍스트 메뉴·툴바 그리드 피커 둘 다** 배선(`RichEditorToolbar` 표 버튼도 `BeginTableDraw`). 테스트 2건(무장·크기 계산) → **316 그린, 0경고.** GUI 검증 완료(사용자). **고무줄 클립 수정**: 데모 에디터가 콘텐츠 높이에 맞춰 Top 정렬(`ShowPageBoundaries=false`)이라 짧은 문서에서 빈 공간으로 드래그하면 미리보기가 에디터 경계 밖이라 잘리고(일부만), 놓을 땐 원시 좌표로 전체 크기 생성 → 불일치. 드래그 좌표를 `ClampToEditorBounds`로 묶어 미리보기==결과 보장. **단 클램프가 콘텐츠 높이로 묶여 큰 표를 못 그리던 후속 문제** → `RichEditorView`가 **에디터 MinHeight=뷰포트 높이/줌**(`UpdateEditorFillHeight`, 뷰포트 변경 추적)로 빈 공간도 편집 표면화 → 클램프가 뷰포트까지 허용(온전한 미리보기 + 큰 표 + click-to-end 부수효과).
> - [x] **GUI 4차(사용자 관찰)** — ① **표 뒤 캐럿 붕괴 버그**: 표 뒤에 글자가 없을 때 캐럿이 표 *앞*(좌측 모서리)으로 가던 것(Avalonia가 줄 끝 DrawableTextRun을 캐럿 거리에서 제외하는 결함, 인라인 이미지와 동일) → `FixCaretAfterTrailingImage`를 인라인 표까지 일반화(런 폭은 레이아웃 `HitTestTextRange` 기하에서). 테스트 1건. ② **베이스라인 안정감**: `InlineTableDrop`(2px) — 표 바닥이 글자 baseline보다 2px 아래 안착(런 `Baseline` 조정, 히트테스트 공식은 런 전체 높이 기준이라 자동 추적). 314 그린.
> - [x] **GUI 3차 버그 3건 수정** — ① **캐럿이 줄 위로**: `CaretYInLine`이 `InlineImage`에만 하단정렬하던 것을 **비-Run 일반화**(인라인 표 줄도 글자 베이스라인에 캐럿). ② **셀 순회 루프(호스트 끝→첫 셀, 마지막 셀→첫 셀)**: 인라인 표 셀이 선형 nav 순서(`ParagraphsInOrder`)에 있어 `GetNextParagraph(host)=첫 셀`이던 근본 원인 → **nav 열거기 `ParagraphsInBlocksNav`(인라인 표 미하강) 신설**, 셀 간 이동·진입·탈출을 `MoveCaretRight/Left`에서 `ParasInInlineTable`로 명시 처리(선택/Find용 `ParagraphsInBlocks`는 하강 유지). 테스트 3건(하단정렬·호스트끝→다음문단·기존 진입/탈출) → **313 그린.** [ ] **GUI 육안검증 재실행**.
> - [~] **P5 직렬화·정리(진행 중 2026-06-20)**: [x] **JSON/.flow** — `InlineDto.Table`(중첩 BlockDto) 추가, 직렬화/역직렬화가 A의 재귀 `BlockToDto`/`DtoToBlock` 재사용 → 인라인 표(+중첩 표·다중블록 셀·스팬·둘러싼 텍스트) 무손실 왕복. **인라인 표 영속성 확보(저장 차단 해소).** `/// <inheritdoc/>` 고립 수정(0경고). 테스트 2건(왕복·중첩) → **318 그린.** [x] **HTML 내보내기 + 클립보드 리치(#3, 2026-06-20)** — `EmitInline`이 인라인 표를 버리던 것을 `<table>`로 내보냄(표 emit을 `EmitTable` 헬퍼로 추출, 블록/인라인/중첩 공용). **셀 안 블록이미지·중첩 표도 내보내** "known gap" 해소(최대한 내용 보존). `CloneParagraphRange`(복사 HTML 경로)가 인라인 이미지만 복제하던 것을 **비-Run 일반화**(인라인 표도 복사에 포함). 복사 시 CF_HTML로 Word/HWP에 인라인 표 전달(HTML엔 인라인 개념 없어 블록 `<table>`로 붙음=베스트에포트). 테스트 1건 → **319 그린, 0경고.** **참고**: 블록 표·이미지·인라인 이미지는 이미 CF_HTML로 보존(로드맵 기존). RTF 내보내기는 대형 별건(보류). **HTML 임포트 버그 수정(사용자 발견)**: `RichEditorView.ImportAsync`가 ZIP(.flow)/RTF/JSON만 판별하고 **HTML 분기 누락** → `.htm` 불러오기가 JSON 파싱 실패→빈 문서였음. UTF-8 트림이 `<`로 시작하면 `LoadHtml`로 라우팅(RTF는 위에서 처리, JSON은 `{` 시작). ToHtml→ParseHtml 왕복 테스트 1건 → 320 그린. **복사→Word/한글 붙여넣기: 표+셀 이미지 보존 확인(사용자 GUI).** **셀 안 중첩 표 HTML 왕복 버그(사용자 발견)**: `ParseTable`이 셀을 `ParseInlines`(인라인만)로 파싱해 **셀 안 중첩 표·블록 이미지·다중 문단을 import 시 드롭**(export는 emit하는데 import가 버림 → 브라우저서 빈 표) → `WalkBlocks(td, …)`로 셀을 **블록 파싱**(중첩 표/블록 이미지/문단 복원, export와 대칭). 왕복 테스트 1건 → 322 그린. (셀 블록 이미지 디코드는 헤드리스 no-op라 export 테스트로 별도 검증.) **열폭 보존(사용자 요청)**: HTML이 열별 픽셀 폭을 안 실어 왕복 시 표가 찌그러지던 것 → 내보내기 `<colgroup><col style="width:Npx">`, 불러오기 `ParseTable`이 그 값을 `ColumnWidths`로 복원(중첩 표 col 제외). 왕복 테스트 1건 → **323 그린, 0경고.** **마일스톤 B + 클립보드/HTML 리치 입출력 완료.**

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

---

## 🚧 라운드2/3 (0.9.0 이후, 미릴리스) — 2026-07 작업 로그

> `Project_Roadmap.md`에서 이관(2026-07-31). 이 절은 당시 기록 그대로이며, 요약은 로드맵에 남아 있다.

🚧 **라운드2 (진행 중, 미릴리스)** — WinUI 포트가 0.9.0 이후 앞서간 분량. 상세는 [`CHANGELOG.md`](CHANGELOG.md) Unreleased.
- 기능 5종: `IsModified`/`MarkSaved`, `RemoveList`, `AutoLinkOnType`, `AllowRemoteImagesOnPaste`, 찾기 highlight-all
- **전 소스 정독 감사에서 찾은 결함 5건 수정**(단축키 충돌·문단 서식 유실·셀 병합 데이터 손실·문서순서 비교·찾기 하이라이트가 선택색 덮음), 테스트 9건 추가
- **2b(WinUI 버그 백포트 중 Avalonia 적용 확인분 3건)**: 셀 안 목록 마커 미렌더(→ `CellParaLeft`를 렌더·히트테스트·링크 히트테스트·캐럿·측정 **다섯 walk에 동일 적용**, 규칙 #1), 리사이즈 핸들 클릭만으로 `IsModified` 뒤집힘(→ 첫 실제 이동 때 스냅샷), `FindCell` O(문서) 스캔 → 부모 체인, 셀 안 다중 문단 선택 시 목록 명령이 첫 문단에만 적용(→ 선택 수집을 컨테이너 무관하게). 테스트 4건(+픽셀 1건)
- **2b 잔여분 전수 대조 완료**: WinUI 나머지 10건을 Avalonia 코드와 1:1 확인 → **추가 수정 4건**(HTML `font-weight` 오탐, RTF 표 안 그림이 셀 탈출, 인라인 표 셀 미정규화(규칙 #5), `LoadHtml`/`InsertHtml`의 `AllowRemoteImagesOnPaste` 미전달). **해당 없음 5건**: 셀 다중문단 붙여넣기(이미 컨테이너 일반화), `SplitByNewlines` 서식 소실(라운드1에서 수정), undo 바이트 예산(Avalonia엔 예산 자체가 없음·50개 제한), Shift+Enter 캐럿(실측 정상), 표 행 높이 stale(`_trustLayoutCache`가 편집 후 자동 무효화). Win2D 누수 1건은 플랫폼 전용.
- **표 안 Ctrl+A 단계 선택 추가**(HWP/Excel식: 셀 내용 → 표 전체 → (중첩이면 바깥 표) → 문서. 단계는 현재 선택에서 역산 — 클릭/화살표가 끼면 자동 리셋)
- **라운드3 전수 조사(2026-07-29)**: 소스 전체를 다시 정독. **G1이 통합한 것은 *기하* 워커뿐이고, *문단 순회* 워커에는 같은
  드리프트 버그 클래스가 남아 있음**을 확인 — 재귀로 고쳐진 것(`TextPointer.CompareTo`, `TextRange.CollectParagraphs`,
  `ParagraphsInBlocks`, `FindCell`)과 1레벨에 멈춘 것이 섞여 있다. 결함 6건 전부 테스트로 재현 확인.
- **🟢 G2 — 문단 순회 워커 통합 (2026-07-29, 완료)**: 위 조사에서 나온 1레벨 워커를 전부 재귀화. 테스트 11건 추가(355→366).
  1. `UndoManager`의 전역 인덱스 왕복 → **깊은 위치 편집 후 undo가 캐럿을 문서 맨 앞으로 날리던** 문제(셀 2번째 문단·중첩 표·인라인 표)
  2. `TextRange.Delete`의 병합 판정을 "최상위 블록인가" → **"같은 블록 리스트의 형제인가"**로 교체 → 같은 셀 안 다중 문단 삭제가 병합됨
     (셀을 *가로지르는* 선택은 기존대로 격자 구조 보존)
  3. `TopLevelBlockOf`/`FindTopLevelBlock` 중복 워커 2개 → **부모 체인 워커 1개**(`TextRange.TopLevelBlockOf`, `CoalesceRuns`와 같은 공유 방식)
  4. `GetPlainText()`(접근성 피어가 사용)·`GetImageCount()`·`PruneLayoutCaches`의 `liveTables`
  → **남은 순회 워커는 전부 재귀**. 기하는 G1(`BlockExtent`), 문단 순회는 G2로 각각 단일화 완료.
- **라운드3 잔여 2건도 완료 (2026-07-29)**: ⑤ 문단 서식 명령 6종(정렬·제목·줄간격·줄높이·들여쓰기·인용)이 선택을 무시하고
  캐럿 문단에만 적용되던 문제 → `ApplyToSelectedParagraphs` 단일 choke point로 통일(임의 깊이 도달, 목록 명령과 동일 동작).
  `ToggleQuote`는 캐럿 문단이 방향을 정하고 선택 전체가 따름(목록 토글과 같은 규칙). ⑥ 문서 경계 Backspace/Delete가
  빈 undo 스텝 + `IsModified` 뒤집던 문제 → 인접 블록이 있을 때만 체크포인트(`WordDelete`와 같은 가드).
  ⑥ 작업 중 **Enter가 undo 체크포인트를 2번 push**하던 것도 발견·수정(첫 Ctrl+Z가 아무 일도 안 하는 것처럼 보이던 원인).
  테스트 11건 추가(366→377).
- **🟢 셀 블록 선택을 1급 개념으로 (2026-07-29, 완료)**: GUI 검증에서 나온 지적 — 셀을 가로질러 드래그하면 사각형이 파랗게
  칠해지는데 **연산은 선형 텍스트 범위를 따라가** 드래그 시작 이전 텍스트가 남고 서식도 부분 적용됐다. 원인: `SelectedCellRange`의
  소비처가 **렌더 2곳 + 컨텍스트 메뉴 2곳뿐**이고 편집/서식 경로는 아무도 안 봤음. 어긋남은 양방향 — 덜 적용(첫/끝 셀이 드래그
  오프셋부터)이자 더 적용(문서 순서가 사각형 **밖** 셀까지 쓸어담음, 3열 표의 세로 블록이 오른쪽 셀까지).
  → `SelectedCellsBlock()`을 만들고 **Delete·문자서식·문단서식·목록 명령이 모두 이걸 먼저 참조**. Delete는 (A) Excel/HWP식
  —선택 셀 내용만 비우고 격자 유지(행·열 삭제는 명시적 메뉴 전용). 단일 셀 블록도 1급(`SelectedCellRange`가 단일 셀에 null을
  반환해 선택 자체가 불가능했음) + 컨텍스트 메뉴 **"셀 선택"** 신설. 캐럿 이동 시 셀 블록 해제(방향키 후 칠해진 채 한 글자만
  지워지던 같은 계열 불일치). 테스트 10건(377→387).
- **셀 안 인라인 표 마우스 진입 불가 수정 (2026-07-29)**: GUI 검증 2차 지적 — 인라인 표가 든 문단을 **셀에 붙여넣으면** 표는
  그려지는데 클릭으로 셀 진입도 드래그 선택도 안 됨. 원인은 또 규칙 #1 위반 — 최상위 문단 히트테스트에는 인라인 표 하강이
  있는데 **셀 내용을 훑는 `HitTestBlockList`에는 없어서** 호스트 문단의 ObjChar에서 멈췄다(렌더는 `DrawCellBlockList`가
  `FlushInlineTableDraws`를 호출해 정상). 박스 기하를 `InlineTableBoxAtPoint` 단일 출처로 뽑아 캐럿·링크 walk 양쪽에 연결.
  덤으로 **인라인 표 안 하이퍼링크가 어디서도(최상위 포함) 클릭되지 않던 것**도 같이 수정. 테스트 3건(387→390).
- **🟢 IME 조합 중 셀 행 높이 (2026-07-29, 완료)**: 렌더는 preedit를 캐럿 문단에 splice하는데 measure(`MeasureCellContentHeight`)는
  빼고 다시 빌드해서, 행이 조합 전 크기로 남고 조합 글자가 셀 아래 테두리를 넘어갔다(좁은 셀에서 한글 입력 시 줄바꿈마다).
  → `PreeditAwareLayout`으로 measure가 렌더와 같은 레이아웃을 쓰게 하고, **조합 시작/종료 시 상위 표 체인의 기하 캐시를 evict**
  (조합은 모델을 안 바꾸므로 아무도 무효화해주지 않음 — 중첩 셀은 호스트 행까지, 인라인 표는 호스트 문단 줄까지). 테스트 3건(390→393).
  최상위 문단도 같은 갭이 있었으나(실측 65 vs 65) 증상이 달라 A안으로 별도 처리 — `MeasureContentHeight`에서만 조합 높이를
  적용하고 `BlockExtent`는 그대로 평문 레이아웃을 히트테스트·페이지네이션에 넘긴다(preedit 레이아웃의 인덱스는 논리 오프셋이
  아니라 표시 위치라, 넘기면 조합 중 클릭이 밀림). 겸사겸사 `ParagraphWrapWidth`로 래핑 폭 공식 복제 2곳을 단일화. 테스트 2건(393→395).
- **🟢 줄 오른쪽 클릭 시 캐럿이 문단 길이+1 (2026-07-29, 완료)**: IME A안 검증 중 발견한 **기존** 버그(조합과 무관 — 유무
  동일 확인). `HitTestIndex`가 Avalonia의 `TextPosition + IsTrailing`을 그대로 반환하는데 줄 끝 너머에서 length+1이 나온다.
  CJK·길이 무관 재현(`'ab'`→3, `'hello world'`→12). 확인된 결과 2가지: **①Backspace가 아무것도 안 지움**(삭제 범위가 모든
  run 밖), **②그 위치 타이핑 시 서식이 끊긴 새 run 생성**(굵은 줄 뒤 입력 → 보통 글씨). "줄 오른쪽 여백 클릭"은 일상
  동작이라 영향이 컸다. → `HitTestIndex`에서 문단 길이로 클램프(캐럿 배치·드래그 선택·링크·셀 진입이 전부 이 함수를 지나가
  므로 호출처가 아닌 여기서). 시그니처에 `Paragraph`를 받게 해 호출처가 빠뜨릴 수 없게 함. 테스트 9건(395→404).
- **🟢 인라인 표 2건 (2026-07-29, 완료)**: GUI 검증 3차 지적.
  ① **인라인 표 안 우클릭에 표 메뉴가 없음** — 메뉴가 `GetBlockAtPoint`(최상위 블록 전용)로 대상을 고르는데 인라인 표 그리드는
  문단 inlines에 달려 있어 안 잡힘 → 평문 메뉴로 빠짐. 히트 위치에서 대상을 푸는 `ContextMenuTargetTable`로 교체(메뉴 빌더는
  이미 인라인 표를 지원하고 있었고 라우팅만 빠져 있었음).
  ② **인라인 표 행 높이 드래그가 호스트 문단에 즉시 반영 안 됨** — 인라인 표는 호스트 문단의 **줄 상자 안에** 배치되는데,
  리사이즈 드래그는 편집을 거치지 않아 프레임이 "trusted" 패스로 돌고, 그러면 캐시된 문단 레이아웃을 시그니처 검사 없이
  그대로 반환한다. 표 캐시만 evict하고 있어서 문단은 다음 클릭/타이핑까지 옛 줄 상자 유지(실측 80→80, 호스트 문단까지
  evict하면 80→206). → `InvalidateTableChain`으로 **표 자신 + 바깥 표들 + 경로상 인라인 표의 호스트 문단**을 함께 evict,
  행·열 리사이즈 양쪽에 적용. 같은 이유로 stale이던 IME 조합 경로도 이 헬퍼를 공유하도록 통합. 테스트 5건(404→409).
- **🟢 ↑/↓ 표 셀 진입을 HWP식으로 (2026-07-29, 완료)**: 마지막 남은 설계 판단 항목. **↓는 위 문단에서 표의 첫 행으로
  진입**(캐럿 x가 있는 열), **↑는 아래에서 마지막 행으로**. 표 안에서는 행 단위 이동, 끝 행에서는 인접 문단으로 나감.
  기존에는 블록 캐럿에 멈췄다가 다음 누름에 표 전체를 건너뛰어서, **Tab·→·클릭 없이는 키보드로 셀에 못 갔다**.
  블록 캐럿은 그대로 유지 — ←/→와 테두리 클릭으로 도달하며 표 들여쓰기/삭제가 거기 있다. **이미지는 옛 동작 유지**
  (내부에 텍스트가 없으므로 ↑/↓가 블록 캐럿에 멈춤). 블록 캐럿에서의 ↑/↓도 같은 규칙으로 통일(문단에서는 진입하는데
  블록 캐럿에서는 건너뛰던 비일관 제거). 테스트 7건 신규 + `BlockCaretTests` 6건을 새 모델로 갱신(409→416).
- **🟢 블록 캐럿 타이핑 2건 (2026-07-29, 완료)**: GUI 검증 4차 지적 — **표 오른쪽 캐럿에서 Space를 누르면 공백이 표 앞으로**.
  들여쓰기는 "블록 앞 여백" 기능인데 블록 캐럿 양쪽 모두에서 발동해, 캐럿과 반대편에 간격이 열렸다. → 들여쓰기/내어쓰기
  (Space·Tab·Shift+Tab)를 **선행 쪽 전용**으로 한정하고, 후행 쪽은 일반 타이핑으로 넘김.
  확인 중 **같은 원인의 더 넓은 버그** 발견: 블록 캐럿을 해제할 때 텍스트 캐럿을 안 옮겨서, **표 뒤에서 글자를 치면 표의
  마지막 셀에**, 표 앞에서 치면 첫 셀에 들어갔다(양쪽 다 재현). → 해제 시 블록 캐럿이 가리키던 위치(앞 문단 끝 / 뒤 문단 시작)
  로 캐럿 이동. 테스트 6건(416→422).
- **🟢 수식키 단독 누름 (2026-07-29, 완료)**: GUI 검증 5차 — **Shift를 누르자마자 캐럿이 이동**. 직전 커밋에서 블록 캐럿
  해제 시 캐럿을 옮기게 했는데, **Shift 단독 누름도 "다른 키"로 취급**돼 해제 경로를 탔다(그 전에도 블록 캐럿은 풀리고
  있었으나 캐럿이 안 움직여 안 보였을 뿐). 같은 fall-through가 **이미지 선택도 Ctrl+C 전에 취소**시키고 있었음(기존 버그).
  → 키 핸들러 맨 앞에서 수식키(Shift/Ctrl/Alt/Win, Caps/Num/Scroll Lock) 단독 누름을 무시. 테스트 8건(422→430).
- **🟢 잔여 3건 정리 (2026-07-29, 완료)**: ① **Shift+Tab이 표 밖에서 공백 4칸 삽입** — `HandleTab`이 shift를 무시하고
  Tab과 같은 분기를 탔다. → 문단 내어쓰기(`Indent(-20)`). ② **조합 중 클릭이 엉뚱한 오프셋** — 화면 글리프에는 preedit가
  있는데 히트테스트는 평문 레이아웃을 읽어, 조합이 길어질수록 어긋나고 평문 끝을 넘으면 문단 끝으로 클램프됐다.
  → 조합 레이아웃을 히트테스트하고 **표시 인덱스 → 논리 오프셋 역변환**(조합 앞은 그대로, 뒤는 길이만큼 당김, 안쪽은
  조합 시작으로 — 조합은 주소 가능한 위치가 아닌 한 덩어리). ③ **셀 안에서는 walk가 평문 높이로 진행** — 셀 rect는
  조합만큼 커지는데 내부 진행은 안 따라가, 조합의 줄바꿈된 줄 클릭이 아래 블록으로 귀속됐다. → `DrawnHeight`로 통일.
  테스트 6건(430→436).
- **🟢 캐럿 스크롤 + Shift+Tab 재수정 (2026-07-29, 완료)**: GUI 검증 6차.
  ① **타이핑이 캐럿을 뷰로 스크롤하지 않음** — 다른 편집은 전부 `ResetCaretBlink`를 거쳐 bring-into-view를 요청하는데,
  타이핑 경로는 그 호출을 일부러 피한다(undo 병합 run이 끊겨 키 하나당 체크포인트가 생김). 그래서 **요청이 아예 0건**
  (라우팅 이벤트로 실측). 셀에서 특히 잘 드러난 건 셀이 아래로 자라기 때문. → 병합을 건드리지 않고 플래그만 세움.
  ② **Shift+Tab이 Tab 뒤에 반응 없음** — 직전 수정이 `Indent(-20)`이었는데 Tab은 **공백 4칸을 넣으므로** 서로 역연산이
  아니었다. → 캐럿 앞 공백을 최대 4칸 제거하고, 없을 때만 문단 내어쓰기로 폴백. 테스트 8건(436→444).
- **🟢 툴바 포커스 강탈 (2026-07-29, 완료)**: GUI 검증 7차 — **툴바 내어쓰기를 누르면 캐럿이 사라지고 입력이 안 됨**.
  캐럿은 에디터가 포커스를 가진 동안만 그려지는데 툴바 버튼이 포커스를 가져가고 아무도 돌려주지 않았다. 명령 자체는
  기억된 캐럿 위치로 실행되니 "버튼은 되는데 타이핑만 죽는" 상태 — 사용자가 "캐럿 위치는 기억한다"고 관찰한 그대로다.
  **내어쓰기만이 아니라 모든 툴바 버튼이 동일**했다. → 조립된 스트립을 한 번 훑어 모든 Button의 `Focusable`을 끔
  (색·목록·줄간격·표 삽입 등 인라인 생성 버튼이 팩토리를 안 거치므로 팩토리별 플래그로는 부족). 스타일로도 가능하지만
  헤드리스에 스타일 루트가 없어 검증이 불가능해 결정적 방식을 택함. 콤보는 드롭다운에 포커스가 필요하므로
  **DropDownClosed에서 반환**(SelectionChanged에서 하면 열린 목록을 화살표로 훑는 중에 뺏김). 테스트 2건(444→446).
- **남은 것**: ~~↑/↓ 표 셀 진입~~ → **완료**, ~~동기 `ParseHtml` 원격 이미지~~ → **수정 완료**(동기 경로는 네트워크 I/O
  없음, 원격은 `ParseHtmlAsync`)


---

## 🗂️ 0.9.0 ~ 라운드32 작업 로그 (2026-07-12 ~ 2026-09-19)

> 2026-09-19 로드맵 정리 때 `Project_Roadmap.md`에서 원문 그대로 옮겼다. 라운드 번호 14·15는 두 번 쓰였다(09-07/08 전수조사 · 09-09 대체 텍스트/셀 세로 정렬).

### 0.9.0 게시 시점 상태 블록 (2026-07-12)

## ✅ 현재 상태 (2026-07-12 · `0.9.0` NuGet 정식 게시)

> **0.9.0에 들어간 WinUI 포트(WinUIRichEditor) 기능 백포트** (`EditorMode` 제거로 major-ish):
> 1. 문서 내 `PageSetup` 영속화(용지·방향·머리글/바닥글·쪽번호, JSON/.flow, 로드 시 적용)
> 2. `IncreaseFontSize`/`DecreaseFontSize`(표준 크기 사다리)
> 3. 중앙 단축키 테이블 `RichEditorShortcuts`(Word 표준 — 키핸들러·메뉴 힌트·툴바 툴팁 단일 출처, 신규 단축키 다수)
> 4. HWP식 컨텍스트 메뉴 재구성 + 슬림 기본 `ShowFormattingMenu`(캐럿 상태 반영·단축키 힌트)
> 5. 툴바 `ToolbarLevel`(Auto/Minimal/Normal/Maximum 밀도) + 페이지/줌·Export/Import/Print 툴바 내장(줌은
>    호스트 훅, `RichEditorView`는 자체 크롬 제거하고 위임; read-only=view 툴바)
> 6. `EditorMode` enum 제거 → `IsReadOnly` + `Allow*`
> 7. **기본 `PageSize`를 `Continuous`로 통일**(A4→Continuous, WinUI와 일치)
>
> **인터랙티브 GUI 동작(툴바 줌/페이지·read-only view 툴바·컨텍스트 메뉴 상태 반영)은 데모 육안검증 필요.**

> 🚧 **미릴리스 작업 (0.9.0 이후)** — 상세 변경은 [`CHANGELOG.md`](CHANGELOG.md) Unreleased, 날짜별 작업 로그는
> [`docs/roadmap-archive.md`](docs/roadmap-archive.md)의 "라운드2/3" 절.
> - **라운드2**: WinUI 포트가 앞서간 기능 5종 백포트(`IsModified`, `RemoveList`, `AutoLinkOnType`,
>   `AllowRemoteImagesOnPaste`, 찾기 highlight-all) + WinUI 버그 대조 + 전 소스 정독 감사 결함 9건 수정
> - **라운드3**: 전수 조사에서 **결함 24건** 수정 — G2(문단 순회 워커 재귀화), 셀 블록 선택을 1급 개념으로,
>   IME 조합 중 셀 행 높이, 줄 오른쪽 클릭 캐럿 off-by-one, 인라인 표 우클릭/리사이즈, ↑/↓ 셀 진입(HWP식),
>   블록 캐럿 타이핑, 수식키 단독 누름, 툴바 포커스 강탈 등. **11건은 GUI 육안검증에서만** 드러났고 전부
>   포인터·포커스·키 조합 계열이었다 — 그래서 1.0 최우선이 P1(상호작용 테스트 인프라)이 됐다.
> - **1.0 준비(P1~P5, 2026-07-30~31)**: 상호작용/렌더 픽셀 테스트 인프라, 상호운용(HTML 인라인 표 왕복 ·
>   RTF 표 내보내기/중첩 표 가져오기), 성능 실측, API 확정 리뷰. 그 과정에서 결함 7건 추가 수정.
>   **446 → 499 unit + 9 → 17 render.**
> - **라운드4 · 1.0 출시 전 전수 검증(2026-07-31)**: 빌드 0 warn · 513 unit + 17 render 그린 · `dotnet pack`
>   정상 · **데모 Native AOT 퍼블리시 성공**(트림 경고 1건은 데모의 `ViewLocator`, 라이브러리는 클린).
>   마일스톤 A+B가 **동시에** 들어간 문서(셀 안 중첩 표 + 인라인 표 + 병합 + 셀 안 이미지)를 JSON/`.flow`/
>   HTML/RTF 네 포맷에 전부 통과시키는 "종합" 왕복 테스트 신설 — 축별 테스트가 못 보던 조합에서 **결함 3건**
>   적발·수정(인라인 표 셀 안 블록 삭제 불가 / 인라인 표 호스트 문단 캐시 시그니처 누락 / HTML 왕복마다
>   인라인 표 뒤 공백 1칸 증식) + README의 RTF 설명 1건 갱신. 편집 불변식 7건(깊은 곳 undo 캐럿 복원·
>   `GetPlainText`·`IsModified`·전체선택 삭제·Find/ReplaceAll 도달성)은 **이미 정상**으로 확인.
> - **RTF/HTML 육안검증 완료(2026-07-31, 사용자 실측 — Word·HWP·브라우저)**: 오래 보류돼 있던 항목.
>   **결함 4건**이 여기서만 드러났다 — ⓐ `\clbrdr*`을 안 써서 **Word/HWP에서 표 테두리가 아예 없음**
>   ⓑ 중첩 표 앞 문단을 `\par`로 안 닫아 부모 텍스트가 첫 중첩 셀에 붙음 ⓒ 중첩 표 **뒤** 문단을 아직
>   `\itap2`인 채로 써서 Word가 통째로 폐기 ⓓ HTML 인라인 표가 `width:100%`라 브라우저·Word에서 전폭
>   블록으로 깔림. 넷 다 수정 + 회귀 테스트.
>   **핵심 교훈**: 우리 reader는 우리 writer의 잘못된 출력을 관대하게 읽어내므로, **자체 왕복 테스트로는
>   절대 안 보인다.** 외부 앱 육안검증이 유일한 검출 수단이었다.
>   재현용 문서 생성기는 스크래치의 `rtfgen`(라이브러리 참조 콘솔) — 필요하면 다시 만들 것.
>   후속으로 RTF **가져오기** 비대칭 2건도 해소: 셀 병합·셀 배경을 읽어들이게 했고(쓰기는 원래 하고 있었다),
>   인라인 표는 무시 가능 그룹 `{\*\arinline}`으로 표시해 자체 왕복에서 복원한다(외부 앱은 종전대로 블록 표).
> - **라운드5 · 게시 직전 전수조사(2026-07-31)**: 패키지 실물 검사(의존성 2개·XML 문서 포함·잡파일 없음),
>   그리고 **깨진/악성 입력** 전면 투입. **결함 3건 + API 계약 1건**:
>   ⓐ `<td colspan="100000000">` 하나로 HTML 임포터가 메모리 고갈(테스트 프로세스 1.3GB에서 정지 실측) —
>   `rowspan`은 막혀 있었는데 `colspan`은 상한이 없었다 ⓑ JSON/`.flow`의 선언 표 크기로 `TableBlock` 생성자가
>   **바로 버려질** 그리드를 선할당 → 1×1로 변경 ⓒ `{"Blocks":[null]}` → NRE.
>   ⓓ **계약 불일치**: `Deserialize`는 "빈 문서 반환"이라 문서돼 있으나 실제로는 `JsonException`을 던지고,
>   `DocumentPackage.Load`는 정말로 삼켰다 — 정반대. **둘 다 예외로 통일**(사용자 결정): 손상 파일을 빈 문서로
>   읽으면 호스트가 "빈 문서"와 "손상"을 구분할 수 없어 저장 시 원본을 덮어쓴다.
>   **교훈**: 포매터는 전부 붙여넣기로 도달 가능한 신뢰 불가 입력 경로다. 정상 왕복만 테스트하면 안 된다.
> - **라운드6 · 수명주기/스레드 + `Input.cs` 정독(2026-07-31)**: **결함 3건**.
>   ⓐ `Document` 교체 시 선택 블록·블록 캐럿·인라인 이미지 선택·셀 선택 모드가 **전부 살아남아** 옛 문서
>   트리를 붙잡고, 없는 블록에 대해 Delete가 undo 체크포인트를 쌓았다 → `ResetInteractionState()` 신설
>   ⓑ **읽기 전용 에디터가 키보드 트랩** — read-only 키 게이트가 Tab까지 삼켜 포커스가 빠져나올 수 없었다
>   ⓒ "표 그리기" 무장 중 **마우스 이동마다 `new Cursor()`** (네이티브 핸들) — 그 메서드 자신의 주석을 위반.
>   **문제 없음으로 확인된 것**: undo 스택 50개 상한 + `ImageBlock.Clone`이 바이트를 공유(체크포인트당 이미지
>   메모리 증가 없음), 툴바·스크롤뷰어 구독이 detach에서 정확히 해제, 레이아웃 캐시 2종 모두 상한 존재
>   (문단 10000 prune / 표 2000 clear), `OpenUrl`이 http(s) 외 스킴을 실행하지 않음(`javascript:`·`file:`·
>   UNC 경로 7종 테스트로 고정).
> - **데모 GUI 육안검증(2026-07-31, 사용자 실측)**: 툴바 줌/페이지·read-only 뷰 툴바·컨텍스트 메뉴 상태는
>   모두 정상. **결함 1건** — 표 셀 안 이미지의 리사이즈 핸들이 먹지 않음(깊이 무관). 원인은 셀이 그림을 셀 폭에
>   맞춰 축소해 그리는데 드래그 계산은 **선언 크기**에서 출발한 것: 핸들은 축소된 모서리에 있고 수십 px 끌어봐야
>   같은 폭으로 클램프돼 화면이 안 움직였다. 핸들이 그려진 크기를 함께 들고 다니도록 수정 + 리사이즈 시
>   `InvalidateTableChain`/`InvalidateMeasure`(열·행 드래그와 동일). 상호작용 테스트 5건 신설.

### 1.0 준비 · 릴리스 · 라운드7~32

## 🎯 1.0까지 남은 일 (기능이 아니라 *검증·상호운용*)

> **방침(2026-07-29 합의)**: 0.10.0을 끊지 않고 **최대한 다듬은 뒤 1.0으로 게시**한다.
> 트레이드오프: 라운드3 수정 24건이 그동안 미릴리스로 남는다(중간 릴리스 여부는 미결).

**P1~P5 완료(2026-07-31), P6은 제외** — 남은 것은 릴리스 작업뿐이다.

**릴리스 체크리스트**
- [x] `<Version>` `0.9.0` → `1.0.0`
- [x] `PublicAPI.Unshipped.txt` → `Shipped.txt` 이관(신규 13건 추가, `ParseHtml`/`ParseHtmlAsync` 구 시그니처
      2건 삭제; `RoundTripHarness`는 커밋 51464ce에서 이미 제거돼 있었다) → Shipped 510줄, Unshipped 비움
- [x] `CHANGELOG.md` `[Unreleased]` → `[1.0.0] - 2026-07-31` (API 동결 선언 추가)
- [x] `README.md` 상태 배지 → `1.0.0`, SemVer 동결 문구로 교체
- [x] **`v1.0.0` 태그 push** → nuget.org 게시 완료
- [x] **macOS CI 실제 그린 확인 완료(2026-08-08, PR #12)** — P1 이후 열려 있던 항목. 스킵이 아니라
      **실측**: macOS 잡에서 유닛 727 + 렌더 17(real Skia) 전부 통과, 빌드 0 error. Linux/Windows도 동일.

**1.1.0 릴리스 (2026-08-08 준비 완료)** — 라운드4~9의 상호운용 수정 22커밋. 상세는 `CHANGELOG.md`.
- [x] `<Version>` `1.0.0` → `1.1.0` (공개 API 14건 추가, 제거·시그니처 변경 없음 → minor)
- [x] `PublicAPI.Unshipped.txt` → `Shipped.txt` 이관 (510 → 524줄, Unshipped 비움)
- [x] `CHANGELOG.md` `[Unreleased]` → `[1.1.0] - 2026-08-08` (나가는 바이트 변경 경고 포함)
- [x] `README.md`/`README.ko.md` 상태 배지 → `1.1.0`
- [x] **PR로 3-OS CI 그린 확인** ([PR #12](https://github.com/centwon/AvaloniaRichEditor/pull/12), 2026-08-08)
- [ ] **`v1.1.0` 태그 push** → CI `pack` 잡이 Trusted Publishing으로 게시 (**사람이 실행**)
우선순위는 라운드3의 실측이 정했다: 결함 24건 중 **11건이 GUI 육안검증에서만** 드러났고 전부
포인터·포커스·키 조합 계열이라, 당시 446개 테스트가 구조적으로 못 보는 영역이었다. 그래서 P1이 최우선이었다.

- [x] **P1 · 상호작용 테스트 인프라** (2026-07-30 완료, 471 unit + 9 render 그린 ×3연속)
      `InteractionHost`(테스트 전용): 헤드리스 Window에 편집기를 `Show()`하고 클릭·드래그·키·텍스트 입력을
      **문서 좌표**로 보낸다(`MapDocToView` 역변환 포함). 렌더 시점에만 기록되는 리사이즈 핸들은 `Render()`로 확보.
      대상 4개 영역 모두 실입력으로 전환 — 툴바 클릭(5), 리사이즈 드래그(6), 셀 드래그 선택(5), 우클릭 메뉴(4), 스모크(5).
      **하네스 제약(실측)**: `Show()` 없으면 입력이 컨트롤에 도달하지 않고, `Close()`는 여전히 금지(플랫폼 종료).
      팝업(우클릭 메뉴·플라이아웃)은 Window 템플릿의 오버레이 레이어가 필요 → 테스트 앱에 **Fluent 테마 적용**.
      툴바는 쓰고 나면 `Dispose()`로 떼야 한다(정적 `LanguageChanged` 구독이 남아 다른 스레드 테스트를 깨뜨림).
      **찾아낸 결함 2건**: ① 캐럿을 한 번도 놓지 않은 상태의 편집이 Undo 스택에 안 올라감(`PushState`가
      `Paragraph == null`에서 조기 return) ② 피커 플라이아웃(색·표·목록·줄간격)이 열릴 때 가져간 포커스를
      닫을 때 돌려주지 않음 → 캐럿 소실(라운드3 툴바 버그와 동일 계열). 둘 다 수정.
      **macOS CI는 여전히 미검증** — 첫 PR에서 확인할 것.
- [x] **P2 · 렌더 픽셀 테스트 깊이** (2026-07-30 완료) — `Tests.Render`(real Skia) **9 → 17건**.
      추가분: 병합 셀이 내부 경계선을 지우는지 / 셀 배경이 옆 셀로 번지지 않는지 / 중첩 표가 부모 셀 *안에*
      그려지는지(자기 경계선 3개) / 40행 표가 **행 경계에서 페이지 분할**되어 2페이지에 이어지는지 /
      **페이지 사이 데스크 간격에 아무것도 새지 않는지**(페이지별 clip 검증) /
      인라인 표가 텍스트 줄 안에 경계선을 그리는지 · 그 줄을 키우는지 / 셀 안 인라인 표까지 재귀 렌더되는지.
      단정은 계속 **구조적**(경계선 개수·잉크 bbox·어느 밴드에 글자가 있는지) — 골든 이미지는 플랫폼 간 비이식.
- [x] **P3 · 상호운용 격차** (2026-07-30 완료, 491 unit + 17 render 그린)
      ① **HTML 인라인 표 재가져오기** — 내보낼 때 `data-are-inline` 표시, 가져올 때 텍스트 줄에 복원(그 문단을
      다시 pending으로 열어야 한다 — HTML 파서가 `<table>`에서 `<p>`를 닫아 뒤 텍스트가 형제로 온다).
      외부 HTML은 표시가 없으니 종전대로 블록 표.
      ② **RTF 표 내보내기 완성** — 병합(`\clmgf`/`\clmrg`·`\clvmgf`/`\clvmrg`, 열마다 `\cellx` 유지)·셀 배경
      (`\clcbpat`)·셀 안 이미지/구분선/목록 마커/다중 문단·중첩 표(`\nestcell`/`\nestrow`, `\itap` +1)·인라인 표
      (호스트 문단을 분할 — RTF엔 인라인 표가 없다). **Word/HWP 육안검증은 미완**(props 그룹이 ignorable이라
      최악의 경우에도 셀 텍스트는 남는다).
      **찾아낸 임포트 결함 2건**(둘 다 평범한 Word 출력에서 발생): ⓐ 건너뛰는 destination 안의
      `\trowd`/`\cell`/`\row`/`\cellx`가 실제로 동작해, Word가 중첩 표 행 정의를 넣는 `{\*\nesttableprops …}`에서
      **부모 셀 텍스트가 통째로 폐기**됐다. ⓑ 그룹 닫힘에서 pending run을 건너뛰는 destination 상태로 flush해
      **모든 ignorable 그룹(북마크·필드·중첩표 props) 앞 텍스트가 사라졌다**.
      → 구조 제어어에 destination 가드 + 그룹 진입 전 run 커밋.
- [x] **P3 잔여 · RTF 가져오기 중첩 표** (2026-07-31 완료) — `\nestcell`/`\nestrow`를 **실제 중첩 표로** 읽는다.
      깊이는 `\itap`으로 구분(Word가 레벨을 구분하는 방식 — `\nestcell`만으론 구분 불가)해 **임의 깊이** 지원,
      부모 셀의 문단은 중첩 표 앞/뒤 원래 순서 그대로 유지(보류 문단을 깊이별로 보관 — 안 하면 중첩 셀이
      부모 텍스트를 가져간다). 부수 수정 2건: ⓐ `\nestrow`는 **ignorable 그룹 안에 오므로 destination 가드를
      의도적으로 걸지 않는다**(중첩을 지원하는 리더는 거기서 처리해야 함) ⓑ `{\nonesttables …}` 폴백 사본을
      건너뛴다(그 `\par`가 부모 셀에 빈 줄로 남던 것). **남은 손실**: 중첩 표의 열 너비(`\cellx`가 무시되는
      props 그룹에 있음) → 기본 너비로 들어온다. 쓰기 쪽도 한 곳 수정 — 중첩 표를 쓴 셀은 닫기 전에 자기
      `\itap`을 다시 선언해야 한다(안 하면 리더가 그 셀을 더 깊은 표에 넣는다).
- [x] **P4 · 성능 실측** (2026-07-30 완료) — 데모에 `--bench-table` 신설(중첩 표 + **인라인 표** 문서 +
      IME 조합 경로). 재현: `Demo.exe --bench-table` → `bench-table-results.txt`. **Release, Windows 11, 1000x800**.
      1유닛 = 문단 2개 + 인라인 표(2x2) 호스트 문단 + 2x3 표(첫 셀에 중첩 2x2).
      | 유닛 | 블록 | cold layout | warm 재측정 | 힙 | 스크롤(무효화) | Render median/p95 | 타이핑 rest median |
      |---|---|---|---|---|---|---|---|
      | 20 | 81 | 41.3 ms | 0.1 ms | 3.2 MB | 60 fps | 1.0 / 1.5 ms | 0.4~0.8 ms |
      | 50 | 201 | 48.2 ms | 0.0 ms | 3.9 MB | 60 fps | 1.0 / 1.5 ms | 0.3~0.4 ms |
      | 100 | 401 | 50.1 ms | 0.0 ms | 6.8 MB | 60 fps | 1.6 / 2.3 ms | 0.7 ms |
      타이핑은 평문 문단 / 인라인 표 호스트 문단 / 중첩 표 셀 **세 지점 모두** 측정 — 차이 없음
      (first keystroke 10~39 ms는 undo `Document.Clone()`, 종전 측정과 동일 성격).
      **IME 조합 비용(핵심 질문)**: 조합 글자당 **median 0.06~0.11 ms**(p95 0.18~0.55, 최초 1회만 14~24 ms),
      평문·인라인 표 호스트·중첩 셀 세 깊이 모두 동일 수준. **라운드3의 표 체인 evict는 사실상 무비용** —
      최적화 불필요로 종결.
      **회귀 가드(타이밍 대신 결정적)**: ① 조합이 **캐럿의 표 체인만** evict하는지(문서 전체 60개 캐시가
      조합 전후 그대로 60 — 전역 clear면 글자당 비용이 문서 크기에 비례하게 된다) ② 표 100유닛 힙 상한.
- [x] **P5 · 1.0 API 확정 리뷰** (2026-07-30 완료, 493 unit + 17 render 그린, 빌드 0 warn)
      **API 파일은 동기 상태** — `PublicApiAnalyzers` 경고 0(선언 누락 없음). 0.9.0 이후 델타는
      `PublicAPI.Unshipped.txt` 13개 추가 + `ParseHtml`/`ParseHtmlAsync` 시그니처 교체 2건뿐.
      **이름·기본값은 그대로 확정**, 단 하나만 변경: `AutoLinkOnType`이 유일하게 평범한 CLR 속성이었다
      → **`StyledProperty`로 승격**(`IsReadOnly`·`Allow*`와 동일, 바인딩/스타일 가능). 미출하 API라 호환 영향 없음.
      **문서와 실제 동작 불일치 6건 수정**: ① `RichEditor` 클래스 doc이 **제거된 `EditorMode` 프리셋**
      (ReadOnly/Basic/Full)을 아직 광고 ② `HtmlDocumentFormatter` — 인라인 표 왕복(`data-are-inline`) 누락
      ③ `RtfDocumentFormatter` — 쓰기가 읽기보다 넓다는 사실(병합·셀 배경은 내보내기 전용, 중첩은 가져올 때
      평탄화, 인라인 표는 문단 분할) 누락 ④ `RichEditorToolbar` — 버튼이 포커스를 안 가져간다는 보장(호스트가
      넣은 `Leading/TrailingItems` 버튼까지)과 피커 팝업이 닫힐 때 포커스를 돌려준다는 P1 동작 누락
      ⑤ `SetFindHighlight` — "모든 매치 강조"라고 적혀 있으나 실제로는 **현재 선택은 제외**(라운드2 수정)
      ⑥ README의 "RTF round-trippable" — RTF는 비대칭임을 명시.
      **`RoundTripHarness` 공개 표면 정리(사용자 승인, 2026-07-30)**: `internal`로 내리는 대신 **데모 프로젝트로
      이동**(`samples/AvaloniaRichEditor.Demo/RoundTripHarness.cs`, `internal`). 공개 포매터 API만 쓰므로
      `InternalsVisibleTo`가 필요 없고, 라이브러리가 개발 도구를 아예 싣지 않게 된다. 공개 API 제거라 breaking —
      그래서 1.0 동결 시점인 지금 한다(`*REMOVED*` 마커로 회수). `--roundtrip` 재실행으로 동작 확인 완료.
- [–] **P6 · mac/Linux 기능 실검증** — **1.0 블로커에서 제외(2026-07-31 결정)**. 메인테이너가 실기를 가질 수
      없으므로 직접 검증이 불가능하다. 확보된 것: **3-OS CI 그린**(빌드 + 499 유닛 + 17 렌더, macOS/Linux 포함 —
      P1에서 테스트 앱에 Fluent 테마가 들어갔으므로 팝업 경로도 여기서 검증된다). 미확보: 헤드리스가 못 보는
      실기 동작(이미지 디코드, 시스템 폰트·한글 폰트 폴백, IME 조합, 네이티브 클립보드 상호운용).
      → README에 이미 **"Windows에서 개발·테스트, mac/Linux는 best-effort"**로 명시돼 있으니 그대로 두고,
      실사용 리포트로 좁혀 나간다(이슈 유입 시 대응).

**릴리스 시 정리할 것** (2026-07-31 처리 완료)
- ~~"라운드2/3" 블록 70줄 이관~~ → `docs/roadmap-archive.md`의 "라운드2/3" 절로 옮기고 여기엔 요약만 남김
- ~~커밋 트레일러 불일치~~ → 실제 커밋에 맞춰 `CLAUDE.md`를 `Claude Opus 5`로 통일

**P1에서 새로 드러난 후속 항목** (2026-07-31 완료)
- ~~`RichEditorLocalization.Language`를 **UI 스레드 밖에서** 바꾸면 붙어 있는 툴바가 죽는다~~ →
  `OnLanguageChanged`에서 UI 스레드로 넘기도록 수정. 이벤트 자체는 호출 스레드에서 동기 발생한다는 점을
  XML doc에 명시(제3자 핸들러도 같은 처리를 하도록). 테스트 3건.

**견고성/성능 후속** (의도적 보류 — 측정상 저가치, 아카이브 N5/리뷰 절 참조)
- 델타 Undo(실측 기각), `ComputePageBreaks` 재계산 캐시, HTML `HasBlockOrMedia` O(n²), `ReplaceAll` O(N²) — 모두 희귀 경로 + 무효화 위험

---

> - **라운드7 · 게시 직전 표적 조사(2026-07-31)** — 안 써본 각도 3개만.
>   ① **랜덤 편집열 불변식 퍼즈**(20시드 × 300스텝, 30종 연산, 매 스텝마다 구조 불변식 검사): **결함 1건** —
>   리스트 켜기가 하드라인 분할 시 non-Run 인라인을 **복제**해서, 인라인 표 셀 안 캐럿이 문서에서 떨어져 나간
>   트리를 가리켰다(타이핑이 화면에 안 보이는 곳으로 감). 이동으로 변경. **이번 세션에서 유일하게 퍼즈만이
>   찾아낸 결함이다.**
>   ② **API 표면 동결 리뷰**: 공개 타입 31개, 내부 타입 누출 없음, 네임스페이스 일관 — **차단급 없음**.
>   ③ **현지화**: en/ko 119키 완전 일치, 코드가 쓰는 키 누락 0. 미참조 22키는 `GetString`이 public이므로
>   호스트용 제공 문자열 — 삭제하면 안 된다.

> - **라운드8 · WinUI 포트 백포트(2026-08-06)** — **결함 11건**. 상세는 `CHANGELOG.md` Unreleased.
>   포트 쪽에서 **퍼즈 시드를 24 → 400 → 20000으로 넓히고**, 그 퍼즈가 **이미지·구분선·빈 문단을 한 번도
>   생성하지 않는다**는 것을 발견해 축을 추가하면서 나온 것들이다. 전부 이쪽 소스에도 있었고,
>   신규 테스트 28개 중 **22개가 수정 전 소스에서 실패**한다(반증 확인). 유닛 **644 → 672**, 렌더 17 그린.
>   - **병합 격자**: `MergeCells`가 기존 병합을 가로지르면 앵커를 덮어쓰면서 그 앵커가 소유하던 셀을
>     해제하지 않아 **도달 불가능한 고아 셀**이 생겼다(표에 논리 셀이 하나도 안 남는 데까지 간다).
>     범위를 **닿는 병합 전체로 확장 → 해제 → 재스탬프**.
>     > **핵심 계약**: 병합 격자를 건드리는 연산은 피복 표시를 **짝으로** 남긴다 — 모든 피복 셀은 자기를
>     > 실제로 덮는 앵커를 가져야 한다. span을 줄이거나 앵커를 덮어쓰면 그 차집합을 해제할 것.
>   - **HTML 7건**: 중첩 목록 항목이 **사라지거나**(직계 하위 `<ul>` 미방문) 순서가 뒤바뀜 · 빈 문서를
>     저장/열기하면 **에디터 태그가 본문 텍스트로** · 연속 공백 붕괴 · 하이퍼링크의 명시 색이 링크 파랑에
>     덮임 · 목록 항목의 제목 레벨 소실 · 빈 문단 소실 · 이미지(및 인라인 표)가 앞 문단에 흡수.
>   - **RTF 4건**: 블록 이미지 밑에 빈 문단이 **왕복마다 증식** · 표 뒤 이미지가 표 **앞으로 이동** ·
>     구분선이 빈 문단으로 소실(`\brdrb`를 **읽는 쪽이 없었다**) · **잘린 `.rtf`가 열린 문서를 덮어썼다**.
>     마지막 것은 `TryParse`의 문서가 약속한 바로 그 시나리오인데 사실이 아니었다 — 잘림은 파싱을
>     중단시키지 않고, 리더가 입력을 다 쓰고 읽은 것을 마무리하므로 **더 짧은 문서를 깨끗이 읽은 것과
>     구별되지 않았다.** 실제로 검출하던 것은 **던지는** damage뿐이었고 `DamagedRtfTests`의 픽스처가
>     정확히 그 한 종류라, 구멍이 풀 커버리지 아래 숨어 있었다. 중괄호 균형으로 검출(`UnclosedGroups`).
>     `Parse`는 붙여넣기 경로라 관대함 유지 — 엄격함은 `TryParse` 몫이라는 것이 두 진입점을 나눈 이유다.
>   - **퍼즈 자체의 구멍**: `AssertTable`이 `LogicalCells()`만 돌아 **피복 슬롯을 아예 검사하지 않았다** —
>     위 고아 셀이 그대로 통과했다. 이제 양방향으로 단정한다.
>   - **교훈 2가지**: ① `Shape()`/관찰 쪽이 아는 형태를 **생성 쪽이 만들지 않으면 그 축은 조용히 0% 커버**다.
>     ② HTML 공백 판정에 `\s`나 `IsNullOrWhiteSpace`를 쓰면 **nbsp를 공백으로 센다** — 살아남으라고 넣은
>     문자를 접게 된다. 접히는 공백은 ASCII 5종뿐이고 `&nbsp;`는 내용이다.
>   - ⚠️ **나가는 HTML/RTF가 바뀌었다**(`&nbsp;` 인코딩, `data-are-fg`/`-h`/`-empty`/`-opens` 마커).
>     Word·HWP·브라우저 육안검증은 아직 안 했다 — 위 "핵심 교훈"대로 자체 왕복으로는 안 보이는 영역이다.

> - **라운드8 후속 · WinUI의 "게이트 추출"은 백포트하지 않는다(2026-08-06, 대조 완료)** — 포트가
>   `ChooseContextMenu` / `ChoosePointerTarget` / `MapImeRange`+`MapImeCaret` 세 개를 순수 함수로 뽑았다.
>   **동기가 여기엔 없다**: 그쪽은 결정이 `RightTappedRoutedEventArgs`·`PointerRoutedEventArgs`·
>   `CoreTextTextUpdatingEventArgs` 안에 있고 셋 다 생성자가 없어 도달이 불가능했다. 이 리포는
>   `InteractionHost`로 **실제 클릭·드래그·키를 주입**하므로 같은 경로를 끝까지 구동한다.
>   세 건을 각각 소스로 확인한 결과:
>   - **IME 오프셋 매핑 — 대응물이 아예 없다.** `CoreTextEditContext`/`TextUpdating`/`_imeRangeDelta`
>     검색 결과 **0건**이다. 여기는 `TextInputMethodClient`를 쓰는 완전히 다른 메커니즘이라 옮길 것이 없다.
>   - **컨텍스트 메뉴 — 분기 집합이 실제로 다르다.** 여기는 **읽기 전용을 객체보다 먼저** 보고, 블록 캐럿과
>     셀 선택 모드 분기가 있으며, 인라인 표 테두리 분기가 없다. 포트의 함수를 그대로 옮기면 이 리포의
>     동작을 **잘못 기술**하게 되고, 이름만 같고 뜻이 다른 함수가 양쪽에 생긴다 — 수렴의 반대다.
>   - **포인터 우선순위 — 계약은 동일한데(읽기 전용 → 이미지 핸들 → 인라인 핸들 → … → 셀 이미지)
>     여기선 이미 더 강한 방식으로 고정돼 있다**: `DraggingTheHandleOfAnImageInACell_ResizesIt` 등이
>     핸들을 실제로 드래그하므로, 선택 클릭이 먼저 잡혔다면 애초에 리사이즈가 일어나지 않는다.
>     순수 함수 테스트보다 이쪽이 낫다.
>   **결론**: 추출은 WinUI의 플랫폼 제약에 대한 대응이지 공유 설계가 아니다. 옮기면 순수한 손해다.
>   - 다만 **동작 차이 1건이 나왔고, 사용자 결정으로 포트에 맞췄다**: 읽기 전용에서 이미지를 우클릭하면
>     포트는 **이미지 메뉴(이미지 자체를 Copy)** 를 주는데 여기는 일반 복사+모두 선택을 줬다 — 그 Copy는
>     이미지가 아니라 (그 시점엔 대개 비어 있는) 텍스트 선택을 복사하므로, **읽는 사람이 그림을 가져갈
>     방법이 아예 없었다.** 두 부분을 함께 고쳤다:
>     ① `ShowContextMenu`에서 이미지 판정이 읽기 전용 분기보다 **앞으로** 온다
>     ② `BuildImageMenu`/`BuildInlineImageMenu`가 **Copy 뒤에서 return** 한다(그 아래는 전부 이미지를
>     변형하는 항목이라, ①만 하면 뷰어에 크기 변경·교체·삭제가 노출되는 **더 나쁜 회귀**가 된다).
>     실제 우클릭을 주입하는 테스트 3개로 고정했고 **둘은 서로 다른 절반을 지킨다** — ①을 되돌리면
>     `OffersTheImagesOwnCopy`가, ②만 되돌리면 `OffersNoEditingVerb`가 실패한다. 유닛 679 → 682.

> - **라운드9 · 라운드8 출력의 외부 육안검증, HTML 절반(2026-08-08)** — **결함 2건**. 상세는
>   `CHANGELOG.md` Unreleased.
>   재현용 문서 생성기를 스크래치에 다시 만들었다(`rtfgen`, 라이브러리 참조 콘솔): 라운드 4~8이 바꾼
>   출력만 모은 문서 4종을 `.rtf`/`.html`로 내보내고, **문서 안에 절 번호와 "…이면 실패" 문구를 직접
>   써 넣어** 사람이 코드를 안 보고도 판정할 수 있게 했다. 21개 항목 체크리스트 동봉.
>   - ⓐ **저자가 넣은 빈 줄이 브라우저에서 높이 0** — 빈 줄이 있는 문단 간격과 없는 간격이 **똑같이
>     16px**로 측정됐다. 마커만 나가고 실제 마크업은 안 나갔던 것(문단 여백은 이미 "두 번 내보내기"를
>     하고 있었는데 빈 줄만 빠져 있었다).
>   - ⓑ **셀 안 그림이 캡션 옆에 붙음** — bare 형식 판정이 **문단 수만** 세서, 문단 1개 + 이미지인 셀이
>     bare로 나가고 inline인 `<img>`가 같은 줄에 앉았다.
>   - **방법론 note**: 두 건 다 눈으로 본 게 아니라 **브라우저에서 `getBoundingClientRect`로 잰** 것이다.
>     실제로 링크 색은 스크린샷에서 파랗게 보였지만 측정하니 정상(초록)이었다 — 육안검증에서도
>     스크린샷보다 측정이 낫다.
>   - **RTF 절반(2026-08-08)** — **결함 1건 추가(누계 3건)**. 방법을 바꿨다: **Word COM 자동화로
>     "재기"**. Word 자신의 파서로 열고 객체 모델에 질문한다(`Tables(1).Range.Cells`, `Borders.LineStyle`,
>     `Headers(1).Range.Text`, `ListFormat.ListType`, `ComputeStatistics`). HWP 자동화 객체
>     (`HWPFrame.HwpObject`)도 사용 가능한 것을 확인했으나 아직 안 씀.
>     ⚠️ **읽기 경로만 쓸 것** — `Documents.Add`+`SaveAs2`(Word에게 참조 RTF를 쓰게 하기)는 헤드리스에서
>     **멈춘다**(10분 타임아웃, 보이지 않는 WINWORD 프로세스 잔류). 하네스는 열기·질의·닫기만 한다.
>   - ⓒ **가로 병합이 Word에서 안 보였다** — `\clmgf` 셀을 Word가 폭 0으로 접고 옆 칸을 빈 칸으로 남긴다.
>     최소 문서 V1~V7로 좁혀 확정했고, **기하 표현**(span 오른쪽 끝에 `\cellx` 하나, 피복 열은 경계·`\cell`
>     없음)만이 Word에서 올바른 표를 만든다. writer·reader를 **둘 다** 고쳤다 — reader는 이제 열 격자를
>     **모든 행의 `\cellx` 합집합**으로 만들고 건너뛴 경계에서 colspan을 역산한다(기존 플래그 읽기는 유지).
>     세로 병합은 정상이라 손대지 않았다.
>     > **교훈**: writer와 reader가 **서로만 아는 방언**으로 합의하면 자체 왕복은 완벽하다. 기존 테스트
>     > 2개가 옛 표기를 단정하며 통과하고 있었던 것이 그 증거다.
>   - **판정된 것**: #1 공백 · #4 빈 줄 · #5 정렬 · #6 이미지 2개/이미지 밑 빈 문단 없음 · #7 구분선 ·
>     #8 인라인 표 · #10 테두리 · #11 병합(수정 후) · #12 셀 배경. #3(목록)은 **결함이 아니었다** —
>     라운드8이 의도한 대로 마커는 literal text(`listType=0`)이고 들여쓰기 36/72/108 + 행잉 −18pt가 실측됐다.
>   - ⓓ **RTF에 용지 크기가 아예 없었다** — `\paperw`/`\paperh`/`\landscape`를 쓰지도 읽지도 않아
>     A4 문서가 Word 기본 용지로 열리고 **자체 왕복에서도 `A4 → Continuous`로 소실**됐다. 라운드6이
>     페이지 설정의 머리글/바닥글 절반만 넣은 것. 치수는 `PaperDips`(컨트롤 레이아웃과 같은 표)로
>     역매칭하며, 모르는 용지는 **가장 가까운 이름으로 스냅하지 않고 그대로 둔다**.
>     Word 실측 `PaperSize=7`(wdPaperA4) — DIP 반올림 오차(11910 vs 규격 11906)는 Word가 스스로 A4로
>     스냅하므로 RTF 전용 정확 twips 표를 따로 두지 않았다(단일 출처 유지).
>     생성 파일을 4회 재읽기하다 `PageSize`가 변하는 걸 보고 찾았다 — 라운드6 테스트는 머리글/바닥글만
>     단정해서 볼 수 없었다.
>   - **Word 측정 완료: 28항목 PASS / 0 FAIL.** #21(바닥글 `/` 누적)도 4회 왕복 통과(바닥글·블록 수 불변).
>     하네스 자체 오류 3건도 수정 — #3 기준이 CHANGELOG와 어긋남, #7이 COM 예외로 오판,
>     #11이 `Rows.Item().Cells`를 써서 **세로 병합 표에서 예외**(→ `Range.Cells` 순회로 변경).
>   - **HWP 육안검증 완료(2026-08-08, 사용자 실측)** — 9항목 중 **8개 정상, 결함 1건**.
>     ⓔ **HWP에서 용지가 Letter** — ⓓ에서 넣은 문서 레벨 `\paperw`만으로는 부족했다. HWP는 **섹션 레벨**
>     (`\sectd\pgwsxn\pghsxn`)만 읽는다. 양쪽 다 쓰고 양쪽 다 읽도록 수정(같은 값이라 동기화 부담 없음).
>     Word는 수정 후에도 `PaperSize=7` 유지 확인.
>     **가로 병합(기하 표기)이 HWP에서도 정상**이라 Word/HWP가 서로 다른 표기를 원하는 최악의 시나리오는
>     없었다 — 그래서 여기서 멈춘다.
>     > **정지 규칙(사용자 판단, 2026-08-08)**: 인터롭은 이제 **리포트 주도**로 간다. 생성기·하네스가
>     > 남아 있으니 "Word/HWP에서 이상하다"는 리포트가 오면 그때 재현한다. 선제 전수 파기는 수확 체감.
>   - **도구를 리포로 이관**: [`tools/rtfgen/`](tools/rtfgen/README.md) — 생성기 + Word 하네스(28항목 자동
>     측정) + 체크리스트 2종 + 가로 병합 판단 근거(`merge-probe/` V1~V7). **솔루션에는 넣지 않았다**(CI 불변).
>   - **HWP는 자동화 불가로 판정(2026-08-08)**: HWP 12 객체는 만들어지지만
>     `RegisterModule("FilePathCheckDLL", …)`이 **예외 없이 False를 반환**하고(보안 모듈 미설치)
>     `Open`도 False다. 다만 **HWP 창에는 파일이 열렸다** — HWP가 우리 RTF를 못 읽는 게 아니라
>     자동화 경로가 막힌 것이다. → HWP는 사람이 직접 열어 보는 수밖에 없다(체크리스트 그대로 사용).

> - **라운드10 · 외부 감사 보고서 검증(2026-08-26)** — 외부 도구가 쓴 감사 보고서(2026-08-17자, 결함 25건
>   주장)를 코드와 전수 대조. 보고서 자체는 리포에 남기지 않았다. **17건 유효 / 8건 오진**. 오진 중 BUG-12는 존재하지 않는
>   "Before" 코드를 인용했고, BUG-09/07은 `ResetCaretBlink()`→`NotifyStatus()`→`InvalidateMeasure()`를
>   놓쳤으며, BUG-06/07의 `InvalidateTableChain` 요구는 `PushUndo()`가 `_textChangedPending`을 세워
>   `_trustLayoutCache=false`로 만드는 걸 몰라서 나왔다. PERF-09의 `ToArray()`는 성능 실수가 아니라
>   **재진입 방어**라 제안대로 고치면 깨진다. → 보고서는 참고 자료지 작업 지시서가 아니다.
>   - **수정 완료(우선순위 1~4)**: ⓐ HTML 내보내기에서 `Run` 안 소프트 개행(`\n`)이 `<br/>`로 나가지
>     않던 회귀 — 임포트는 `<br>`→`\n`으로 받고 있었고 `PreserveDroppableSpaces` 주석이 이 Replace를
>     **전제로** 쓰여 있었는데 코드에는 없었다(테스트도 없었다). ⓑ 인라인 객체만 있는 셀이 병합에서
>     유실(텍스트 유무로만 판정) — 라운드3이 고친 "추가 블록 유실"과 다른 케이스. ⓒ RTF가 `RawBytes==null
>     && Image!=null`인 그림을 조용히 누락(공개 `Image` 세터가 `RawBytes`를 비운다) → `WritePict`에 PNG
>     인코딩 폴백. ⓓ HTML `<img width=200>`처럼 한 축만 선언되면 다른 축이 원본 크기로 남아 종횡비 파괴.
>     ⓔ RTF 파라미터 `int.Parse` → `TryParse`. ⓕ `TextRange.Delete()` 단독 호출이 빈 `Inlines` 문단을 남김.
>   - **계약 변경**: ⓔ로 "거대 파라미터 = 손상"이 아니게 됐다. `DamagedRtfTests`가 그 예외를 손상 픽스처로
>     쓰고 있어서 **절단(unclosed group)** 픽스처로 교체했고, 진단 채널 테스트 4개는 파서에 남은 throw가
>     없어 **이미지 디코드 실패**로 옮겼다.
>   - ⓖ **덤으로 잡은 플레이키 테스트** — `SelectionBrushProperty`의 기본값이 mutable `SolidColorBrush`라
>     정적 초기화를 실행한 스레드에 귀속됐다. 헤드리스 세션 스레드가 바뀌면 상호작용 테스트 7개가
>     "calling thread cannot access this object"로 죽었다(수정 전 실측 1/4 확률, 수정 후 11회 연속 그린).
>     `ImmutableSolidColorBrush`로 교체 — CLAUDE.md 규칙 #8이 문서 모델에 요구하는 것을 **속성 기본값**이
>     빠뜨리고 있었다. `FindMatchBrush`도 같이. `RichEditorToolbar`의 static brush 3개
>     (`ActiveBrush`·`NoColorBrush`·`DimInk`)는 처음엔 "안 걸린다"고 미뤘다가 **2026-08-27에 수정** —
>     `SelectionBrush`도 터지기 전까지는 정확히 그 상태였다.
>     > **회귀 방어**: 프로세스 전역으로 공유되는 두 형태(`static readonly` 브러시/펜 필드 +
>     > 브러시 타입 속성 기본값)를 라이브러리 전체에서 훑는 테스트를 넣었다. 고친 자리를 이름으로
>     > 박지 않고 쓸어담으므로 **다음 것은 추가되는 날 잡힌다**. 수정을 되돌리면 셋을 이름으로
>     > 지목하며 실패하는 것까지 확인했다. 호스트가 지정한 브러시는 그대로 이긴다(기본값만 바뀜).
>   - **후속: BUG-08 + BUG-06(2026-08-26)** — 문서 높이를 바꾸는데 `InvalidateMeasure()`를 안 부르던
>     두 계열. 표 행/열 4개(`TableInsert/DeleteRow/Column`)와 이미지 크기 프리셋 4개
>     (`Reset/ScaleImageSize`, 인라인판)가 `InvalidateVisual()`만 불러 ScrollViewer가 편집 전 extent를
>     유지했다. 나머지 구조 편집은 `ResetCaretBlink()`→`NotifyStatus()`로 우연히 커버되고 있었고,
>     이 8개만 그 경로를 안 탄다(리사이즈 **드래그**는 release에서 무효화하므로 처음부터 무관).
>     보고서가 함께 요구한 `InvalidateTableChain`은 **넣지 않았다** — `PushUndo()`가 이미 캐시를 버린다.
>     > **측정 노트**: `TableInsertColumn`은 높이가 안 변한다. 열은 자기 폭을 유지하고
>     > `MeasureOverride`는 content width가 아니라 **available width**를 돌려주기 때문. 무효화는
>     > 높이가 실제로 움직이는 경우(키 큰 열 삭제, 페이지드 모드의 `ComputePageBreaks`)를 위한 것이고,
>     > 테스트도 이 케이스만 플래그를 단정하고 높이 불변을 명시적으로 기록해 둔다.
>   - **후속: PERF-02 + PERF-03(2026-08-27)** — 보고서의 성능 항목 중 실효가 있는 둘만. 출력 바이트도
>     분기 결정도 안 바뀌며, 테스트가 단정하는 것이 바로 그 **등가성**이다.
>     ⓐ `WritePict`의 바이트당 `ToString("x2")` → 청크 단위 `Convert.TryToHexStringLower`(스택 버퍼).
>     한 번에 다 변환하면 5MB 그림에 10MB char 배열이 생기므로 청크로 나눴다.
>     ⓑ 툴바 Import가 포맷 판별 전에 Latin1·UTF-8 **양쪽으로 전체 디코딩**(+ `ToArray()` 2회)하던 것을
>     버퍼 스니핑으로. RTF 시그니처와 그 앞 공백은 Latin1에서 전부 1바이트라 바이트로 판별 가능하다.
>     > 바이트 스니프는 `LooksLikeRtf(string)`과 **정확히** 같은 답을 내야 한다(틀리면 RTF가 JSON
>     > 로더로 조용히 넘어간다). 그래서 테스트가 둘을 직접 대조하고, `TrimStart()`가 U+0100 미만에서
>     > 지우는 집합의 비자명한 둘(NEL 0x85 · NBSP 0xA0)까지 포함한다. 그 두 값을 스킵 집합에서 빼는
>     > 변이를 넣어 테스트가 실제로 잡는 것도 확인했다. BOM은 일부러 안 건너뛴다(문자열판과 동일).

> - **라운드11 · WinUI 포트 백포트(2026-08-28)** — 포트의 2026-08-26 감사 라운드에서 넘어온 후보 3건 중
>   **2건이 실재했고 1건은 오진이었다.** 둘 다 **읽어서가 아니라 돌려서** 확인했다.
>   - ⓐ **제어어의 홀로 선 `-`가 먹히던 것** — 파라미터는 `-` 다음에 **숫자**여야 하는데 부호만 보고
>     소비해 `{\rtf1\ansi\fs-x hello}`가 `"x hello"`로 나왔다(`-` 소실). 중괄호 균형이 맞는 완전한
>     문서이고 Word는 `\fs` + 텍스트 `"-x hello"`로 읽는다. 전방탐색으로 교체.
>   - ⓑ **CSS 퍼센트 색상 미인식 + 범위 밖 채널이 던지던 것** — 정규식이 `\d+`뿐이라
>     `rgb(100%, 0%, 0%)`는 색이 조용히 버려졌고, `\d+`에 상한이 없는데 `int.Parse`라
>     `rgb(99999999999, 0, 0)`는 **`OverflowException`이 `ParseCssColor` → 워크 → `ParseHtml` 밖으로
>     튀어나왔다**(색 하나가 아니라 붙여넣기 전체가 죽는다). 퍼센트 허용 + `TryChannel`(TryParse·클램프).
>   - ❌ **오진: 서로게이트 페어** — 포트가 "상류엔 `IsSurrogatePair`가 한 곳도 없다"고 보고했으나
>     **이미 완전히 처리하고 있다**: `PrevCharBoundary`/`NextCharBoundary`가 Backspace·Delete·←·→
>     **네 자리 전부**에 들어가 있고, 다만 `IsHighSurrogate`/`IsLowSurrogate`를 쓴다.
>     → **grep 한 토큰으로 상류 상태를 판정하면 이렇게 된다.** 포트 쪽 기록도 정정됐다.
>   - `BackportRound6Tests` 3개 신설, 757 → 760. 반증 확인(ⓐⓑ 되돌리면 각 테스트가 실패).
>   > **라운드10과의 우연한 일치**: 포트도 같은 시기에 RTF 오버플로 계약을 "손상 아님"으로 바꿨고,
>   > 그 결과 **손상 픽스처를 절단으로 교체**하고 **진단 채널 테스트의 fault 원천을 옮겨야** 했다 —
>   > 라운드10 ⓔ가 여기서 겪은 것과 글자 그대로 같은 연쇄다(포트는 `ColorUtil.ParseHex`로, 여기는
>   > 이미지 디코드 실패로 옮겼다). 두 프로젝트가 독립적으로 같은 결론에 도달했다.
> - **라운드12 · 문단 서식 목록의 두 번째 사본(2026-09-06, 포트 백포트)** — 포트가 언두 축을 감사하다
>   찾은 것으로, **양쪽에 글자 단위로 동일하게** 있었다. `Paragraph.Clone`이 `CopyFormatFrom`의 필드
>   13개를 손으로 복제했고, 심지어 `CopyFormatFrom`의 주석이 *"Mirrors the field list in `Clone`"* 이라고
>   **사람에게 부탁하고 있었다**(G2/워커 통합과 같은 모양). `Clone`이 그걸 부르게 했다. 동작·API 불변.
>   - 무게는 중복이 아니라 **실패 모양**이다: `Clone` 쪽을 잊은 새 속성은 편집 중엔 멀쩡하고
>     **첫 Ctrl+Z에서 사라진다**(언두 상태가 곧 clone). 이 프로젝트는 같은 모양을 이미 한 번 겪었고
>     그게 `CopyFormatFrom`이 생긴 이유다.
>   - **측정: 이 목록은 커버리지가 0이었다.** 공유 목록에서 `Background` 한 줄을 지우자 **기존 764개가
>     전부 초록**이었다. 퍼즈는 `Undo`/`Redo`를 부르지만 **구조 불변식만** 보고, 왕복 스위트는 포매터를
>     지나므로 `Clone`을 건드리지 않는다.
>   - `ParagraphCloneFidelityTests` 3개 신설(764 → 767). 가드를 **모델 속성 반사**로 세웠다 — 목록을
>     따로 적는 가드는 진짜 목록을 잊는 그 편집에서 똑같이 잊힌다. 모르는 속성 타입이 생기면 실패한다.
>   - 포트 쪽 가드는 다른 축이다(랜덤 편집열을 되감아 매 단계 복원 결과를 대조하는 퍼즈 축). 여기 퍼즈는
>     컨트롤을 몰기 때문에 언두 그룹이 op와 1:1이 아니라 그 판을 그대로 옮길 수 없었다.
>   > **대조하다 드러난 격차**: 포트의 `UndoState`에는 `ApproxBytes`가 있고 이력이 **바이트
>   > 예산(64MB, 최소 3단계)** 으로도 잘리는데, 여기 이력은 **개수 상한만** 있다. 큰 문서에서 스냅샷
>   > 50개는 포트가 예산을 넣은 이유 그 자체다 → **라운드13에서 처리**(아래).
> - **라운드13 · 언두 이력의 메모리 예산(2026-09-07, 포트 백포트 — 그리고 포트를 고쳤다)** —
>   이력이 **개수 상한 50만** 갖고 있었는데 스냅샷 하나가 문서 전체의 깊은 clone이다. 실측: 문단 20000
>   문서가 **체크포인트당 12.1MB**, 50개면 **592MB**가 에디터 하나 뒤에 쌓인다. 예산(64MB, 최소 3단계)을
>   넣었다.
>   - **백포트하려고 계측했더니 포트의 지표가 틀렸다는 게 드러났다.** 포트는 글자당 2바이트를 매겼는데
>     `Clone`은 **문자열을 참조 공유**한다 — 텍스트는 체크포인트 비용이 **0**이다. 같은 문서를 2자와
>     60자로 만들면 유지량이 **바이트 단위로 같고**(1814KB), 옛 모델은 그 둘을 2.3배 다르게 봤다.
>     즉 예산이 **공짜인 이력을 자르고 비싼 이력을 살리고** 있었다.
>   - 지표는 **요소 수 × 310바이트**(여기 실측값; 포트는 자기 모델에서 ~155다 — 같은 법칙, 다른 플랫폼).
>     실제 유지량과 몇 % 이내로 맞고, 인라인이 많은 경우만 13% 보수적(과다 = 안전).
>   - `UndoManager(long maxBytes)`는 **테스트 seam**이다. 실제 예산을 채우려면 요소 20만 개짜리 문서가
>     필요해 단위 테스트로 못 만들고, 그래서 정책이 검증 불가였다.
>   - `UndoBudgetTests` 7개 + `UndoBudgetProbeTests`(계측). 767 → 779. 반증 확인: 바닥 조건 / 인라인 표
>     하강 / 글자당 과금을 각각 되돌리면 해당 테스트만 빨개진다.
>   > ⚠️ **바닥 조건은 6회 푸시라야 검증된다.** `Trim`이 스택 ≤ `MinSteps`에서 조기 반환하므로 4회면
>   > 바닥을 지워도 답이 같다 — 포트의 첫 판이 실제로 그렇게 통과했다.
>   > ⚠️ **동작 변경**: 큰 문서의 이력은 이제 50단계보다 짧아진다(그게 목적이다). 텍스트가 긴 문서는
>   > 오히려 더 길어진다. API 변화 없음.

> - **라운드14 · 릴리스 판단용 전수조사(2026-09-07)** — 빌드 0 warn, 유닛 779 + 렌더 25 그린을 먼저 확보한 뒤
>   **아직 포트를 못 따라간 축 하나**만 팠다: 퍼즈 폭(포트는 20 → 20000, 여기는 20 그대로).
>   폭을 넓히기 전에 라운드8의 교훈("생성 쪽이 안 만드는 형태는 조용히 0% 커버")을 생성기에 먼저 적용했고,
>   거기서 **결함 1건**이 나왔다.
>   - **퍼즈 생성기에 병합이 없었다.** 불변식 쪽은 병합 격자를 양방향으로 25줄에 걸쳐 검사한다 —
>     라운드8의 고아 셀 결함을 잡으라고 쓴 코드다. 그런데 `Step`의 30종 연산 중 `MergeCells`/`UnmergeCell`이
>     하나도 없어서, **그 단정들은 생성된 문서에 한 번도 실행된 적이 없었다.** 라운드8이 명시한 그 교훈에
>     정확히 다시 걸려 있었다.
>   - ⓐ **병합 뒤 행/열 편집이 캐럿을 피복 셀에 주차시켰다** — `TableInsertRow`/`DeleteRow`/`InsertColumn`/
>     `DeleteColumn`이 착지 슬롯(새 행의 0열, 새 열의 0행)이 피복인지 **묻지 않고** `_caretPosition`에 직접
>     썼다. 피복 셀은 `LogicalCells()`에 없으니 렌더·클릭·포매터·내비게이션 어디에도 없다 — 타이핑이
>     문서가 보여줄 수 없는 문단으로 들어간다. 병합 범위 **안에** 행을 넣는 평범한 조작이 재현 경로다
>     (span이 늘어 착지 슬롯이 계속 피복). `FocusCell`은 처음부터 이 리다이렉트를 했고 주석도 그렇게 적혀
>     있는데, 이 네 곳만 `FocusCell`을 우회했다. → 공용 `CellCaretTarget`으로 통일.
>   - **검출력**: 병합 축을 넣자 **기존 20 시드 중 8개가 즉시 실패**. 수정 후 20 시드 그린, 이어서
>     **5000 시드를 손으로 돌려 그린**(2분 48초) — 이 축에 두 번째 결함이 없다는 근거는 5000 쪽이다.
>     CI 시드는 20 유지(8/20이 잡으므로 회귀 방어에 충분, `rtfgen`을 솔루션 밖에 둔 것과 같은 판단).
>   - `MergedTableCaretTests` 5개 신설. 779 → 784, 렌더 25. **반증 확인**: 수정을 되돌리면 타깃 3개 +
>     퍼즈 8시드가 실패하고, 열 케이스는 자기 호출부만 되돌려야 실패한다(처음 쓴 열 테스트는 병합 범위
>     *밖*에 삽입해 수정 없이도 통과했다 — 아무것도 안 지키던 것을 조건 수정).
>   - ❌ **오진 1건, 보고 전에 자체 기각**: 병합/병합해제 메뉴가 `InvalidateVisual()`만 부르는 것이
>     라운드10 BUG-08(스크롤 extent 정체)과 같은 형제로 보였으나, `FocusCell` → `ResetCaretBlink()` →
>     `NotifyStatus()` → `InvalidateMeasure()`로 이미 커버된다. 라운드10에서 외부 감사가 8건 오진한 것이
>     정확히 이 체인을 놓쳐서였다.
>   > **교훈**: 불변식을 늘리는 것과 그 불변식이 실행되게 하는 것은 다른 일이다. 이번에 실행되지 않고
>   > 있던 것은 **바로 직전 라운드가 그 교훈을 적으면서 추가한 단정**이었다.
>   - **두 번째 축 · 서식**: 퍼즈 생성기에 문자 서식(굵게/기울임/밑줄/취소선·크기·글꼴·색·형광펜·
>     하이퍼링크)과 찾기/바꾸기를 넣었다. 20 시드 그린 — **소득 없음**. 예상된 결과다: 퍼즈 불변식은
>     **구조만** 보는데 서식은 내용 층이라 구조 단정에 걸리지 않는다. 이 축은 아래 고정점 축과
>     결합돼야 실제로 검증된다.
>   - **세 번째 축 · 반복 왕복(고정점)** — `FormatFixpointTests` 신설. 기존 왕복 테스트는 전부 **1회**
>     저장·열기라 **누적형 결함을 구조적으로 못 본다.** 이 리포는 그 계열을 이미 두 번 겪었고
>     (라운드8 "빈 문단이 왕복마다 증식", 라운드9 "4회 재읽기하다 `PageSize` 소실") **둘 다 사람이 손으로**
>     찾았으며 가드가 없었다.
>     계약은 좁게 잡았다 — **1회차는 포맷이 표현 못 하는 걸 잃어도 되지만, 2회차는 더 잃을 게 없다.**
>     그래서 1회차를 기준선으로 2~4회차가 정확히 같아야 한다. 비교 오라클은 `DocumentSerializer.Serialize`
>     (손으로 적은 필드 목록은 라운드12가 보여준 대로 잊힌다).
>     **포맷 간 순환(json→html→rtf→…)은 일부러 안 한다** — 네 포맷의 의도된 손실이 겹쳐서 차이가
>     결함인지 설계인지 귀속 불가능해진다.
>     문서 13종 × 포맷 4종 = 52케이스. **48 통과, 4개가 실패하며 그 4개는 skip으로 표시**해 두었다.
>   - ⚠️ **미해결 관찰 3건 (수정 안 함, 기전 미확인)** — 아래는 결함이라 단정하지 않는다. 하네스 오염을
>     고친 뒤 **재확인**했으므로 설정 탓은 아니지만, 죽는 이유를 확인하지 못했다.
>     ⓘ `html`/`plain` — 소프트 개행(`\n`) 하나 든 평범한 두 문단이 **반복 왕복에서 프로세스를 죽인다**
>     (StackOverflow/OOM은 잡히지 않아 테스트 호스트가 통째로 죽고 로그도 안 남는다). 라운드10이
>     `<br/>` 내보내기를 되살린 뒤 **두 번 통과시킨 첫 테스트가 이것**이다.
>     ⓙ `html`/`kitchen-sink` — 2회차에 **654자 손실**(12,027 → 11,373). 차이는 `Run` 서식 속성 쪽.
>     ⓚ `rtf`/`nested-table`·`kitchen-sink` — **중첩 표**가 든 두 문서만 반복 왕복에서 프로세스 사망.
>     의심: 왕복마다 `\itap` 깊이가 한 단계씩 늘어 무한히 깊어진다(라운드9가 "중첩 표를 쓴 셀은 닫기 전에
>     자기 `\itap`을 다시 선언해야 한다"고 적어둔 그 지점).
>     → **다음 세션 최우선.** 재현은 `ARE_FIXPOINT_DOC=<이름>`으로 한 케이스만 돌리면 된다
>     (프로세스가 죽으므로 전체 실행은 리포트를 통째로 잃는다).
>     → **라운드15에서 3건 모두 기전 확인 + 수정. 아래 라운드15 절 참고**(의심했던 `\itap` 무한 깊이는
>     틀렸고, 죽는 이유도 우리 코드가 아니었다).
>   - ❌ **오진 2건, 둘 다 하네스가 원인이었고 보고 전에 자체 기각**:
>     ① 병합/병합해제 메뉴가 `InvalidateVisual()`만 부르는 것 → `FocusCell`→`ResetCaretBlink()`→
>     `NotifyStatus()`→`InvalidateMeasure()`로 이미 커버(라운드10 외부 감사 8건 오진과 같은 함정).
>     ② "RTF가 가로 용지를 세로로 되돌린다" → **테스트 헬퍼가 문서를 얹은 뒤 `PageSize`를 덮어써서**
>     `CapturePageSetupToDocument`가 문서의 용지를 지운 것. 고치자 통과.
>   > **세션 자체의 교훈**: 이번에 만든 하네스가 **세 번** 가짜 결과를 냈다(Parent 배선 요구 / 테스트
>   > 프로세스를 죽여 만든 가짜 멈춤 / `PageSize` 오염). 새 축을 열 때는 **축이 뭘 잡기 전에 하네스가
>   > 맞는지부터** 확인할 것. 그리고 안 끝나는 테스트는 조건을 바꿔 다시 돌리지 말고 **한 번 계측**할 것.

> - **라운드15 · 라운드14가 남긴 미해결 3건(2026-09-08)** — 셋 다 기전을 확인했고, **두 건은 우리 결함,
>   한 건은 Avalonia 12.0.1 자체**였다. 고정점 52케이스 전부 그린(skip 0).
>   - **재현을 테스트 밖으로 꺼낸 것이 전부였다.** 죽는 케이스는 테스트 호스트가 리포트째 사라져 아무것도
>     못 본다. 라이브러리를 참조하는 **콘솔 앱 하나**(스크래치)에서 같은 왕복을 돌리자 `Console` 로그가
>     남았고, 매달린 프로세스에 `dotnet-stack`을 붙여 **첫 시도에 스택을 얻었다**. 라운드14가 "OOM은 못
>     잡는다"에서 멈춘 지점이 여기였다.
>   - ⓘⓚ **죽는 이유는 `\itap`이 아니라 `TextLayout`이었다.** 스택은 매번
>     `TextFormatterImpl.PerformTextWrapping` → `CreateEmptyTextLine` 루프였고 워킹셋이 초당 수백 MB로
>     늘었다. 우리 코드를 한 줄도 안 쓰는 순수 호출로 좁혔다 —
>     `new TextLayout("a\n\nb", …, textWrapping: Wrap, maxWidth: 700)`가 **끝나지 않는다**.
>     `NoWrap`이면 정상, 12.0.2·12.0.5도 동일, **12.1.2에서 고쳐져 있다**. 즉 **줄바꿈 있는 빈 줄 +
>     줄바꿈 랩**이 12.0.x 엔진 버그다.
>     > ⚠️ **이건 왕복 얘기가 아니다.** Shift+Enter를 **연속 두 번** 누르면 문단 텍스트가 `"…\n\n…"`이 되고
>     > 같은 루프에 빠진다 — 타이핑만으로 닿는 프리즈/OOM이다. 우회로가 없다(랩을 끄거나 줄마다
>     > `TextLayout`을 따로 만드는 것뿐인데, 후자는 "단일 `TextLayout`" 규칙을 깬다). **의존성 상향이
>     > 유일한 수정이며, 판단 대기 중**(아래 참고).
>   - ⓚ **RTF: 중첩 표 앞의 `\par`를 내용으로 읽었다(우리 결함).** 쓰기 쪽은 셀의 문단을 닫으려고
>     `\par`를 넣는데(`CloseBeforeNested`), 읽기 쪽은 셀 안 `\par`를 소프트 개행으로 바꾼다 — 그런데 그
>     문단은 뒤따르는 `\itap`이 어차피 닫는다. 그래서 **왕복마다 셀에 빈 줄이 하나씩 늘었다**(모델 +310자/회,
>     무한). `\par`가 방금 넣은 개행만 위치(바이트 수) 표시로 식별해 버린다 — 작성자가 친 소프트 개행은
>     `\line`으로 오므로 살아남는다.
>   - ⓙ **HTML: 654자는 "손실"이 아니라 런 조각화였다(우리 결함).** 잃은 글자는 없고 `Run` **목록**이
>     달랐다 — 파서는 소스 노드마다 런을 만들고(`&nbsp;`, 평탄화된 표 텍스트, 서식 없는 `<a>`) 내보내기는
>     그것들을 도로 하나로 붙인다. 그래서 **몇 번 들어왔다 나갔느냐에 따라 런 목록이 달라졌다.**
>     `TextRange.CoalesceAll`(기존 `CoalesceRuns`의 문서 전체 판)을 **HTML·RTF 파서 끝에** 건다.
>     RTF 쪽도 같은 조각화가 있었고(`"r1c1"` + `"\n"` + `"second para in cell"`), 그 `"\n"`만 든 런이
>     바로 ⓘ의 엔진 버그를 밟는 형태여서 **호스팅 시 죽던 마지막 케이스**였다.
>   - ✅ **Avalonia 12.0.1 → 12.1.2 상향 완료**(사용자 승인 2026-09-08). 12.0.x에 남을 수 없는 이유는
>     "왕복 버그"가 아니라 **타이핑으로 닿는 프리즈**였고, 코드 우회로가 규칙 위반뿐이라 의존성을 옮겼다.
>     - csproj 4개(라이브러리·데모·유닛·렌더). **패키지 의존성 하한이 12.1로 올라간다** — 12.0.x에
>       고정된 앱은 따라 올라와야 한다(CHANGELOG 상단 경고).
>     - 폐기 경고 9건: `Bitmap.Save(stream)` → `Save(stream, PngBitmapEncoderOptions.Default)`
>       (전부 PNG 저장이라 의미 동일). **0 warn 복구.**
>     - `Round2bTests`의 열 드래그 2건을 `InteractionHost`로 이전. 그 둘은 루팅 없이 `RaiseEvent` + 수제
>       `Pointer`를 쓰던 마지막 잔재고, 12.1에서 **아무것도 리사이즈하지 않으면서 조용히 통과/실패**했다.
>       같은 드래그를 실제 `TopLevel`로 보내면 12.1.2에서 정상(100 → 130, `IsModified` true) — 즉 그
>       테스트가 검증하던 건 제품이 아니라 자기 자신이었다.
>     - 회귀 가드: `SoftBreakLayoutTests` 2건(빈 소프트 줄 / `"\n"`만 든 런) + 고정점 문서에
>       `soft-breaks` 추가(4포맷 × 1 = 4케이스). ⚠️ **이 가드는 실패하지 않고 매달린다** — Avalonia가
>       뒤로 가면 스위트가 안 끝나는 것으로 나타난다(파일 주석에 명시).
>     - 결과: 빌드 **0 warn / 0 error**, 유닛 **842** 그린, 렌더 **25** 그린.
>     - **GUI 육안 검증(사용자, 2026-09-08)**: Shift+Enter 2회 정상, 표 경계 드래그 정상, 페이지 뷰
>       전환 정상 — **12.1 상향으로 인한 회귀는 발견되지 않았다.** 대신 **IME 별건 발견** ↓
>   - **IME 조합 글자가 항상 본문 크기로 그려졌다**(상향과 무관한 기존 결함, 데모 육안에서 발견).
>     `BuildTextLayout`의 preedit 속성이 `Typeface.Default` + `DefaultFontSize` **하드코딩**이라, 제목
>     문단에서 조합하면 작게 보이다가 **확정되는 순간 제 크기로 튀었다**(자체 크기·글꼴·색을 가진 런도 동일).
>     조합 글자가 곧 합쳐질 런(`TryInsertTextCore`가 늘리는 그 런)의 속성을 쓰도록 바꿨다 —
>     밑줄은 유지, 런이 없는 빈 문단은 제목 크기를 아는 폴백.
>     수정 후 데모에서 **육안 확인 완료**(사용자, 2026-09-08): 제목 줄 안에서 조합해도 크기 변화 없음.
>     > **테스트를 두 번 썼다.** 첫 판은 제목 문단의 **총 높이**를 본문과 비교했는데, 제목은 조합 전에도
>     > 더 크므로 **결함이 있어도 통과했다**. 지금은 preedit이 붙을 때 문단이 **늘어난 높이**를 본다
>     > (옛 코드에서는 제목이든 28pt든 29px로 동일 — 그게 결함의 지문이다). 반증 확인 완료.
>   > **교훈**: 라운드14는 "잡을 수 없는 크래시"라 멈췄지만, 잡을 수 없는 건 **테스트 호스트 안에서**일
>   > 뿐이다. 프로세스를 우리가 소유하면 로그도 스택도 남는다. 그리고 오래 의심하던 자리(`\itap`)는
>   > 셋 중 어느 것의 원인도 아니었다.

> - **라운드14 · 이미지 대체 텍스트(2026-09-09, 포트 백포트)** — 이 에디터는 대체 텍스트를 **담을 수가
>   없었다**: `<img alt="…">`가 들어오면 **가져오기에서 사라졌고** 내보내는 쪽도 쓰지 않았다. 포트는 1.0부터
>   갖고 있었다. 문서 포맷이 실제로 보존할 수 있는 접근성 정보라 무손실 포맷 전부에서 살아남게 했다.
>   - `ImageBlock.AltText` / `InlineImage.AltText`(+ `Clone`), JSON/`.flow`는 **`Alt`** — **포트와 같은
>     와이어 이름**이다(둘이 같은 `.flow`를 쓴다). null이면 생략하므로 기존 파일은 바이트 동일.
>   - HTML은 표준 `alt` 속성으로 쓰고 읽는다(엔티티 디코드). **`alt=""`는 "장식용"이라는 뜻**이므로
>     빈 문자열이 아니라 **설명 없음**으로 들어온다. RTF는 자리가 없어 종전대로(포트와 동일).
>   - 편집: 두 이미지 메뉴(블록·인라인)에 "Alt Text..." 항목, en/ko 현지화, 읽기 전용 제외, 언두 1회.
>   - `ImageAltTextTests` 10개. 반증: clone·JSON 쓰기·HTML 쓰기·HTML 읽기에서 각각 빼면 **7개가 빨개진다**.
>   > ⚠️ **테스트가 두 가지를 대가로 배웠다.** ① `<img>`를 **읽는** 것은 디코드라 `[AvaloniaFact]`여야 한다 —
>   > 평범한 `[Fact]`에서는 디코드가 던지고 `LoadImage`가 삼켜 **그림이 아예 안 들어오는데**, 그 실패가
>   > alt 결함처럼 보인다. ② 임포터는 **64px 미만을 인라인 아이콘**으로 만든다 — "블록 이미지" 픽스처는
>   > 그보다 커야 하고, 아니면 alt와 무관한 이유로 인라인으로 돌아온다.

> - **라운드15 · 셀 세로 정렬(2026-09-09, 포트 백포트)** — 셀 내용이 늘 위에 붙어 있었고 바꿀 수단이
>   없었다. `TableCell.VerticalAlignment`(Top/Center/Bottom) + 우클릭 ▸ 셀 세로 정렬, JSON/`.flow`(`VAlign`)
>   ·HTML(`valign`)·RTF(`\clvertalc`/`\clvertalb`) 왕복 — **와이어 표기는 포트와 동일**하다.
>   - 속성 자체는 작다. 큰 것은 **셀 내용을 놓는 걷기가 여덟 개**라는 점이다(그리기·히트테스트 2종·링크
>     조회 × 최상위/중첩/인라인). 전부 `rect.Y + 5`를 손으로 갖고 있었고, 이제 **하나의
>     `CellContentOffsetY`** 를 공유한다 — 그리기만 오프셋을 쓰면 텍스트는 아래에 그려지고 클릭은 위에서
>     받는다(R1·R2가 정리한 "같은 걷기의 사본"과 같은 계열).
>   - 표 레이아웃 캐시는 건드리지 않았다: 캐시가 담는 것은 **셀 사각형**이고 오프셋은 그리기·히트테스트
>     시점에 사각형에서 유도되므로 캐시가 낡지 않는다.
>   - `CellVerticalAlignmentTests` 22케이스. 반증: 히트테스트 **한 곳**에서 오프셋을 빼거나 RTF/JSON
>     쓰기에서 정렬을 빼면 해당 테스트만 빨개진다.
>   > ⚠️ **문단 동일성으로는 이걸 시험할 수 없다 — 첫 판이 그걸 증명했다.** 문단 하나짜리 셀은 어디를
>   > 클릭해도 폴백으로 그 문단이 나와서, 위/아래 정렬이 **동일한 띠(26..234)** 를 보고했고 실패할 수가
>   > 없었다. 셀에 문단을 **둘** 넣고 그 경계가 어디로 가는지를 재도록 바꿨다. 캐럿 라운드의
>   > `AdjacentTopLevelParagraph` 함정과 같은 계열 — 폴백이 **틀린 이유로 맞는 답**을 준다.

> - **라운드16 · 캐럿 affinity(2026-09-09, 포트 백포트)** — 소프트 랩에서 "줄 k의 끝"과 "줄 k+1의 머리"는
>   **같은 오프셋**이고 레이아웃은 늘 앞쪽을 답한다. 공백에서 감기면 End가 공백을 잘라 우회되지만,
>   **단어 중간에서 감기면** 잘라낼 것이 없다 — 실측: 문자 400개 문단에서 End가 캐럿을
>   **y=116.3,x=10 → y=130.8,x=10**(한 줄 아래, 왼쪽 여백)으로 보냈다. 지금은 **y=116.3,x=570**이다.
>   - `TextPointer.AtLineEnd`(포트와 같은 이름·의미). **표시 전용** — `Equals`/`CompareTo`가 무시하므로
>     선택·정렬은 그대로다. affinity가 서면 캐럿 사각형을 **앞 글자의 뒤쪽 모서리**에서 취한다.
>   - ⚠️ **세로 이동이 별도 결함을 갖고 있었고 테스트가 그걸 잡았다.** 감긴 줄의 오른쪽 끝을 히트테스트하면
>     **다음 줄의 첫 오프셋**이 나온다(같은 모호성, 히트테스트 쪽). 그래서 ↑가 원래 줄로 한 글자 들어간
>     자리에 착지해 캐럿이 **제자리**였다(idx 337, y 116.3 → 116.3). 목표 줄로 클램프하고 거기서 affinity를 세운다.
>   - `CaretLineAffinityTests` 8개, 876 → 884. 반증 3종(기하 무시 / End가 안 세움 / 클램프 제거) 각각
>     해당 테스트만 빨개진다.
>   - **클릭 경로도 포함**(같은 라운드에서 이어서). 감긴 줄 오른쪽 바깥을 클릭하면 다음 줄의 첫
>     오프셋이 나왔다 — 실측 off=379(줄은 378에서 끝난다), 즉 아래 줄로 한 글자. `HitTestIndex`가
>     **클릭한 줄로 클램프**하고 거기서 affinity를 세운다(캐럿 x=570, 아래 줄 x=23이 아니라).
>   > **affinity는 클램프가 세운다 — 뒤쪽 절반 클릭마다가 아니다**(포트는 후자다). 실측: trailing 플래그를
>   > 0으로 만들어도 **어떤 테스트도 변하지 않았다** — 경계에 닿는 클릭은 결국 클램프가 처리하기 때문이다.
>   > 관측 불가능한 플래그는 기능이 아니라 부채고, 하드 개행에 가드가 필요해진 이유가 정확히 그것이다.
>   > 좁힌 규칙은 반증으로 확인했다(클램프에서 affinity를 빼면 클릭 테스트가 빨개진다).
>   > ⚠️ **하드 개행은 클램프가 필요 없고 받지도 않는다.** 실측: `abc<br/>def`에서 `abc` 오른쪽을 클릭하면
>   > 이미 오프셋 3(개행 앞)에 선다 — 레이아웃의 히트테스트가 개행을 넘지 않는다. 캐럿 사각형의 가드는
>   > `AtLineEnd`가 **공개 속성**이라 남긴다(호스트가 개행 직후에도 세울 수 있다).

> - **라운드17 · 포트 4라운드 백포트 묶음(2026-09-12)** — 포트가 "관찰이 0인 공개 멤버" 기준으로 돈 네 축(편집 명령 ·
>   기능 플래그 · 문서 API 래퍼 · 하이퍼링크)에서 **"상류에도 같은 코드"로 대조해 둔 항목**을 옮겼다. 옮기기 전에
>   상류 `main`부터 봤다 — Word 방식 토글과 병합 셀 삽입 위치는 **이미 `c90cda5`로 들어와 있어** 제외했다. 테스트
>   **889 → 915**(`PortBackportBundleTests` 26), 빌드 0 warn. 상세는 `CHANGELOG.md` Unreleased.
>   - 옮긴 것 8건: 캐럿 서식의 크기 보고(`DrawnRunSize` — 그린 크기를 보고) · 문서 교체 시 서식 복사 해제 ·
>     Tab 자동 링크 · 괄호 짝(`TrimUrlTail`) · `LoadJson`/`LoadJsonAsync`/`LoadPackageAsync`의 형태 검사 ·
>     `document.json` 없는 zip 거부 · 페이지 설정 호스트 기본값 · 붙여넣기·`InsertHtml`의 `AllowImages`/`AllowTables` 적응.
>   - ⚠️ **상류 테스트 하나를 뒤집었다** — `FlowPackage_ZipWithoutDocument_ReturnsAnEmptyDocument`("손상 아님, 빈
>     문서")를 `_Throws`로. 바로 위의 1.0 동결 계약("손상 문서를 빈 문서로 읽지 않는다")과 같은 손실을 근거 없이
>     예외로 둔 조항이었고, 포트에서 사용자가 뒤집기로 결정한 바로 그 동작이다.
>   - 상류 모양에 맞춘 차이: 비동기 로더는 모델을 계속 **UI 스레드**에서 만든다(규칙 #8) — 형태 검사는 파싱 직후,
>     `FromDto` 전에. 붙여넣기 적응은 외부 경로가 이미 `InsertParsedDocument` 한 곳을 지나 거기서 한다(포트는 드래그가
>     같은 삽입부를 써서 네 지점에 따로 뒀다). 앱 내부 붙여넣기는 **복제본**에 적응한다 — 보관된 클립보드 목록은 그대로.
>   - ⚠️ **공허한 테스트 하나를 반증 전에 잡았다** — 그림 제거 이론을 `[AvaloniaTheory]`가 아닌 `[Theory]`로 썼더니
>     헤드리스 디스패처 밖이라 파서가 그림을 **만들지 못했고**, 그림 없는 입력으로 통과하고 있었다. 같은 원인으로 "빈
>     결과면 언두 없음"은 빨갛게 나왔다(원래 내용이 없다고 판정). 속성을 고친 뒤 반증 **18종 전부** 의도한 테스트만 죽었다.
>   - 알려진 공백: 이 스위트엔 실제 클립보드가 없어(`RichEditorClipboardTests` 머리말) 붙여넣기 세 지점(앱 내부·RTF·
>     HTML)의 **배선**은 검증 밖이다 — 적응 로직은 `InsertHtml`로 검증한다. 포트는 실제 클립보드로 지점별 테스트를 둔다.
>   - ⚠️ **두 리포가 갈라진 곳 하나**: 제목 적용 시 run 크기 — 상류는 `c90cda5`에서 **본문 기본값으로 초기화**하기로
>     했고, 포트는 사용자 결정으로 **직접 지정한 크기를 유지**한다. 이 묶음은 이 차이를 건드리지 않았다.

> - **라운드18 · 제목 속 캐럿 크기(2026-09-12)** — 포트가 제목 속 캐럿에서 결함을 고친 뒤(직접 지정한 크기를 무시하고
>   제목 크기를 씀) 상류를 **측정**했더니 반대 방향 결함이 있었다. `CaretTextHeight`가 제목을 보지 않고 저장된 run
>   크기만 써서, 10pt로 저장되고 제목 크기로 그려지는 무서식 run에 **10pt 캐럿**이 섰다. 테스트 **915 → 921**
>   (`CaretHeadingSizeTests` 6).
>   - 실측(수정 전): H1 무서식 캐럿 19.0 — 본문 20pt 글자는 29.1. H2도 19.0(본문 16pt 23.3). 직접 지정한 크기는 원래 맞았다.
>   - 본문에서 안 보였던 이유: 1.4em 캐럿이 줄 상자보다 커서 **줄 상자로 잘린다**(본문 10pt도 19가 아니라 14.5).
>     제목 무서식에서만 계산값이 줄 상자보다 작아 결함이 드러났다.
>   - 수정: `CaretTextHeight`가 렌더러 규칙 `DrawnRunSize`를 쓴다(라운드17에서 캐럿 서식에 들어온 헬퍼).
>   - 반증 2종: raw 크기로 되돌리기 → 무서식 4케이스만 실패. 제목이면 늘 제목 크기 → 명시 크기 케이스만 실패.
>   - ⚠️ **공허한 케이스 하나를 반증이 잡았다** — 명시 12pt 케이스는 두 교란 모두에서 통과했다. 줄 상자 클램프가
>     "너무 큰 캐럿"을 원래 높이로 되돌리므로, 제목보다 **작은** 크기로는 판별이 안 된다. 28pt로 바꿨다.

> - **라운드19 · 캐럿 서식 보고(2026-09-12, 포트 백포트)** — 포트가 "캐럿 보고 = 거기서 친 글자의 서식 = 화면"을
>   오라클로 결함을 찾은 뒤(포트 PR #22) 상류를 **측정**했더니 같은 뿌리가 있었다. 테스트 **921 → 943**
>   (`CaretFormatReportTests` 21 + `CaretHeadingSizeTests`에 이미지 앞 캐럿 1).
>   - 실측(수정 전): 이미지 옆 **6개 위치**에서 보고 ≠ 타이핑(포트는 4) — 보고는 문단 **첫** run으로 폴백하고, 타이핑은
>     서식 없는 새 run을 쓰거나 이미지 뒤 run에 합류했다(`굵게 [img]|기울임`: 툴바 굵게, 타이핑 기울임·14pt·빨강) ·
>     굵게 그려지는 제목이 "굵게 아님", Ctrl+B가 숨은 굵기만 뒤집음 · 링크 **끝과 시작** 타이핑이 링크를 늘림.
>   - 수정: 포트와 같은 `TypingSource` 한 규칙(캐럿 앞의 가장 가까운 글자, 이미지 건너뜀, 없으면 뒤)을 삽입·보고·
>     캐럿 높이·서식 복사가 공유 · 굵게는 `DrawnBold`로 렌더러와 공유 · 대상이 전부 강제 서식이면 토글은 변경도 언두도
>     없음 · 링크는 안에서만 이어짐 · (비공개) `ClearFormatting`이 제목에서 "서식 없음" 크기를 쓴다. 동작 결정 3건은
>     포트에서 사용자가 정한 Word 방식 그대로다.
>   - 상류 모양에 맞춘 차이: 링크는 여기서 파랗게 칠하지 않으므로 색 보고는 옮기지 않았다. 링크 밑줄은 **다른 장식이
>     없을 때만** 렌더러가 강제하므로(취소선 링크는 밑줄이 없다) 강제 판정도 그 조건이다(`CtrlU_OnAStruckLink_Underlines`).
>     토글 판단 입력을 포트처럼 "명령이 스타일할 글자"로 모았다 — 캐럿 단어는 그 단어의 run이, 빈 캐럿은 타이핑 서식이
>     판단한다(전엔 둘 다 캐럿 서식). `CurrentLinkUri`는 상류에 없다.
>   - **반증 15종 — 전부 의도한 테스트만 실패**: 삽입·보고 각각 `TypingSource` 무시 · 캐럿 높이 옛 조회 · 제목 굵게
>     run 보고 · 굵게/밑줄 강제 판정 제거(각각) · 밑줄 강제가 다른 장식 무시 · 링크 항상 연장 · 링크 시작에서 합류 ·
>     `ClearFormatting`이 `DefaultFontSize` · 미리보기 복제본 부모 없음 · 쪼개진 링크 이음매 무시 · "하나라도 강제면
>     멈춤" · 캐럿 단어를 타이핑 서식으로 판단 · `DrawnBold`에서 제목 조건 제거.
>   - ⚠️ 포트에 없던 테스트 하나를 더했다: `CtrlU_AtALinksEnd_LeavesTheLinkAlone` — 링크 끝의 캐럿은 링크 **단어** 안이라
>     Ctrl+U가 그 단어를 스타일하는데, 타이핑 서식(거기선 보통 글자)으로 판단하면 링크에 숨은 밑줄 플래그가 선다. 이
>     교란(U14)을 잡는 테스트가 없었다. 포트는 같은 동작이지만 같은 테스트가 없다 — 포트로 되돌려 줄 것.

> - **라운드20 · 벡터 PDF 내보내기(2026-09-12)** — `SavePdf`는 페이지마다 RGB 그림 한 장이었다(글자 선택·검색 불가).
>   포트가 인쇄를 벡터로 바꾸는 과정에서 "PDF도 제대로"가 나왔고, 상류는 여러 OS용이라 Windows PDF 프린터를 쓸 수 없어
>   **Skia PDF**로 간다(포트는 별도로 Windows "Microsoft Print to PDF" 경로 — 사용자 결정).
>   - **프로브 먼저**: Avalonia.Skia 12의 공개 다리는 `DrawingContextHelper.RenderAsync(SKCanvas, Visual, …)`(Visual을 받는다,
>     콜백 아님) — 그래서 한 페이지를 그리는 `PrintPageVisual`을 두고, 페이지 그리기는 `RenderPrintPage`에서 `DrawPrintPage`로
>     떼어 래스터·벡터가 같은 것을 그린다. 측정: `RenderAsync`는 동기 완료(동기 `SavePdf`에서 교착 없음), 글자는 글자로
>     (Type0 폰트 + ToUnicode, 그림 0개) — 단 **7.7MB**. SkiaSharp 네이티브에 서브세터가 없어 폰트가 통째(맑은 고딕 7.45MB).
>   - **글꼴 추리기**: Avalonia.Skia가 이미 싣는 HarfBuzz 네이티브가 `hb_subset`을 내보낸다(관리 바인딩은 없음 → P/Invoke).
>     `PdfFontSubsetter`가 Skia 출력(클래식 xref 한 구간, 직접 길이, Flate)만 다룬다 — 쓰인 글리프를 모아 `RETAIN_GIDS`로
>     추리고 스트림·길이·xref만 다시 쓴다. 모르는 모양은 그대로 둔다. 같은 페이지 **17.9KB**.
>   - 쓰인 글리프는 **콘텐츠 스트림의 표시 코드(정답)와 ToUnicode의 합집합**이다 — 둘은 서로를 받친다(한쪽만 읽어도
>     윤곽 보존 테스트가 통과했다). **`/W`는 쓰지 않는다**: 글리프 목록처럼 보이지만 Skia는 기본 너비(`/DW`)와 같은
>     글리프를 뺀다(맑은 고딕의 한글은 전부 1000이라 하나도 없었다) **그리고** 같은 너비 구간을 `c1 c2 w`로 묶어 쓰이지
>     않은 글리프까지 덮는다 — 첫 판은 `/W`도 읽었고, 교란으로 빼 보니 Inter 윤곽이 72 → 12, Bold 30 → 4였다.
>   - `NO_LAYOUT_CLOSURE`: 기본값은 GSUB로 닿는 글리프(합자·대체형)까지 넣는다 — 페이지 내용은 이미 모양 잡힌 글리프라 불필요.
>   - ⚠️ 테스트 파서가 틀렸던 것 하나: ToUnicode를 한 패턴으로 읽어 `bfchar` 두 쌍을 `bfrange`로 오독(한 글자 누락) —
>     제품 파서는 블록을 나눠 읽는다. 테스트도 그렇게.
>   - 대체 경로: 벡터 경로를 쓸 수 없으면(헤드리스 드로잉 등 Skia 백엔드 없음) 예전 래스터 PDF.
>   - 테스트 `VectorPdfTests`(렌더 프로젝트, 실제 Skia) 5 — 글자 검색 가능 · 추리기(글리프 수 유지, 윤곽은 소수) · 쪽 수 ·
>     모르는 입력 무변경 · **보이는 글리프가 모두 윤곽을 유지**(추리기 전과 글꼴별 비교 — 빠진 윤곽은 빈칸으로 인쇄된다).
>   - ⚠️ **실기에서 드러난 결함 — 내용이 종이를 벗어났다**(사용자, 저장한 PDF). DIP → 포인트(72/96) 환산을 다리의 dpi
>     인자에 맡겼는데 그 인자는 그리기를 **축척하지 않는다** — 96DIP/인치로 72pt 페이지에 그려 줄이 오른쪽으로 1/3 넘쳤다.
>     프로브는 "글자가 글자로 들어가는가"만 봤고 위치는 안 봤다. 환산을 `PrintPageVisual`이 스스로 건다(`Scale`), 한 페이지
>     그리기를 `DrawVectorPage`로 떼어 테스트가 같은 경로로 그린다. `TheVectorPage_FitsThePaper` — A4 포인트 크기 표면에
>     그린 잉크가 같은 편집기의 72dpi 래스터 페이지와 같은 자리(±2px)에 있어야 한다(왼쪽 가장자리는 여백 + 내용 들여쓰기라
>     공식은 틀린 오라클 — 첫 판이 그렇게 틀렸다). 반증: 축척 1로 되돌리면 잉크가 x=593까지(오른쪽 여백 559).
>   - **결과**: 메인 **943/943**, 렌더 **25 → 31/31**(`VectorPdfTests` 6). 반증 8종 — 벡터 경로 끄기 · 서브세터 무동작 · xref 옛 오프셋 ·
>     글리프 출처 전부 제거 · `RETAIN_GIDS` 제거 — 전부 의도한 테스트만 실패(출처는 한쪽씩 빼면 안 죽는다: 설계대로 서로를 받친다).
>   - ⚠️ 기존 테스트 하나를 고쳤다 — `SavePdf_WritesParseableMultiPagePdf`는 래스터 작성기의 **서식**을 고정하고 있었다
>     (`"/Type /Page "` 뒤 공백, `/Filter /FlateDecode`). 메인 프로젝트(헤드리스 드로잉)에서도 벡터 경로가 돌아 Skia가
>     `/Page` 뒤에 줄바꿈을 쓰고 작은 콘텐츠를 압축하지 않는다. 의도("쪽 수가 맞는 파싱 가능한 PDF")만 남겼다.
>     헤드리스 드로잉에선 예전 래스터도 빈 그림이었으므로 회귀가 아니다.

> - **라운드21 · 뷰어 표 복사(2026-09-13, 포트 백포트)** — 포트 실기에서 나왔다: 읽기 전용에서 표를 우클릭하면 "복사"가
>   선택 없음으로 흐렸고, 뷰어에서 표를 가져갈 길이 없었다. 포트는 표를 **개체로** 선택해(선택 테두리) 복사한다. 상류엔 표를
>   개체로 고르는 개념 대신 셀 선택 모드가 있어, **표 전체 선택**(Ctrl+A 단계와 같은 셀 채움)으로 옮겼다.
>   - 뷰어 우클릭: 선택이 없고 표 위(격자 밖으로 걸친 경계 띠 포함)면 표 전체 선택 → 복사가 켜지고, 채움이 복사 대상을 보여 준다.
>   - 뷰어 경계 클릭: 블록 캐럿(들여쓰기·삭제용 — 뷰어엔 할 일이 없다) 대신 표 전체 선택, Ctrl+C로 복사. 이동 커서는 원래
>     뷰어에서도 떴다. 편집 모드는 그대로 블록 캐럿(테스트로 대조).
>   - **표 전체 선택의 복사는 그 표**(`SelectedWholeTable`): 블록 캡처가 최상위 블록을 복사해 **중첩 표는 바깥 표**로, **1×1
>     표는 글자**로 나갔다 — 편집 모드의 Ctrl+A 단계 뒤 복사도 같았다. 1×1 표는 표 단계가 따로 없어 셀 블록이 곧 표 전체다.
>   - 테스트 `ViewerTableCopyTests` 6(뷰어 우클릭 2×2·1×1 · 글자 우클릭 대조 · 중첩 표 복사 · 경계 클릭 뷰어/편집).
>     메인 943 → 949(첫 커밋). 반증 4종 — 전체 표 판정 끔 · 우클릭 선택 제거 · 경계 클릭 선택 제거 · 1셀 대체 제거 — 전부
>     의도한 테스트만 실패.
>   - **편집 모드도**(실기에서 사용자가 발견): 경계 클릭의 **블록 캐럿**(Del로 표 삭제·Space로 들여쓰기 — 표를 한 덩어리로
>     쥔 상태)이 쥔 표를 복사·Ctrl+C가 가져간다. 텍스트 선택이 없어 복사할 게 없었다. 경계 **우클릭**은 클릭과 같게(블록 캐럿 →
>     표 메뉴, 복사 켜짐) — 글자 메뉴가 뜨거나 표 메뉴의 복사가 흐렸다. 경계 띠는 격자 밖으로 걸쳐 있어 경계를 먼저 찾는다.
>     잘라내기는 그대로(Del·"표 삭제"가 지운다).
>   - ⚠️ 테스트 함정 둘: ① 내부 클립보드가 **static**이라 앞 테스트가 남긴 표로 복사 없이도 통과할 수 있었다 — 복사 전에 비운다.
>     ② 격자 **안** 왼쪽 경계를 우클릭하면 경계 우선 판정을 빼도 표가 잡혀 그 반증이 죽지 않았다 — 격자 **밖** 윗 경계 띠로.
>   - 데모에 "읽기 전용" 체크박스 — 뷰에 전환이 없어 뷰어 동작을 데모에서 볼 수 없었다.
>   - 테스트 `ViewerTableCopyTests` 8, 메인 **943 → 951/951**. 반증 5종 추가(블록 캐럿 표 복사 제거 · 경계 우클릭 블록 캐럿
>     제거 · 표 메뉴 복사 켜짐 무시 · 경계 우선 판정 제거 · 전체 표 판정 재확인) — 전부 의도한 테스트만 실패.
>   - **인라인 표 경계**(사용자 제안): 이동 커서 · 클릭 = 표 전체 선택(편집·뷰어 — 인라인 표엔 블록 캐럿이 없다) → Ctrl+C ·
>     우클릭 = 표 전체 선택 + 복사(뷰어는 짧은 메뉴, 편집은 표 메뉴 — 그 메뉴의 잘라내기는 셀 블록 규칙대로 셀만 비운다).
>     인라인 표의 문서 위치는 호스트 문단을 그릴 때만 알려져서, 그릴 때 격자 사각형을 `_inlineTableRects`에 기록한다
>     (`InlineTableBorderAtPoint`, 뒤에서부터 찾아 중첩된 안쪽 표가 이긴다).
>   - ⚠️ 테스트 지점 함정: 2×2 표의 세로 **가운데는 행 경계선**이라 편집 모드에선 행 높이 핸들이 먼저 가져갔다(뷰어엔 핸들이
>     없어 통과) — 첫 행 안으로 옮겼다. 그 두 테스트가 교란 없이 빨갛던 동안의 반증 결과는 버리고 다시 돌렸다.
>   - 테스트 `ViewerTableCopyTests` 13, 메인 **951 → 956/956**. 반증 6종(경계 판정 · 커서 · 클릭 · 뷰어 우클릭 · 편집 우클릭 ·
>     사각형 기록) 전부 의도한 테스트만 실패.
>   - **셀 안 표도**(사용자 지적 — "셀 안의 표에는 적용이 안 됐다"): 기록을 `DrawNestedTable`로 옮겨 인라인 표와 셀 안 표를
>     한 번에(`_nestedTableRects`, `NestedTableBorderAtPoint`). 둘 다 그 함수로 그려진다. 띠가 겹치는 곳(셀 여백만큼)은
>     **안쪽 표**가 이긴다 — 클릭·우클릭·뷰어 모두 중첩 판정을 최상위보다 먼저 묻고, 목록은 뒤에서부터 찾는다(표의 셀 속 표가
>     나중에 기록된다). 겹침 테스트는 두 깊이(최상위 표의 셀 속 표 · 중첩 표의 셀 속 표).
>   - 테스트 `ViewerTableCopyTests` 20, 메인 **956 → 963/963**. 반증 7종(중첩 판정 · 커서 · 클릭 · 뷰어 우클릭 · 편집 우클릭 ·
>     기록 · 바깥 우선) 전부 의도한 테스트만 실패. ⚠️ 겹침 테스트 전에는 "바깥 우선" 교란이 살아남았다 — 겹치는 지점을 누르는
>     테스트가 없었다.
>   - **잘라내기·Delete는 표를 지운다**(실기: "셀 안 표를 잘라내도 남는다" → 사용자 결정 "Delete도 표를 지우는 게 낫다"): 표를
>     통째로 잡은 상태(전체 선택 · 블록 캐럿)에서 Ctrl+X · Delete/Backspace · 메뉴 잘라내기/삭제 → `RemoveTableHeldWhole`. 셀 블록
>     규칙(셀만 비움)은 **일부 셀** 블록에만 남는다. 기존 `DeleteBlock`은 되돌리기를 따로 남기고 캐럿을 옮기지 않아 쓰지 않았다
>     (전체 선택의 캐럿은 표의 마지막 셀에 있다). 블록 캐럿의 Ctrl+X는 표를 복사만 하고 지우지 않았다 — 함께 고침.
>     ⚠️ 동작 변경: 편집 모드에서 Ctrl+A 단계로 표 전체를 선택하고 Delete → 이제 표가 지워진다(경계 선택과 같은 상태라 구분이
>     없다). `Delete_OnACellBlock_LeavesTheGridStanding`은 표 전체를 드래그하고 있어서 부분 블록(2×3의 앞 두 열)으로 바꿨다.
>   - ⚠️ 테스트 함정: 메뉴 항목의 Click만 일으키면 메뉴가 열린 채라 다음 Ctrl+Z가 메뉴로 갔다(메뉴 경로 6개가 되돌리기에서 빨갛던
>     원인) — 실제 클릭처럼 메뉴를 닫고 누른다. 그동안의 반증 결과는 버리고 다시 돌렸다.
>   - 테스트 `ViewerTableCopyTests` 32, 메인 **963 → 975/975**. 반증 8종(제거 끔 · 블록 캐럿 대상 제외 · Delete 키 · 메뉴 삭제 ·
>     메뉴 잘라내기 · Ctrl+X · 캐럿 안 옮김 · 메뉴 삭제 흐림) 전부 의도한 테스트만 실패.
>   - **인라인 표는 인라인으로 복사된다**(사용자 결정): 통째로 잡은 표가 인라인 표면 내부 클립보드를 블록 목록 대신 **인라인
>     목록**(그 인라인 표 하나)으로 채워, 붙여넣기가 `InsertInlines`로 캐럿 위치 — 글줄 안 — 에 넣는다. 블록 표로 담겨 붙인 문단을
>     가르던 것. HTML도 인라인 표가 든 한 줄로 만들어 인라인 표시가 실린다. 최상위 표·셀 안 표는 그대로 블록.
>     기존 테스트 여섯(인라인 경우의 복사·잘라내기)이 "블록 목록에 표"를 고정하고 있어 `AssertCopiedTheTable`로 바꿨다(인라인이면
>     인라인 목록). 헤드리스 테스트엔 시스템 클립보드가 없어 붙여넣기 쪽은 `InsertInlines`를 직접 부른다.
>   - 테스트 `ViewerTableCopyTests` 33, 메인 **975 → 976/976**. 반증 1종(인라인 표를 블록으로 복사) — 인라인 경우 7개만 실패.
>   - **표를 셀 안에 붙여넣으면 그 셀에 중첩된다**(사용자 질문 — 포트는 이미 됐다): `InsertBlocksAtCaret`/`BlockInsertTarget`에
>     "중첩 표가 아직 안 그려진다(P4-2b)" 시절의 폴백이 남아, 표가 든 붙여넣기는 캐럿이 셀 안이어도 바깥 표 **뒤**로 갔다. 폴백
>     조건을 걷어냈다(내부 클립보드 · HTML · RTF 모두 이 경로). 그 폴백을 고정하던 `Paste_TableHtml_WithCaretInCell_StaysTopLevel`은
>     `_NestsInTheCell`로 뒤집고 내부 클립보드 경로 1개 추가 — 캐럿을 글자 **중간**에 둬 "x | 표 | y"로 판정(끝에 두면 뒤에 붙이는
>     폴백과 구분이 안 된다). 메인 **976 → 977/977**. 반증 1종(폴백 조건 복원) — 새 테스트 2개만 실패.
>     ⚠️ 반증 복원을 `Copy-Item`으로 했더니 **옛 수정 시각**이 복원돼 증분 빌드가 교란된 DLL을 그대로 썼다(전체 실행에서 2개 빨강).
>     복원 뒤엔 파일 시각을 갱신할 것.
>   - **셀 블록 모델 통일(포트와, 사용자 결정 2026-09-13)**: 셀 선택 **모드 플래그**(`_cellSelMode`/`_cellSelTable`)를 걷어냈다.
>     여러 셀 블록은 선택 두 끝에서 파생(`SelectedCellRange`), **한 셀 블록**은 표지 `_cellBlockMark`(셀 + 그때의
>     `_selectionStart`/`_selectionEnd` **객체** + 1×1 표 전체 여부) — 그 객체가 그대로 선택이고 셀 내용 전체일 때만 유효
>     (`MarkedCell`). 끄는 코드가 필요 없고 같은 범위를 나중에 잡아도 되살아나지 않는다. 렌더러·명령·메뉴가 `CellBlockTable` →
>     `SelectedCellRange` 한 경로로 읽는다.
>     - **측정한 결함(수정)**: Shift+→로 셀을 넘으면 `ApplyCaretSelection`이 모드를 끄는데 렌더러는 블록을 칠해 Delete가 글자를
>       지웠다("a0b|a1b" → "a | 1b"). 이제 두 셀이 비워진다.
>     - **추가**: F5(`TryCellBlockKey`, 뷰어에서도) · Shift+방향키 셀 단위 확장(`ExtendCellBlock` — 앵커 고정, 병합 폭 너머, 앵커
>       복귀 = 한 셀, 가장자리 정지) · 메뉴 "셀 선택"에 F5 표시(`ShortcutId.SelectCell`, 힌트 전용).
>     - **동작 변경**: 셀을 넘는 끌기 뒤 클릭 = 캐럿(붙박이 셀 모드·더블클릭 편집 폐기).
>     - 테스트 977 → 984(Shift+→ 회귀 · F5 · 확장 · 되살아나지 않음 · 뷰어 F5 · 병합 셀 · 끌기 뒤 클릭), 모드 필드를 읽던 4개 파일은
>       `CellBlockTable`로. 불변식 퍼즈에 F5·Shift+방향키(실제 `OnKeyDown`) → 300·2000시드. 반증 11종 — 첫 판에서 병합 폭 교란(U3)이
>       살아남아 병합 셀 테스트를 더했다(포트엔 있었다), 이후 전부 테스트에 걸림.
>   - **메뉴 항목 수렴(포트와, 사용자 결정 2026-09-13)**: 글자 토글·서식 지우기·링크 삽입을 **선택 없이도 활성**(단축키와 같게).
>     링크 삽입이 선택을 요구한 이유가 결함이었다 — `SetHyperlink`가 선택 없으면 **아무것도 안 하고 빈 되돌리기 단계**를 쌓았다.
>     이제 `ApplyStyleToSelection`(캐럿 단어 / 다음 입력)로. "링크 열기"는 열 수 있는 링크(http/https, `IsOpenableUrl`)에서만 활성.
>     포트에서 온 것: 목록 메뉴 "목록 제거", Ctrl+Shift+7 번호 목록(`ShortcutId.NumberedList`). 테스트 984 → 994
>     (`ContextMenuConvergenceTests`), 반증 8종 전부 의도한 테스트만 실패. 미룬 것: 셀 배경색 편집 UI(상류엔 없다) · "셀 복사"
>     메뉴(F5 + Ctrl+C로 대신됨).
>   - **실기 후속 (2026-09-14)** — 실기 확인 완료.
>     - **셀 블록 복사**: F5 한 셀은 셀의 **글자**로, 여러 셀 블록은 **표 전체**로 복사됐다(블록 캡처가 최상위 블록 단위). 이제 칠한
>       사각형을 독립 표로(`CellBlockAsTable` — 안쪽 병합 유지, 밖에서 들어온 병합은 보통 셀), 평문은 `TableText`(탭·줄바꿈).
>       포트 `TableBlock.Extract`와 같은 결과이되 공개 API를 늘리지 않게 private.
>     - **링크 대화상자**: `InputDialog`가 포커스 없이 열렸다 → `Opened`에서 입력칸 포커스 + 캐럿 끝, 닫히면 `Focus()`. 빈 곳 OK는
>       링크가 다음 입력에 대기만 해 보이지 않았다 → `ApplyHyperlinkFromDialog`가 주소를 링크 글자로 삽입. ⚠️ 공개 `InsertText`는
>       체크포인트를 쌓지 않아 `PushUndo`를 먼저 — 테스트 첫 판이 "되돌려도 남는다"로 잡았다.
>     - 테스트 994 → 998, 반증 4종(빈 곳 삽입 · 체크포인트 · 블록 복사 · 앵커 한 칸) 전부 의도한 테스트만 실패.
>     - 메뉴 행 높이 실측(포트 밀도 조정용): 여백 10,2,10,2, 행 18px, 구분선 1px(헤드리스), 아이콘 없음(데모가 제공 안 함).
>   - **선택을 둔 채 표 우클릭 (2026-09-14, 측정 후 수정)**: 캐럿만 누른 셀로 가고 선택은 제자리였다 — 캐럿이 다른 셀에
>     그려지고 Shift+방향키가 거기서 늘어났다(입력 자체는 선택 자리를 바꿔 결과는 맞았다). 선택이 있으면 캐럿을 둔다(포트·텍스트
>     분기와 같게). 텍스트 메뉴의 "표" 하위 메뉴도 캐럿의 표로(`MenuCellTable`) — 행·열 항목이 캐럿 셀의 번호를 쓴다. 첫 반증에서
>     이 둘째 규칙이 살아남아 두 표 사이 우클릭 테스트를 더했다. 테스트 998 → 1000, 반증 2종 모두 실패.
>   - **표 메뉴를 포트와 항목 단위로 같게 (2026-09-14, 사용자 결정: 누른 대상에 맞는 메뉴)**: 셀 배경색 하위 메뉴 추가(툴바 팔레트
>     격자 + 없음, 셀 블록 — 한 셀 포함 — 아니면 누른 셀), 표 삭제 앞 구분선, 셀이 없으면 세로 정렬을 흐리게(숨기지 않음), "목록 제거"
>     다시 제거. 두 리포 테스트가 같은 항목 순서를 기대값으로 가진다(`TableMenuLabels`). 데모는 편집 중 전체 서식 메뉴.
>     - 결함(포트 테스트가 발견): **빈 셀** 한 셀 블록은 길이 0이라 "선택 있음"을 위치 비교로 보던 세 곳(우클릭 메뉴·복사·Ctrl+C/X)이
>       선택 없음으로 읽었다 — 우클릭이 블록을 풀고 복사가 아무것도 안 했다. `HasTextOrCellSelection`으로.
>     - 테스트 1000 → 1005, 반증 4종.
>     - **실기 후 조정(사용자)**: 블록 캐럿(테두리)으로 통째로 잡은 표는 셀을 정하지 않으므로 셀 항목을 흐리게 — 테두리 옆 셀을
>       잡던 것(포트가 맞았다). 셀 안 글자 메뉴에 "셀 선택"을 "표" 하위 메뉴 바로 위로(`SelectCellItem`). 1005 → 1007, 반증 2종.
>   - 실기 확인 완료(2026-09-20, 사용자): 뷰어(데모 체크박스)에서 표 우클릭 → 복사 → Word/HWP 붙여넣기 · 경계 클릭 후 Ctrl+C · 편집 모드 경계
>     우클릭 복사 · 경계 클릭 후 Ctrl+C · 인라인 표·셀 안 표 경계의 커서·클릭·우클릭(샘플에 인라인 표가 있다 — 셀 안 표는 셀에
>     표를 넣어서) · 표를 잡고 잘라내기/Delete → 표가 사라지고 Ctrl+Z로 돌아오는지 · 인라인 표를 복사해 글줄 중간에 붙이면
>     인라인 표로 들어가는지.
>
> - **라운드22 · 표 크기 끌기(2026-09-15, 포트 백포트)** — 포트가 테스트 참조 0이던 `RichEditor.TableResize.cs`를 감사해 결함 4건을
>   고쳤다. 상류로 옮길 셋을 **실입력 테스트로 먼저 측정**(`ResizeDragContainmentTests` 8개 — 6 빨강, 대조군 2 초록) 후 수정:
>   - **좁은 두 열 사이 경계 끌기 = 크래시**(상류만): 합이 40px 미만이면 `Math.Clamp` 경계가 뒤집혀 `ArgumentException`. 포트는 외부
>     감사 때 `ClampColumnDelta`로 고쳤는데 상류로 오지 않았다 → 같은 도우미.
>   - **셀 문단 인라인 표가 셀 밖으로**(190px 셀에서 420px): `EnclosingCellInnerWidth`가 부모가 셀인 표만 봤다 → 인라인 표도(호스트 문단 여백 뺌).
>   - **넘친 중첩 표가 잡자마자 스냅**(150 → 40): 상한이 시작 폭 아래로 내려가지 않게.
>   - **포인터 캡처 상실**: 떼기 없이 캡처를 잃으면 열·행 끌기와 드래그 선택이 버튼 없이 계속됐다(테스트에서 `IPointer.Capture(null)`로
>     **실제 재현**). `OnPointerCaptureLost` override(새 공개 표면, Unshipped)가 모두 끝낸다. 떼기 경로는 이미 플래그를 먼저 푼 뒤 캡처를 놓는다.
>   - 포트의 나머지 하나(문서 교체 시 끌기 해제)와 "누르기는 쓰지 않는다"는 상류가 이미 맞았다.
>   - 테스트 1030 → 1038. 반증 7종 — 인라인 상한 제거 · 스냅 상한 · 뒤집힘 폴백 제거 · 캡처 처리기 무동작 · 처리기에서 선택/열/행
>     해제 한 줄씩 제거 — 전부 의도한 테스트만 실패.
>
> - **라운드23 · 표·그림 끌어 옮기기(2026-09-15, 포트에서 기능 이식)** — 표 왼쪽·위 테두리는 이동 커서(`MoveCursor`)를 보이면서
>   누르면 블록 캐럿만 놓았다(커서가 약속만 하던 상태, 포트도 같았다). 포트가 먼저 만든 끌기를 **동작 그대로** 옮겼다:
>   테두리·그림 누르기 → 슬롭 6px → 회색 드롭 캐럿 → 떼기로 이동, 놓을 때 Ctrl=복사(캐럿 옆 "+"). `RichEditor.DragBlock.cs`.
>   - **클릭 동작은 상류 그대로**: 최상위 표 테두리 = 블록 캐럿(이미 `_selectedBlock`과 같은 틀로 그려지고 Delete·복사·잘라내기가
>     그 표를 잡는다 — `_selectedBlock`으로 바꾸면 복사 경로가 표를 못 본다). 드롭 뒤에는 **클릭이 남기는 상태**를 다시 건다
>     (그림=선택, 최상위 표=블록 캐럿, 셀 안·인라인 표=`SelectWholeTableForCopy`).
>   - 드롭 규칙·가드는 포트와 같다(문단 맨 앞=앞, 맨 끝=뒤, 중간=분할 — 분할 꼬리는 제목 수준 유지; 제자리=편집 아님; 이동은 자기
>     자손 셀 금지). 공개 API 무변경 — Ctrl 떼기는 `OnKeyUp` override 대신 생성자의 `KeyUp` 구독.
>   - **포트보다 나은 점**: `InteractionHost`로 실제 누르기·이동·떼기·Ctrl을 주입 — 포트가 실기로만 보던 배선(테두리가 끌기를 거는지,
>     떼기가 Ctrl을 읽는지)을 자동 검증. `ObjectDragInteractionTests` 14개, 편집기 퍼즈에 드롭 명령(`DropOp`).
>   - 테스트 1038 → 1052. 반증 8종 모두 실패 — 항상 분할 5 · 제자리 판정 제거 1 · 자기 셀 허용(**순환 → 호스트 정지**,
>     `--blame-hang-timeout`이 끊음) · 교체 시 취소 제거 1 · 인라인 보정 제거 1 · 뷰어 차단 제거 1 · 테두리 무준비 2 · Ctrl 무시 1.
>     뷰어 차단은 처음에 **헛돌았다** — 뷰어의 표 테두리 누르기는 복사용 선택에서 먼저 반환해 가드에 닿지 않는다. 그림 끌기를 더해 고침.
>   - 실기 확인 완료(2026-09-20, 사용자): 테두리·그림 끌기, Ctrl 복사와 "+", 자기 셀로 끌 때 금지 커서, 클릭만 하면 전과 같은지.
>
> - **라운드24 · 줄 간격을 HWP 기준으로 통일(2026-09-15)** — 툴바는 HWP %를 보이고 문서·주석은 "HWP % ÷ 100"이라 했지만, 계산은
>   `비율 × 글자 크기 × 1.2`(Word "배수")라 160%를 고르면 HWP 기준 ≈192%가 나왔다. 계산을 **글자 크기 × 비율**로 바꿨다.
>   - 필드·API·저장 형식 무변경(사용자 결정: 필드 유지, 기존 문서 모양 변화는 감수). 미설정(NaN)은 계산 시 HWP 기본 160% —
>     그래서 JSON 이전 처리 없이 기존 문서도 160%로 그려지고, `LineHeight`만 둔 문단(고정 값)은 그대로다. ≤1.0을 무시하던 규칙 삭제.
>   - **Avalonia 함정**: `TextParagraphProperties.LineHeight`는 정확 높이라 줄보다 큰 인라인 그림·표를 다음 줄 위에 겹쳐 그린다
>     (12.1.0 `TextLineImpl` 확인). 가산 간격 `LineSpacing`은 **internal**, `GenericTextParagraphProperties`는 sealed라 쓸 수 없다 →
>     줄 상자보다 큰 인라인 객체가 있는 문단만 자연 높이로 되돌린다(이 문단은 비율 간격을 잃는다).
>   - 기본 줄이 커지자 ↓의 고정 30px 걸음이 아래 그림에 못 닿았다 → 다음 블록 위쪽으로 걷는다(`NextBlockTop`).
>   - 테스트 1052 → 1053(`LineSpacing_IsHwpPercentOfFontSize_AndDefaultsTo160`) + render 32 그린. 기존 인라인 객체 테스트 8개가
>     Avalonia 함정을 먼저 잡았다.
>   - 실기 확인 완료(2026-09-15, 사용자): 기본 160%, 툴바 전환, 100%에서 줄이 붙는 모습(HWP와 같음), 인라인 객체 줄, ↓ 그림 진입.
>   - **후속(2026-09-16, 사용자 결정)**: 실기에서 "목록이 100%보다 넓다" — 목록 항목은 각각 문단이라 `Block.MarginBottom` 기본
>     10px가 줄 간격에 더해졌다(100% 목록 ≈175%). **문단만** 기본 아래 여백 0(`Paragraph` 생성자, `DividerBlock`과 같은 방식),
>     그림·표는 10 유지. JSON `?? 10`·HTML `DefaultMarginBottom`도 0. 저장 파일은 여백을 명시하므로 모양 유지.
>     포트에도 같은 변경. 병합: 이 저장소 PR #36, 포트 PR #31(2026-09-16, CI 전부 통과).
>
> - **라운드25 · HTML/RTF 줄 간격 입출력(2026-09-16, 포트에서 이식)** — 두 포매터 모두 줄 간격을 버리고 있었다. HTML은 CSS
>   `line-height`(%·단위 없는 수 = 글자 크기 배수라 HWP 비율과 1:1, px = `LineHeight`, `normal` = 미설정), RTF는 `\sl` = 비율 × 200
>   (`\slmult` Word 배수 ≈ HWP ÷ 1.2), 고정값 `\sl-N\slmult0`, 미설정은 160%를 명시하고 `{\*\arsl}`로 우리 리더에선 미설정 유지.
>   셀 문단은 줄 간격이 있으면 자기 요소로 나간다(`NeedsOwnElement`). RTF `\pard`가 줄 간격 상태도 초기화한다.
>
> - **라운드26 · 되돌리기 예산에 지운 그림 청구(2026-09-16, 포트에서 백포트)** — 64MB 예산이 그림을 "살아 있는 문서와 공유"라며
>   통째로 뺐다. 편집으로 지운 그림은 스냅샷만이 붙잡는다 — 여기서는 `Clone`이 **디코드된 비트맵까지** 공유하므로 12MP 한 장이
>   바이트 + 46MB. 포트가 이미지 하네스로 측정(JPEG 36장 삽입·삭제: 133MB가 예산과 무관하게 유지)한 뒤 고친 것을 이식.
>   스냅샷 walk 한 번에 그림 요소 수집(`UndoState.Pictures`) → `Trim`이 현재 문서가 참조하지 않는 **바이트 배열과 캐시된 비트맵을
>   각각** 참조 동일성으로 스택당 한 번 청구(`CachedBitmap`: 디코드를 유발하지 않는 internal 접근자). live = push 시 새 스냅샷,
>   undo/redo 시 복원 상태, 커진 쪽 스택만 트림. 테스트 +5(`UndoBudgetTests`, 비트맵 청구는 상류 전용), 반증 5종 전부 빨강,
>   전체 1061 그린. 포트의 나머지 이미지 메모리 수정(편집 삭제 비트맵 해제·표시 크기 디코드·인쇄 자리표시)은 포트의 비동기
>   GPU 캐시 구조에서 생긴 문제라 해당 없음 — 단 여기의 **원본 해상도 지연 디코드**(`Image` getter, 불러온 문서는 축소 없음)는
>   같은 메모리 특성을 가지므로 별도 후보.
>
> - **라운드27 · 그림을 그려지는 크기로 디코드(2026-09-16, 포트 설계 이식)** — 렌더가 모델 `Image`를 그렸고, 그 getter는 원본 크기로
>   디코드해 요소(와 Clone을 통해 모든 되돌리기 스냅샷)에 붙잡았다. `Image`는 **공개 API("디코드된 비트맵")** 라 getter를 축소
>   디코드로 바꾸지 않고, 렌더 전용 `ImageDisplayCache`(바이트 배열에 약참조, `Bitmap.DecodeToWidth`)를 뒀다: 요청 픽셀 = 표시 크기 ×
>   `RenderScaling` × 에디터→창 변환 배율(호스트 줌 `LayoutTransform` 포함), 여유 1.25배, 원본 초과 금지, 더 크게 그리면 재디코드
>   (비교 기준은 결과가 아니라 **요청했던 목표** — 결과 기준이면 no-op 백엔드의 1×1 스텁에서 매 프레임 재디코드), 교체된 비트맵은
>   컴포지터가 쓸 수 있어 Dispose하지 않음. 인쇄(`DrawPrintPage`)는 300 DPI. 인라인 그림은 레이아웃 생성이 아니라 **그릴 때** 해석
>   (`LayoutSeg.Picture` 델리게이트 — 측정용 레이아웃이 화면 밖 인라인 그림까지 디코드하던 것 제거). 내부의 `Image` 접근 전부 정리:
>   크기 프리셋·블록↔인라인 전환은 헤더(`ImageInfo`, 포트에서 이식), 메뉴 활성은 `HasPicture`, 이미지 저장·복사는 일시 디코드,
>   삽입·교체는 디코드한 비트맵을 모델에 심지 않음. 테스트: 기본 +1(모델 고정 없음, no-op 백엔드), 렌더 +6(실제 Skia 크기 —
>   `Tests.Render`에 `InternalsVisibleTo` 추가). 반증 7종 전부 빨강(처음엔 "재디코드 끔"이 헛돌았다 — 줌 테스트가 처음부터 3배로
>   그려 업그레이드 경로를 안 탔다. 1배→3배로 고침). 전체 1062 + 38 그린. **실기 확인 필요**: 고DPI 선명도, 줌 슬라이더에서 사진
>   많은 문서의 끊김(업그레이드는 UI 스레드 동기 디코드), 인쇄·PDF 그림 품질.
>   - 실기 확인 완료, PR #39 병합(2026-09-16).
>
> - **라운드28 · 그림 복사의 일시 할당(2026-09-17, 포트에서 백포트)** — 포트가 측정한 문제의 대조. 여기서는 복사가 RTF를 넣지 않고
>   (텍스트 + CF_HTML), RTF 16진수는 이미 조각 단위였다(여기가 앞서 있던 부분). 같은 프로브(`CopyAllocationProbeTests`,
>   `RICHEDITOR_PERF=1`)로 먼저 측정: 10MB 사진 1장 복사 **205MB**(선택 HTML 165 + CF_HTML 문자열 27 + UTF-8 13), RTF 내보내기 120MB.
>   포트와 같은 수정(`AppendBase64` 조각 기록 + `DocumentPictures.Bytes`로 빌더 선할당 + `AppendHtml`로 `<div>` 같은 빌더 + RTF 최종을
>   `string.Create`)에 더해 여기만의 것: CF_HTML을 문자열 없이 바로 UTF-8 바이트로(`BuildCfHtmlBytes`; `BuildCfHtml` 문자열판은 그것을
>   감싸 기존 오프셋 테스트가 새 경로를 검증). **복사 205 → 67MB, RTF 120 → 80MB**. 테스트 +5(`PictureTextOutputTests`), 반증 5종 전부
>   빨강. 왕복 테스트는 처음 빨갛다 — 여기 리더는 읽을 때 그림을 디코드해 안 되는 것을 버리므로, 무작위 바이트 + 플랫폼 없는 `[Fact]`로는
>   그림이 전부 사라졌다(수정 무관, 테스트 데이터 문제). 실제 BMP + `[AvaloniaFact]`로 고침. 전체 1068 + 렌더 38 그린.
>
> - **라운드29 · 그림 가로·세로 손잡이 + 비율 바뀐 그림의 선명도(2026-09-19, 포트에서 이식)** — 포트 PR #38·#39의 대조.
>   - **덮기 디코드**: `ImageDisplayCache.Decode`가 원본 비율로 사각형 **안**에 맞췄다 → 비율이 바뀐 그림(`<img width height>`,
>     아래 손잡이)은 긴 축이 모자라 흐림. 측정(렌더 테스트, 수정 전): 400×100을 200×124, 100×400을 125×89에서. 포트는 같은 원인으로
>     매 프레임 재디코드 루프까지 났지만 여기는 **요청 목표와 비교**해 루프는 없었다. 수정: 사각형을 **덮는** 균일 배율(원본 상한, 올림).
>   - **보간**: 덮기로 바꾸자 짧은 축을 그리기에서 여러 배 줄이게 되어 **앨리어싱이 드러났다**(1px 줄무늬 ~17:1 축소, 행 green 0~239 —
>     수정 전 contain 디코드에선 디코더가 미리 줄여 통과했다). `DrawPicture`(`PushRenderOptions` HighQuality)로 그림 그리기 세 곳 통일.
>   - **손잡이**: 모서리(비율 유지) · 오른쪽 가운데(너비) · 아래 가운데(높이), 커서 ↘↔↕. `_imageHandles`/`_inlineHandles` 항목에
>     `ResizeGrip`, `PictureHandles`가 모서리를 **먼저** 기록(겹치면 누르기·커서 모두 첫 일치 = 모서리; 기존 테스트의 `.First()`도 모서리).
>     가장자리 손잡이는 그려진 크기에서 두 변을 모두 씀(셀이 줄여 그린 그림에서 가장자리가 포인터를 1:1로).
>   - ⚠ **열 경계와의 겹침**: 셀을 꽉 채운 선택된 그림의 오른쪽 손잡이는 열 경계 위에 있다. 여기는 그림 손잡이를 열 경계보다 **먼저**
>     보므로(기존 순서) 경계 가운데를 잡으면 그림이 바뀐다 → `AnImageInACell_IsCappedWhenDrawnButKeepsItsLargerStoredSize`가 경계 위쪽을
>     잡도록 조정. **포트는 반대 순서**(열·행 경계 먼저)였다 — 사용자 결정(2026-09-19): **여기 방식(선택된 그림 손잡이 우선)으로 포트를 맞춘다**.
>   - 테스트: 렌더 +3(`ImageDisplayDecodeRenderTests`), 기본 +10(`PictureResizeHandleInteractionTests` — 실입력 끌기·호버 커서·셀·인라인·뷰어).
>     `InteractionHost.ImageHandles`/`InlineImageHandles`에 grip(이름) 추가. 반증: 손잡이 4종 전부 빨강(모서리 오기록은 기존 셀 테스트 5개도
>     잡음), 디코드·보간은 수정 전·중간 상태가 빨강. 전체 1078 + 렌더 41 그린.
>   - 실기 확인 완료(2026-09-19, 사용자): 손잡이 셋·커서·셀·인라인, 테스트 HTML의 줄무늬·글자.

>
> - **라운드30 · 포트 감사 백포트 — 가져오기 BOM · 표 그리기 · Ctrl+K(2026-09-19, 포트 PR #42의 대조)** — 포트가 "테스트 참조 0" 파일
>   셋을 감사해 찾은 것을 여기서 **먼저 측정**(`PortAuditBackportTests`, 표 그리기는 실입력 `InteractionHost`): 대조군 4 초록, **결함 9 빨강**.
>   - **BOM 있는 파일 가져오기 실패**: 바이트 판별(`LooksLikeRtf(buf)`)도 문자열 분기(`TrimStart().StartsWith("<")`)도 BOM을 건너뛰지 않아
>     UTF-8 BOM의 HTML/JSON/RTF, UTF-16 HTML/JSON **5종 모두 JSON 리더로** 가서 실패(진단 채널만). BOM으로 인코딩을 정하고 판별은 BOM 뒤부터.
>     `ImportStreamAsync`로 피커와 분리. ⚠ 기존 `ByteSniff_BomIsNotSkipped`의 주석이 "BOM 파일은 UTF-8 분기로 가서 거기서 처리된다"고
>     적고 있었는데 그 분기도 처리하지 않았다 — 단정 자체(판별 함수는 BOM을 안 건너뜀)는 참이라 유지, 주석 정정.
>   - **표 그리기 4건**: 모드가 켜진 채 문서 열기(`LoadHtml`) → 다음 클릭이 새 문서에 표 · 되돌리기도 모드 유지 · 캡처 상실에도 유지(놓으면 삽입) ·
>     **200px 셀 안에서 500px 표**(편집기 경계로만 잘림). 수정: `ResetInteractionState`·`OnPointerCaptureLost`에서 `CancelTableDraw`, 폭은
>     드래그 시작점의 셀 내용 폭으로(`TableDrawRoom`, 미리보기도 같은 `DrawnTableRect`). 놓기의 `Capture(null)`을 삽입 뒤로 — 포트는 이
>     순서 때문에 한 커밋 동안 "어떤 그리기도 표를 안 넣음"을 냈는데, **여기서는 반증해 보니 무해했다**(`pd`/`startView`를 지역으로 먼저
>     복사 — 먼저 해제해도 삽입됨을 측정). 방어적 정리로만 두고 주석을 그렇게 적었다.
>   - **Ctrl+K(링크 삽입)**: 포트에만 있던 단축키. 캐럿이 링크 안이면 그 주소를 채워 대화상자를 연다(`CaretLinkUri`, 포트 `CurrentLinkUri`와
>     같은 규칙), 메뉴에 힌트. 끝까지 테스트: 키 → 대화상자(창 열림 이벤트로 포착) → 주소 확인 → OK → 링크 교체.
>   - 반증: BOM 판별 끔(5) · 문서 교체 해제 제거(2) · 캡처 해제 제거(1) · 폭 제한 제거(1) · Ctrl+K 분기 제거(1) 전부 의도한 테스트만 빨강.
>     놓기 순서 되돌리기는 초록(위). 전체 1093 + 렌더 41 그린.
>   - 참고(포트와 차이, 미변경): 포트는 놓기에서 캐럿을 옮기지 않고 캐럿 위치에 넣는다 — 여기는 드래그 시작점으로 캐럿을 옮긴다.
>   - 실기 확인 완료(2026-09-19, 사용자): BOM 파일 3종 가져오기, 표 그리기(보통·셀 폭 제한·모드 해제), Ctrl+K.

>
> - **라운드31 · AltGr · 찾기 UI(2026-09-19, 포트에서 이식)**
>   - **AltGr**: AltGr는 Ctrl+Alt로 들어와, AltGr 문자가 있는 자판에서 제목 단축키 Ctrl+Alt+1~6이 독일어 AltGr+2(²)·AltGr+3(³)을
>     가로챘다. `KeyEventArgs.KeySymbol`이 인쇄 가능한 문자면 처리하지 않고 TextInput으로(`IsAltGrTyping`). 포트는 OS 자판에 물어야
>     해서(`ToUnicodeEx`) 배선을 실입력으로 검증 못 했지만, 여기는 헤드리스 창에 `KeySymbol`을 실어 **배선까지** 검증(대조군: 기호 없음 =
>     제목 단축키). 반증: 판정 제거 → 빨강. `InteractionHost.Key`에 keySymbol 오버로드.
>   - **찾기 UI**: 검색 엔진(`FindNext`/`FindPrev`/`ReplaceNext`/`ReplaceAll`, 강조, n/m 위치)은 있었는데 **닿을 길이 없었다** — Ctrl+F도,
>     F3도, 막대도 없었다(표면 기계 diff: 포트에만 `FindRequested`·`LastFindQuery`·`FindAgain`·`ShowFindBar` 등). 포트 설계 그대로:
>     편집기가 Ctrl+F/Ctrl+H에 `FindRequested(withReplace)`를 내고(찾기는 뷰어에서도, 바꾸기는 편집 가능할 때만), F3/Shift+F3는 마지막
>     검색 반복, `RichEditorView`가 도구 모음 아래 막대로 응답(Enter 다음 · Shift+Enter 이전 · Esc 닫기 · ▸ 바꾸기 줄 · Aa · n/m, 닫으면
>     강조 해제). `ShowBuiltInFindBar=false`면 호스트 자체 UI. 공개 API 추가 9(minor). 도구 모음의 찾기 버튼은 여기 아이콘 세트에
>     찾기 아이콘이 없어 넣지 않았다(포트엔 있음 — 남은 차이).
>   - 테스트: `AltGrKeyTests` 8, `FindBarTests` 6(실입력). 반증: 뷰 구독 제거(2) · F3 제거(1) · 뷰어 찾기 허용 제거(5) 전부 빨강.
>     `InteractionHost.CreateWithView` 추가. 전체 1107 + 렌더 41 그린.
>   - ⚠ **실기에서 F3가 안 됐다**: 찾기 막대에서 Enter 뒤에도 포커스가 검색 상자에 남아 F3가 편집기의 F3에 닿지 않았다(테스트는 편집기에서만
>     F3를 눌렀다). 검색·바꾸기 상자에서 F3/Shift+F3 처리, 포커스가 상자에 있는 채로 실입력 테스트 + 반증. 포트도 같은 결함이라 함께 수정.
>   - 실기 확인 완료(2026-09-19, 사용자): 찾기 막대 · Enter/F3/Shift+F3 · 바꾸기 · Esc.

>
> - **라운드32 · 도구 모음 찾기 버튼(2026-09-19, 포트와 수렴)** — 라운드31의 남은 차이. `RichEditorIcon.Find`(= 47, 끝에 추가 — 공개 순서 값
>   보존) + 돋보기 벡터(포트 것). 버튼은 보기 도구 모음 맨 앞(찾기는 읽기만 하므로 뷰어도)과 편집 도구 모음의 삽입 묶음 뒤. **받는 쪽이 있을
>   때만 표시**(`HasFindUi` + 구독 변경 시 내부 `FindUiChanged`, `AllowFindReplace` 변경도 반영) — 포트가 처음엔 무조건 보여 편집기+툴바
>   구성에서 눌러도 아무것도 안 열리는 버튼을 냈고 실기에서 잡혔다. 테스트 `ToolbarFindButtonTests` 3(실입력 클릭 → 막대 열림, 호스트 정리),
>   반증 3종(받는 쪽 확인 제거 · 변경 알림 연결 제거 · 플래그 미반영) 전부 빨강. 전체 1111 + 렌더 41 그린. 공개 API 추가 1.
>   - 실기 확인 완료(2026-09-19, 사용자): 돋보기 버튼 · 툴팁 · 클릭으로 찾기 막대.
>
> - **라운드33 · macOS CI를 이틀 동안 빨갛게 만든 렌더 테스트(2026-09-20)** — 라운드29가 넣은
>   `ASquashedPicture_IsFiltered_NotAliased`가 macOS에서만 "found 0 picture rows"로 죽었다. 같은 커밋의 Windows·Linux는
>   초록이고 유닛도 전부 초록이라, 09-18부터 main의 CI 4회가 이 하나 때문에 빨갰다.
>   - **원인은 제품이 아니라 테스트였다.** `CopyPixels`는 백엔드의 픽셀 배치를 그대로 돌려준다 — Skia의 네이티브 색 타입은
>     Windows·Linux가 BGRA, macOS가 RGBA다. 이 테스트만 빨간 줄무늬를 채널 **위치**로 찾아(`buf[o+2]`=R, `buf[o+1]`=G),
>     RGBA에서는 `o+2`가 B(=0)라 모든 줄이 탈락해 정확히 0이 됐다. 나머지 40개는 비트맵 **크기**만 보거나 채널끼리
>     비교(`max-min`)하거나 알파만 봐서(알파는 두 배치 모두 인덱스 3) 무사했다 — 41개 중 채널을 이름으로 짚은 건 이것뿐.
>   - 수정: 채널 이름 대신 성질로 판정한다(줄무늬는 빨강 = 한 채널이 나머지 둘과 멀다, 배경·흰 줄은 아니다). 측정값은 낮은
>     채널 — 평균화가 끌어올리는 바로 그 값이라 단정의 의미는 같다. 실패 메시지에 그 열의 실제 바이트를 찍어, 가설이 틀렸다면
>     macOS CI가 무엇이 그려졌는지 말하게 했다(실기가 없어 CI가 유일한 측정 수단이라 진단 자체를 출력에 심었다).
>   - 반증: `DrawPicture`의 보간을 HighQuality → None으로 되돌리면 다시 빨강(24 rows) — 채널 가정만 걷어냈고 앨리어싱
>     검출력은 그대로다. Windows 렌더 41 그린, 이어서 **macOS·Linux·Windows 전부 그린**(PR #46) — 09-16 이후 첫 3-OS 초록.
>   - 제품 코드 무변경. macOS에서 그림은 원래 정상으로 그려지고 있었다.
