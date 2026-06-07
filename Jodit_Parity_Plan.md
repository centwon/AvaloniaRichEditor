# Jodit 대체(파리티) 구현 계획

> 목표: AvaloniaRichTextBoxPort를 **Jodit 에디터 수준**으로 끌어올려, SaemDesk의 **WebView2 + Jodit**을
> 네이티브 컨트롤로 **드롭인 교체**한다. SaemDesk는 콘텐츠를 **HTML로 저장**하므로 성패의 핵심은
> **HTML 무손실 왕복(round-trip)**이다.

---

## 0. 전제 / 성공 기준

> **충실도 목표(완화됨)**: 100% 픽셀/태그 재현이 아니라 **시각적으로 무난한 수준**의 왕복.
> 고빈도·고가시성 서식(글꼴/크기/색/배경/정렬/표/이미지/목록/제목)을 우선하고, 희귀하거나
> 미묘한 Jodit 특유 마크업은 근사 허용.


- **저장 포맷은 HTML 유지** (SaemDesk `_post.Content` 등 기존 데이터·코드가 HTML 기반). JSON은 내부용으로만 유지.
- **교체 지점은 작다**: `JoditEditor`의 공개 API만 동일하게 구현하면 호출부 무변경.
  - `Mode`(ReadOnly/Simple/Full), `Text`(HTML, 양방향), `TextChanged`, `GetHtmlAsync()`, `PrintAsync()`, `InsertHtmlAsync()`
- **성공 기준 (측정 가능)**
  1. 실제 저장 HTML 코퍼스 N건을 열고 → 편집 없이 다시 저장했을 때 **시각적 손실 없음**(왕복 diff 허용 범위 내).
  2. 아래 기능 파리티 체크리스트 통과.
  3. SaemDesk에서 `JoditEditor` → `NativeEditor` 교체 시 **호출부 코드 변경 0**.

---

## 현재 격차 요약 (조사 결과)

| 영역 | Jodit(SaemDesk 사용) | 포트 현재 | 격차 |
|---|---|---|---|
| 굵게/기울임/밑줄/취소선 | ✅ | ✅ | - |
| 글자 크기 | ✅ | ✅ | - |
| 글자 색 | ✅ 임의색 | △ 적용은 임의, **ToHtml은 빨강/파랑만** | export 수정 |
| **배경색(형광펜)** | ✅ brush | ❌ | 모델+UI+HTML |
| **글꼴 패밀리(font)** | ✅ | ❌ Run에 속성 없음 | 모델+렌더+UI+HTML |
| 정렬 | ✅ | ✅ | - |
| 글머리(ul) | ✅ | ✅ | - |
| **번호목록(ol)** | ✅ | ❌ | 모델+렌더+HTML |
| **제목/문단스타일(h1~h6, 인용)** | ✅ | ❌(크기만) | 모델+렌더+UI+HTML |
| **구분선(hr)** | ✅ | ❌ | 모델+렌더+HTML |
| 링크 | ✅ | ✅ | - |
| 표 | ✅ | ✅(행·열 편집) | colspan/rowspan 여부 확인 |
| 이미지 표시/리사이즈 | ✅ | ✅ | - |
| **이미지 붙여넣기/드롭 + 압축** | ✅(1920×1080/500KB) | ❌ | 입력 경로 |
| undo/redo, 서식지우기(eraser) | ✅ | ✅ | - |
| **인쇄(print)** | ✅(브라우저) | ❌ | 네이티브 인쇄 |
| **ReadOnly 모드** | ✅ | ❌ | 모드 처리 |
| **HTML 무손실 왕복** | (브라우저 네이티브) | ❌ ToHtml 매우 lossy(이미지 미출력, 색 하드코딩 등) | **최대 작업** |

---

## 1. 단계별 계획 (의존순)

