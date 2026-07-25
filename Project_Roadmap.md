# Avalonia RichTextBox Port — Roadmap & Status

WPF의 `RichTextBox`/`FlowDocument`를 **순수 C# + Avalonia `TextLayout`**로 바닥부터 이식하는 프로젝트.
PTS(비관리형 C++)를 못 쓰므로 렌더·레이아웃·히트테스트·선택·IME를 직접 구현한다.

> 📜 **상세 이력**(완료된 Phase·N·마일스톤의 날짜별 작업 로그)은 [`docs/roadmap-archive.md`](docs/roadmap-archive.md)로 옮겼다. 릴리스별 변경은 [`CHANGELOG.md`](CHANGELOG.md).

---

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

> 🚧 **라운드2 (진행 중, 미릴리스)** — WinUI 포트가 0.9.0 이후 앞서간 분량. 상세는 [`CHANGELOG.md`](CHANGELOG.md) Unreleased.
> - 기능 5종: `IsModified`/`MarkSaved`, `RemoveList`, `AutoLinkOnType`, `AllowRemoteImagesOnPaste`, 찾기 highlight-all
> - **전 소스 정독 감사에서 찾은 결함 5건 수정**(단축키 충돌·문단 서식 유실·셀 병합 데이터 손실·문서순서 비교·찾기 하이라이트가 선택색 덮음), 테스트 9건 추가
> - **2b(WinUI 버그 백포트 중 Avalonia 적용 확인분 3건)**: 셀 안 목록 마커 미렌더(→ `CellParaLeft`를 렌더·히트테스트·링크 히트테스트·캐럿·측정 **다섯 walk에 동일 적용**, 규칙 #1), 리사이즈 핸들 클릭만으로 `IsModified` 뒤집힘(→ 첫 실제 이동 때 스냅샷), `FindCell` O(문서) 스캔 → 부모 체인, 셀 안 다중 문단 선택 시 목록 명령이 첫 문단에만 적용(→ 선택 수집을 컨테이너 무관하게). 테스트 4건(+픽셀 1건)
> - **2b 잔여분 전수 대조 완료**: WinUI 나머지 10건을 Avalonia 코드와 1:1 확인 → **추가 수정 4건**(HTML `font-weight` 오탐, RTF 표 안 그림이 셀 탈출, 인라인 표 셀 미정규화(규칙 #5), `LoadHtml`/`InsertHtml`의 `AllowRemoteImagesOnPaste` 미전달). **해당 없음 5건**: 셀 다중문단 붙여넣기(이미 컨테이너 일반화), `SplitByNewlines` 서식 소실(라운드1에서 수정), undo 바이트 예산(Avalonia엔 예산 자체가 없음·50개 제한), Shift+Enter 캐럿(실측 정상), 표 행 높이 stale(`_trustLayoutCache`가 편집 후 자동 무효화). Win2D 누수 1건은 플랫폼 전용.
> - **표 안 Ctrl+A 단계 선택 추가**(HWP/Excel식: 셀 내용 → 표 전체 → (중첩이면 바깥 표) → 문서. 단계는 현재 선택에서 역산 — 클릭/화살표가 끼면 자동 리셋)
> - **남은 것(설계 판단 필요)**: ↑/↓로 표 **셀 진입** 불가(현재는 블록 캐럿에 멈췄다가 표를 건너뜀 — 의도된 모델인지 결정 필요), IME 조합 중 셀 행 높이가 안 자람(measure 경로가 preedit 미인지), 동기 `ParseHtml`이 원격 이미지를 **동기 다운로드**(최대 5초 UI 정지 — WinUI는 동기 경로에서 네트워크를 뺐음)

기능 충실도 **A**, 코드 품질 **B+**, 견고성/검증 **B**, 프로덕션 준비도 **B−(베타)**.
혼자 만든 from-scratch Avalonia 리치 에디터로는 상위권 — 기능은 상용 근접, 1.0은 *새 기능이 아니라 검증 깊이*로 결정.

**완성된 기능 (전부 동작·테스트 그린, 354 unit + 9 render):**
- 인라인 서식(굵게/기울임/밑줄/취소선·글꼴·크기·색·형광펜·하이퍼링크), 문단(정렬·줄간격·들여쓰기·제목·리스트·인용)
- **표**: 셀 병합(colspan/rowspan), 열/행 리사이즈, Tab 내비, **셀=블록 컨테이너**(다중 문단·블록이미지·구분선·**중첩 표**), **인라인 표**(HWP식 "글자처럼 취급", 완전 편집), **드래그 크기 지정 삽입**
- 인라인/블록 **이미지**(삽입·리사이즈·교체·저장), 찾기/바꾸기, undo/redo, 우클릭 메뉴
- 클립보드(내부 리치·CF_HTML 복사·외부 HTML/RTF 붙여넣기·이미지·Excel/TSV→표), **HTML/JSON/RTF/.flow 입출력**
- CJK **IME**(인라인 preedit), 워드식 **페이지 보기**, **인쇄·래스터 PDF**
- 드롭인 **`RichEditorView`**(에디터+툴바+상태바; 툴바에 페이지/줌·파일액션 내장), `ToolbarLevel` 밀도, 능력=`IsReadOnly`+`Allow*`(EditorMode 없음), 현지화(ko/en)

**완료된 마일스톤** (상세는 아카이브):
| 마일스톤 | 내용 |
|---|---|
| Phase 1~6 | 모델·렌더 엔진 → 편집·선택·서식 → 클립보드·포매터 → 상용 수준 기능 |
| 🖨️ P-마일스톤 | A4 페이지 보기 + 인쇄/PDF (갭 주입 방식) |
| 📦 N0~N6 | NuGet 패키징·공개 API·CI·에디터 모드·툴바·이미지 저장 모델 → alpha→beta→0.7.0→0.8.0→**0.9.0** |
| 🟢 마일스톤 A | 셀 안에 블록(재귀 프리미티브·중첩 표) |
| 🟢 마일스톤 B | 인라인 표(글자처럼 취급) — 본 릴리스 |
| 🟢 G1 (사실상 완료) | 기하 워커 통합(`BlockExtent` 단일 출처 → 워커 드리프트 버그 클래스 제거). 남은 P2(BlockBox 캐싱 열거자)는 성능 선택, 비병목이라 미착수 |

---

## 🎯 1.0까지 남은 일 (기능이 아니라 *검증·상호운용*)

**검증 게이트**
- [ ] **렌더 픽셀 테스트 깊이** — `Tests.Render`(real Skia)가 CI에 있으나, 복잡 표·페이지 분할·인라인 표까지 픽셀 단언 확대
- [ ] **mac/Linux 기능 실검증** — 3-OS CI는 그린이나 실기 기능 확인은 미완(헤드리스 한계: 이미지 디코드 등)
- [ ] **대형 문서 성능 실측** — `--bench`를 인라인 표 포함으로 재측정(편집 경로는 실측상 sub-ms, 병목 아님)

**상호운용 격차** (베스트에포트 → 정밀화)
- [ ] **RTF 표/인라인 표 내보내기** — 현재 HTML만 표/인라인 표를 보존(HWP는 RTF 선호)
- [ ] **HTML 인라인 표 재가져오기** — 현재 블록 표로 들어옴(HTML에 인라인 개념 없음; in-app 메타로 보존 가능)

**견고성/성능 후속** (의도적 보류 — 측정상 저가치, 아카이브 N5/리뷰 절 참조)
- 델타 Undo(실측 기각), `ComputePageBreaks` 재계산 캐시, HTML `HasBlockOrMedia` O(n²), `ReplaceAll` O(N²) — 모두 희귀 경로 + 무효화 위험

---

## 🔵 백로그 (착수 미정)

- **벡터(선택 가능) PDF** — content stream + CJK 폰트 서브셋팅이 난제. "무의존성+AOT" 방침과 충돌 → 현 래스터 PDF가 합리적 v1
- **단락 경계 넘는 Find** — 줄바꿈 포함 검색어만 해당, 실사용 극저
- **HWP/XLS(구) 붙여넣기**, blockquote/중첩목록 깊이 정밀화
- **인라인 객체 공통 추상(`InlineObject`)** — 3번째 인라인 타입이 실제로 생기면 이미지+표 베이스 추출(현재는 표 전용 라우팅)

---

## 📌 작업 규칙

- 진행 상황·보류 항목은 **이 파일**을 먼저 확인하고, 작업 후 갱신한다.
- 각 단계는 독립 출하 + 테스트 그린 유지. 위험 경로(렌더)는 데모 GUI 육안검증.
- 비자명한 엔진 규칙은 [`CLAUDE.md`](CLAUDE.md) "비자명한 핵심 규칙" 참조.