### Phase 0 — 기반 & 검증 하네스 *(먼저)*
- [x] **왕복 테스트 하네스**: `--roundtrip <inDir> [outDir]` 헤드리스 모드(`Program.cs` + `Formatters/RoundTripHarness.cs`). `html → ParseHtml → ToHtml` 후 기능 토큰 in/out 카운트로 손실 리포트(`report.txt`).
- [x] **대표 코퍼스 작성**: `tests/corpus/01_inline,02_lists,03_headings,04_table_image.html` (Jodit 스타일 인라인 style·ol/ul·h*·hr·blockquote·표·data:이미지).
- [x] **실데이터 코퍼스 추가**: `C:\Users\centw\saemdesk\board.db`(테이블 `Post`, 컬럼 `Content`=HTML)에서 가장 큰 게시글 8건 추출 → `tests/corpus/real_*.html`. **⚠ 학생 개인정보 포함 → `.gitignore`로 커밋 제외.** (.NET 10 파일기반앱 `dotnet run extract.cs` + `#:package Microsoft.Data.Sqlite@10.0.8`로 추출.)
- [ ] **호환 API 스펙 확정**: `NativeEditor`가 구현할 표면을 `JoditEditor`와 1:1 매핑 정의.

**⚠ 실데이터 왕복 리포트 (계획 재정렬의 근거):**
- **font-family** = 압도적 1위 손실. 한 글에 10,672 / 2,420 / 1,054회 등 — Jodit은 거의 모든 span에 `font-family`를 붙임. 모델에 글꼴 속성이 없어 **전량 소실**.
- **font-size(px)** 대량(3,679 / 272 / 177회) — 현재 `>14`만 취급해 대부분 소실.
- **color(임의색)** 대량(892 / 777 / 536회) — 빨강/파랑 하드코딩이라 전량 소실.
- **background** 대량(566 / 536회) — 모델·HTML 미지원으로 소실(셀 배경/형광펜).
- **align** 입력 대비 급감(2,221→44 등) — Jodit이 블록 외 요소에도 정렬을 붙임. 문단 정렬로의 매핑 정책 필요.
- 표 구조(`<table>`)·굵게는 대체로 보존. heading/hr/ol/img/밑줄/이탤릭은 소실되나 인라인 스타일보다 빈도 낮음.
- → **재정렬된 우선순위(실데이터 기준): ① font-family ② font-size(임의) ③ color(임의) ④ background ⑤ align 매핑 ⑥ 이미지 ⑦ ol/heading/hr/밑줄/이탤릭.**

**기준선 손실 리포트 (현재 ToHtml/ParseHtml):**
| 샘플 | 손실 항목 |
|---|---|
| 01_inline | underline, strikethrough, **임의 color**, background, font-family |
| 02_lists | **ol(번호목록)** |
| 03_headings | **h1~h6**, blockquote, hr (제목이 굵게/큰글씨로만 근사, 태그 소실) |
| 04_table_image | 임의 color, **img(이미지 자체 미출력)** |

→ Phase 2 작업 우선순위 = 이미지 출력, ol, 제목/hr/blockquote, 밑줄/취소선, 임의색, 배경색, 글꼴.

### Phase 1 — 문서 모델 확장 *(모든 것의 토대)*
- [x] `Run.FontFamily`(string?) 추가 — `BuildTextLayout`의 `Typeface`에 반영(글꼴), `Clone`·JSON 직렬화·HTML 왕복 반영.
- [x] `Run.Background`(IBrush?) 추가 — `GenericTextRunProperties` 배경 브러시로 렌더, `Clone`·JSON·HTML 반영.
- [x] **(Phase 2a 동반)** 인라인 스타일 HTML 무손실 왕복: 임의색(#hex/rgb/rgba)·배경색·글꼴 패밀리·**font-size(px+pt 환산)**·밑줄·취소선·이미지(data:base64) — `ApplyInlineStyle`/`ParseInlines` 확장 + `ToHtml` `EmitInline` 헬퍼. 하네스로 검증: 합성 01_inline 완전 통과, 실데이터 글꼴/크기/색/배경 대부분 복원.
- [x] **리스트 모델 정리**: `Paragraph.ListType {None, Bullet, Ordered}` 추가, `IsListItem`은 계산속성으로. `ToggleBullet`/`ToggleNumbering`. HTML `ul`/`ol` 왕복. *(중첩 깊이는 평탄화 — 모델에 레벨 없음)*
- [x] **문단 스타일(제목)**: `Paragraph.HeadingLevel`(0=본문, 1~6) 추가. `SetHeading`(런 크기/굵기 적용+레벨 보존), HTML `h1~h6` 왕복. **실데이터에서 제목 완전 복원.**
- [x] **구분선 블록**: `DividerBlock : Block`(hr). 렌더(가로줄)·3개 히트테스트 높이전진·캐럿 인접 삭제·HTML/JSON 왕복. **실데이터에서 hr 완전 복원.**
- [x] **배경색(셀/문단)**: `Paragraph.Background` 추가(셀=Paragraph 재사용). 렌더 채움(셀·문단), HTML `td`/`p` `background-color`·`bgcolor` 왕복, JSON. **실데이터 최대 손실원(background 566→0)이던 표 셀 배경 해소.**

**현황(실데이터 왕복)**: 보이는 본문 서식(글꼴/크기/임의색/배경/정렬/표/이미지/목록/제목/hr)은 대부분 왕복. 남은 count 차이는 Jodit이 `p`·`td`·`span` 래퍼에 반복 지정한 **블록 레벨 중복 스타일**로, 시각적 손실 아님(완화된 목표 충족).
- [ ] (확인 후) 표 `colspan/rowspan` 필요하면 셀 모델 확장 — **범위 큰 항목, 실데이터로 필요성 먼저 판단**.
- 검증: 각 신규 속성/블록이 화면에 렌더되고 캐럿/선택/삭제가 정상.

### Phase 2 — HTML 무손실 왕복 *(핵심, 최대 난이도)*
- [ ] **`ToHtml` 전면 재작성**: 모든 모델 기능 출력
  - 인라인: 굵게/기울임/밑줄/취소선, **임의 색(hex)**, **배경색**, **글꼴 패밀리**, 크기, 링크.
  - 블록: 문단(정렬), **h1~h6/인용**, **ul/ol(중첩)**, **hr**, 표(열폭/행높이/정렬), **이미지(base64 data URL)**, 인라인 이미지.
- [ ] **`ParseHtml` 강화**: 인라인 `style=`(color/background/font-family/font-size/weight/style/decoration), `ol/ul`(중첩), `h1~h6`, `blockquote`, `hr`, `data:` 이미지 → Bitmap, 표 스타일/병합(범위 결정).
- [ ] **DOMPurify 대체**: 로드/붙여넣기 HTML 새니타이즈(스크립트/위험 속성 제거) — 파서가 화이트리스트라 자연 차단되나 명시적 정리 추가.
- [ ] 코퍼스 왕복이 **시각 손실 없음** 될 때까지 반복.
- 검증: Phase 0 하네스에서 코퍼스 통과율 목표치(예: 100% 시각 동등).

### Phase 3.5 — 외부 붙여넣기
> 실데이터가 복잡한 이유 = **HWP 문서에서 복사·붙여넣기**한 콘텐츠(셀 배경·pt 폰트·촘촘한 인라인 스타일).
- [x] **HWP/Excel HTML 경로**: CF_HTML로 들어오는 콘텐츠를 강화된 `ParseHtml`로 처리(글꼴/색/배경/크기pt/표/정렬 등).
- [x] **Excel TSV 폴백**: HTML이 없을 때 탭 구분 텍스트를 감지(`LooksTabular`)해 `TableBlock`으로 변환(`InsertTableFromTsv`).
- [x] **클립보드 버그 2건 수정 (실데이터 진단으로 발견)**:
  1. **HTML 포맷 미감지** — `TryGetHtmlAsync`/`TryGetImageAsync`가 `fmt.ToString()`로 매칭해 Excel/HWP의 `"HTML Format"`을 못 잡음 → `fmt.Identifier` 기준으로 수정. (CF_BITMAP이 `Bitmap` 객체로 오는 경우도 처리.)
  2. **Excel CF_HTML `<table>` 누락** — Excel은 fragment 마커를 `<table>` 안쪽에 두어 추출 조각에 `<tr>/<td>`만 있고 `<table>`이 없음 → `ParseHtml`에서 `<tr>` 있고 `<table>` 없으면 `<table>`로 감싸 보정.
  - 결과: **엑셀 셀 복사·한글 진짜 표 → 표로 붙여넣기.** (한글 글상자/도형은 `.gif` 이미지라 이미지로 — 정상)
- [ ] (향후) HWP 글상자/표를 표 구조로 받기 = 클립보드 **DOCX Format**(`<w:tbl>`) 파싱. HWP CSS 클래스 서식·VML 그림 보류.

### Phase 3 — 편집 기능 파리티 (UI/상호작용)
- [x] ul/ol 토글, **들여쓰기/내어쓰기**(`Paragraph.Indent`, margin-left 왕복).
- [x] 글꼴 패밀리 선택, **제목 드롭다운(H1~H3/본문)**, 배경색(형광펜), hr 삽입 — 툴바 + 컨텍스트 메뉴.
- [x] **이미지 붙여넣기(클립보드 비트맵)** + **드래그&드롭** + 다운스케일(최대 1920×1080).
- [x] **ReadOnly 모드**: 입력/리사이즈/붙여넣기 차단·캐럿 숨김·메뉴 축소, 선택/복사 허용.
- [x] **잔여 정리(Phase 2)**: blockquote(`Paragraph.IsQuote`, 좌측 바 렌더, `<blockquote>` 왕복), 중첩 목록 깊이(`Paragraph.ListLevel`, 재귀 `ParseList`/스택 기반 emit, 들여쓰기 렌더), **블록 정렬 읽기**(`ReadAlign` — `<p style=text-align>`가 소실되던 실버그 수정). 합성 코퍼스 01/02/03 완전 통과.

### Phase 4 — 인쇄 (우회 채택)
- [x] **(b) 임시 HTML → OS 기본 브라우저 인쇄**로 우회(`NativeEditor.PrintAsync`). 정밀 페이지네이션/PDF는 향후.

### Phase 5 — 호환 래퍼 & SaemDesk 통합
- [x] `NativeEditor : UserControl` — `JoditEditor` 동일 API: `Mode`(ReadOnly/Simple/Full), `Text`(HTML 양방향), `TextChanged`(LostFocus), `GetHtmlAsync`/`InsertHtmlAsync`/`PrintAsync`, Full 모드 내장 툴바.
- [ ] **SaemDesk 실통합** — 의도적으로 보류(현재 목표 = "통합 가능 수준"). 기능 플래그로 한 화면부터 교체는 추후.

### Phase 6 — 하드닝
- [x] **Native AOT 퍼블리시 확인 통과**(소스젠 JSON·무리플렉션, win-x64 네이티브 exe 생성). 잔여 경고는 기존 ViewLocator 트림 경고뿐.
- [x] 회귀 하네스 상시화(`--roundtrip`), 변경마다 확인.
- [ ] 대용량 base64 성능 정밀 측정 — 추후(현재 1.7MB 글도 동작).

---

## 2. 리스크 / 의사결정 필요

1. **저장 포맷**: **둘 다 지원(확정)** — JSON = 내부 정본(무손실, 완료), HTML = 교환/SaemDesk 호환용. `NativeEditor.Text`는 HTML(get=`ToHtml`/set=`ParseHtml`)을 노출하고, 별도로 JSON 저장/불러오기도 유지. → 그래도 SaemDesk 교체의 핵심은 HTML 무손실 왕복.
2. **표 병합(colspan/rowspan)**: 모델 미지원. 실데이터에 있으면 셀 모델 대수술 필요 → **코퍼스로 빈도 확인 후 결정**.
3. **인쇄 충실도**: 정확 인쇄가 필수인지/수준은? 방식(a/b/c) 선택을 좌우.
4. **롤아웃**: 기능 플래그 점진 교체 vs 일괄 교체.
5. **임의 CSS 충실도 한계**: Jodit이 생성하는 인라인 스타일 범위(폰트/색/표 스타일)만 보장, 그 외는 근사.

---

## 3. 권장 순서 & 마일스톤

1. **M1 (검증 가능 토대)**: Phase 0 + Phase 1 → "모델에 모든 개념이 있고 화면에 그려진다".
2. **M2 (교체 가능성 입증)**: Phase 2 → "실제 저장 HTML이 시각 손실 없이 왕복된다" ← **고/노고 분기점**.
3. **M3 (사용성)**: Phase 3 → "Jodit처럼 편집된다".
4. **M4 (드롭인)**: Phase 5 래퍼 + 한 화면 통합.
5. **M5**: Phase 4 인쇄 + Phase 6 하드닝 + 전면 롤아웃.

> 핵심 분기: **M2(HTML 왕복)**. 여기서 실데이터가 충실히 왕복되면 교체는 현실적이고, 안 되면 비용이 급증한다.
> 따라서 Phase 0의 코퍼스 수집과 왕복 하네스를 **가장 먼저** 한다.
