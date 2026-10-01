# AvaloniaRichEditor 문서 형식 명세 (Document Format Specification)

이 문서는 AvaloniaRichEditor가 문서를 저장/교환하는 두 가지 자체 형식을 정의한다.

| 형식 | 용도 | 진입점 API |
|---|---|---|
| **JSON 문자열** (포맷 v1.0) | 임베드·DB TEXT 컬럼·diff·이식 | `RichEditor.ToJson()` / `LoadJson()`, `DocumentSerializer.Serialize()` / `Deserialize()` |
| **`.flow` 패키지** (ZIP 컨테이너) | 파일로 주고받기 (base64 오버헤드 제거) | `RichEditor.SavePackageAsync()` / `LoadPackageAsync()`, `DocumentPackage.Save()` / `Load()` |

HTML 입출력(`ToHtml`/`LoadHtml`)은 **교환용**이며 손실이 있을 수 있다(예: 행 높이, 일부 여백). 무손실 보존이 필요하면 JSON/`.flow`를 사용한다.

> 구현 소스: [`DocumentSerializer.cs`](../src/AvaloniaRichEditor/Formatters/DocumentSerializer.cs), [`DocumentPackage.cs`](../src/AvaloniaRichEditor/Formatters/DocumentPackage.cs). 이 명세와 코드가 다르면 코드가 우선이고, 이 문서를 고친다.

---

## 1. 문서 모델 개요

직렬화 형식을 이해하려면 메모리 모델의 불변식을 알아야 한다.

```
FlowDocument
└─ Blocks: Block[]            ← 최상위 블록은 형제(sibling)로 평탄하게 나열
   ├─ Paragraph               ← 텍스트 문단 (리스트 항목·제목·인용 포함)
   │  └─ Inlines: Inline[]
   │     ├─ Run               ← 단일 서식의 연속 텍스트
   │     ├─ InlineImage       ← 글자처럼 취급되는 작은 이미지 (논리적으로 1글자)
   │     └─ InlineTable       ← 글자처럼 취급되는 표 (HWP식, 논리적으로 1글자) — TableBlock 래핑
   ├─ TableBlock              ← 표. 각 셀(TableCell.Blocks)은 블록 리스트(문단·이미지·구분선·중첩 표)
   ├─ ImageBlock              ← 독립 블록 이미지
   └─ DividerBlock            ← 수평 구분선 (<hr>)
```

핵심 불변식:

- **비-Run 인라인 = 1글자.** 오프셋·길이 계산에서 `InlineImage`·`InlineTable`은 항상 1문자(개념상 U+FFFC)로 센다(원자 객체). 인라인 표(마일스톤 B)는 `TableBlock`을 래핑해 글자처럼 줄 안에 흐르되 셀 편집·네비·리사이즈가 모두 동작한다.
- **하나의 Paragraph가 여러 줄을 가질 수 있다.** `Run.Text` 안의 `\n`은 하드 줄바꿈이다(Shift+Enter 소프트 줄바꿈, 붙여넣기·로드로 유입). 리스트 문단에서는 줄마다 별도의 마커가 그려진다.
- **표 셀은 블록 리스트다(`TableCell.Blocks`).** 셀은 여러 문단·블록이미지·구분선·중첩 표를 담을 수 있다(마일스톤 A). 셀 안 Enter는 문단을 분할한다(하드 `\n` 아님). 호환을 위해 평범한 1문단 셀은 레거시 단일-문단 형식으로 직렬화되고, 다중 블록/비문단 셀만 `Type="Cell"` 래퍼로 인코딩된다(중첩 표는 블록 DTO 재귀).
- **로드 시 정규화(NormalizeBlocks).** 문서의 처음/끝, 그리고 연속한 비문단 블록 사이에는 문단이 보장되도록 에디터가 빈 Paragraph를 삽입할 수 있다. 따라서 *직렬화 → 역직렬화 → 직렬화*에서 빈 문단이 추가될 수 있다(내용 손실은 없음).

---

## 2. JSON 형식 (포맷 v1.0)

### 2.1 직렬화 일반 규칙

- 인코딩: UTF-8, `System.Text.Json` 소스 생성 컨텍스트(AOT 호환). **들여쓰기 없음**(2026-10-01부터 — 이전 판은 들여썼다).
- **한글 등 비ASCII 글자는 이스케이프하지 않는다**(`JavaScriptEncoder.Create(UnicodeRanges.All)`). HTML에 민감한 `<`·`>`·`&`·`'`·`"`는
  여전히 `<` 식으로 이스케이프하므로 JSON을 웹 페이지에 끼워 넣어도 안전하다. (이전 판은 한글을 모두 `\uXXXX`로 썼다.)
- **판독기가 없을 때 가정하는 값과 같은 필드는 쓰지 않는다**(아래 필드 표의 "읽기 기본값"). `Type`이 `"Paragraph"`/`"Run"`이면
  `Type`도 생략한다. 이 규칙으로 쓴 문서는 1.0 이후의 **모든 판독기가 그대로 읽는다**(구 판독기로 직접 확인함 — §4).
- **판독기는 모르는 필드를 무시해야 한다**(System.Text.Json 기본 동작). 전방 호환의 근거.
- **손상된 입력은 예외다.** 유효하지 않은 JSON은 `JsonException`, `Blocks`가 없는 JSON(다른 앱의 파일)은 에디터의 로드 경로
  (`LoadJson`·`LoadJsonAsync`·`LoadPackageAsync`)에서 `JsonException`이다 — 빈 문서로 읽으면 호스트가 원본을 덮어쓰게 되기 때문.
  리터럴 `null`은 빈 문서다.

### 2.2 루트: `FlowDocumentDto`

```jsonc
{
  "Version": "1.0",              // 포맷 버전(SemVer 문자열). 레거시 정수(1·2)도 읽음, 없으면 "1"
  "Blocks": [ /* BlockDto[] */ ],
  "Images": {                    // 이미지 풀 (이미지가 없으면 생략)
    "<SHA256 hex(대문자)>": { "Data": "<base64>", "MimeType": "image/jpeg" }
  },
  "PageSetup": {                 // 문서 페이지 설정 (기본값과 같으면 생략 — 아래 참고)
    "PageSize": "A4",            // enum 이름. 기본 "Continuous"(폭에 맞춰 reflow)
    "Orientation": "Portrait",   // "Portrait" | "Landscape" (Continuous에서는 무의미)
    "ShowPageBoundaries": true,
    "Header": null,              // 머리글 텍스트(없으면 생략)
    "Footer": null,              // 바닥글 텍스트(없으면 생략)
    "ShowPageNumbers": false,
    "MarginLeft": 25,            // 페이지 여백 **mm**, 변마다 하나. 기본값이면 생략
    "MarginTop": 20,             // 기본 사방 15mm
    "MarginRight": 25,
    "MarginBottom": 20
  }
}
```

- **여백(`Margin*`)**: 용지 가장자리와 본문 사이의 띠(머리글·바닥글·쪽번호가 그려지는 곳). 단위는 **밀리미터**다 — 용지 크기가 mm로 정의되고 Word·아래한글도 mm로 보여 주며, RTF는 물리 길이(twips)로 싣는다(렌더는 96dpi DIP로 환산: 1mm = 96/25.4 ≈ 3.7795px). 변마다 하나이며 **기본값(사방 15mm)이면 생략**되므로 여백을 건드리지 않은 문서의 바이트는 그대로다. 일부 변만 있으면 나머지는 기본값. **본문을 놓을 자리가 남지 않는 값**(음수·NaN·무한대, 또는 마주 보는 두 변의 합이 용지보다 큼)은 한 변만 고치지 않고 **네 변 모두 기본값으로 되돌린다** — 파일이 뜻한 바가 아니므로 절반만 적용하지 않는다.
  - RTF는 정수 twips라 mm가 정확히 왕복되지 않는다. 0.1mm 단위 값은 그대로 돌아오고(15mm → 850 → 15), 그 밖의 값은 정확한 길이를 유지한다(Word의 1.25인치 = 31.75mm).
- **`PageSetup`(선택)**: 워드프로세서식 페이지 설정. 로드 시 에디터의 용지/방향/머리글·바닥글/쪽번호 속성에 적용되고, 이후 페이지 속성을 바꾸면 문서로 다시 캡처된다. **기본 상태(용지 `Continuous`, 머리글/바닥글/쪽번호 없음)면 통째로 생략**되므로 평범한 문서의 바이트는 이전과 동일하다. 열거값은 이름으로 직렬화되어 미래의 알 수 없는 값은 기본값으로 안전하게 강등된다. 이 필드를 모르는 (구) 판독기는 무시한다 — 추가 필드라 버전 증가 없음.

#### 버전 이력

> **버전 표기**: 포맷 버전은 **SemVer 문자열**(`"Version": "1.0"`)이다. **레거시 정수형(`1`·`2`)도 그대로 읽는다**(숫자/문자열 모두 허용, 둘 다 `"1.0"`보다 오래된 형식). 필드가 없으면 `"1"`(레거시)로 간주.
> 판독기가 분기하는 것은 **메이저**뿐이다: 에디터의 로드 경로는 메이저가 지원 범위(현재 1)보다 큰 문서를 `JsonException`으로 거부한다(§4). 공개 `DocumentSerializer.Deserialize`는 관대하게 읽는다.

| 버전 | 변경 | 읽기 호환 |
|---|---|---|
| (없음)/`1` | 초기 형식. 이미지는 블록마다 인라인 base64(`ImageBase64`) | 항상 지원(레거시 폴백) |
| `2` | 문서 수준 `Images` 풀 도입. 블록은 `ImageRef`(SHA-256 hex 키)로 참조. 동일 이미지 1회 저장 | v1 필드(`ImageBase64`, `IsListItem`)는 읽기 폴백 유지 |
| `"1.0"` (현재) | 안정 기준선. 정수→SemVer 표기 전환 + 이미지 풀 + **글자 크기 pt** + **비례 줄 간격(`LineSpacing`)** | 레거시 정수 버전 문서를 그대로 읽음 |
| `"1.0"` (2026-10-01 작성기) | **스키마 변경 없음.** 작성기가 기본값 필드·기본 `Type`·1뿐인 병합 격자·`Rows`/`Columns`를 생략하고, 들여쓰지 않고, 한글을 이스케이프하지 않는다. 같은 문서가 4~12분의 1(corpus 실측 240.6 → 37.7 KB, 3,043 → 378 KB) | 1.0 이후 모든 판독기가 그대로 읽음 |

- 풀 키 = **원본 인코딩 바이트의 SHA-256, 대문자 16진 문자열** (`Convert.ToHexString`).
- 로드 시 풀 항목은 한 번만 디코드되고, 같은 키를 참조하는 모든 블록이 **동일한 `byte[]` 인스턴스를 공유**한다.
- 이미지 바이트는 **원본 인코딩 그대로**(JPEG는 JPEG로) 저장한다 — 재인코딩 금지. `RawBytes` 없이 Bitmap만 있는 이미지는 PNG로 1회 인코딩 후 풀에 합류한다.
- **글자 크기 단위 = pt**: `FontSize`는 **pt**로 저장된다(이전 px). 런타임 마이그레이션은 없다 — 베타 시점에 외부 저장 문서가 없어 호환 부담이 없기 때문. 레거시 정수 버전(px 시절)으로 저장된 구 문서는 같은 숫자를 pt로 읽어 약 33% 크게 보인다. 포맷 버전 `"1.0"`이 pt 기준 형식을 표시한다.

### 2.3 블록: `BlockDto`

블록은 `Type` 판별자를 가진 평면(flat) 객체다. 값: `"Paragraph"`(기본 — 쓰지 않는다), `"Table"`, `"Image"`, `"Divider"`. 알 수 없는 `Type`(같은 메이저의 더 새 판이 만든 블록)은 그 텍스트를 가진 Paragraph로 읽히고 `RichEditorDiagnostics`에 `NotSupportedException`으로 보고된다 — 그 문서를 저장하면 그 블록은 빠진다.

#### 공통 필드 (모든 블록)

아래 모든 표에서 **"읽기 기본값"과 같은 값은 쓰지 않는다**.

| 필드 | 타입 | 읽기 기본값 |
|---|---|---|
| `Indent` | number? | 0 (왼쪽 여백 px) |
| `MarginTop` | number? | 문단 **0**. 표·이미지·구분선은 **자동**(NaN = `Block.AutoTopMargin`, 윗글에서 한 줄 간격) — 그래서 이 셋은 자동일 때 생략, 0을 포함한 명시 값은 기록 |
| `MarginBottom` | number? | 문단 **0**(HWP처럼 줄 간격만으로 구분), 이미지·표 **10**, Divider **0** |

#### `Type: "Paragraph"`

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Inlines` | InlineDto[] | 인라인 목록 (아래 §2.4) |
| `TextAlignment` | string | Avalonia `TextAlignment` 이름(`"Left"`/`"Center"`/`"Right"`/`"Justify"` 등). 파싱 실패 시 Left |
| `LineHeight` | number? | 절대 줄 높이 px("고정값"). 없으면 NaN(=미설정). `LineSpacing` 설정 시 무시됨 |
| `LineSpacing` | number? | 비례 줄 간격 = HWP "글자에 따라" %÷100. 줄 높이 = 문단의 가장 큰 글자 크기 × 값(1.0=100%=글자 크기, 1.6=160%). Word "배수"와 다름. 없으면 NaN → `LineHeight`, 그것도 없으면 HWP 기본 160%. `LineHeight`보다 우선 |
| `MarginRight` | number? | 오른쪽 여백 px(줄바꿈 폭 축소). **문단 전용**. 없으면 0 |
| `ListType` | string | `"None"`/`"Bullet"`/`"Ordered"`. 파싱 실패 시 레거시 `IsListItem` 참조 |
| `ListMarker` | string? | 글머리표/번호 모양: `Disc`/`Circle`/`Square`/`Dash`(글머리표), `Decimal`/`DecimalParen`/`LowerAlpha`/`UpperAlpha`/`LowerRoman`(번호). 없으면 `Default`(•/"1.") |
| `IsListItem` | bool? | **v1 레거시, 읽기 전용 폴백**: `ListType` 없고 true면 Bullet. 쓰지 않는다 |
| `ListLevel` | int? | 중첩 리스트 깊이 (0=최상위). 읽을 때 0~8로 자른다 |
| `HeadingLevel` | int? | 0=본문, 1~6=h1~h6 |
| `Background` | string? | 문단/셀 배경색 (색상 형식은 §2.5) |
| `IsQuote` | bool? | 인용 블록(blockquote) 여부 |

#### `Type: "Image"` (블록 이미지)

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `ImageRef` | string? | `Images` 풀 키 (현행 작성 방식) |
| `ImageBase64` | string? | **v1 레거시 읽기 폴백**: 인라인 base64. `ImageRef`가 풀에서 해석되면 무시 |
| `MimeType` | string? | `ImageBase64` 바이트의 MIME. 없으면 `image/png`(레거시는 항상 PNG였음) |
| `Width`, `Height` | number? | 표시 크기 px. NaN이면 생략하고, 없으면 NaN(자연 크기, 렌더 폴백 200) |
| `Alt` | string? | 대체 텍스트(접근성 설명). null이면 생략 |

#### `Type: "Table"`

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Rows`, `Columns` | int? | 행/열 수. **쓰지 않는다** — 판독기는 `Cells` 격자에서 재계산한다(구 파일의 `Columns`는 열 수의 하한으로만 쓰임) |
| `ColumnWidths` | number[] | 열 너비 px (열 수만큼) |
| `RowHeights` | number[]? | 행 최소 높이 px. **없음·빈 배열·0 = 자동(내용 높이)**. 비어 있으면 쓰지 않는다 |
| `Cells` | BlockDto[][] | 행 우선(row-major) **밀집 격자**. 평범한 1문단 셀은 Paragraph형 BlockDto(레거시 호환), 다중 블록·비문단 셀은 `Type:"Cell"` 래퍼(아래). 병합으로 가려진 칸도 자리는 유지. 행이 하나도 없는 표는 읽지 않는다 |
| `ColSpans`, `RowSpans` | int[][]? | 셀 병합 격자(밀집, `Cells`와 같은 크기). 앵커 셀=병합 칸 수(평범한 셀은 1), **가려진(covered) 셀=0**. 없으면 전부 1×1 — **병합이 없는 표는 쓰지 않는다** |

가져오기 상한(신뢰할 수 없는 입력, 2026-10-01 라운드35): 표는 **1,000열·25만 셀**까지 읽고 넘는 셀은 버린다(가장 넓은 행이 모든 행을 채우는 증폭 방지).

병합 규약: 병합 영역의 왼쪽-위 셀이 **앵커**이며 `ColSpans[r][c]`/`RowSpans[r][c]`에 병합 크기를 갖는다. 영역 내 나머지 칸은 두 배열 모두 0으로 마킹되고, 그 칸의 `Cells` 내용은 무시된다(빈 문단 권장). 격자는 항상 직사각형이어야 한다.

다중 블록 셀(`Type: "Cell"`): 한 셀이 여러 문단·블록이미지·구분선·중첩 표를 담으면 `Cells[r][c]`를 셀 래퍼로 인코딩한다.

| 필드 | 타입 | 의미 |
|---|---|---|
| `Type` | string | `"Cell"` |
| `Blocks` | BlockDto[] | 셀의 블록 리스트(재귀 — 중첩 `Type:"Table"` 포함 가능) |
| `Background` | string? | 셀 배경색 (§2.5) |

평범한 1문단 셀은 이 래퍼 없이 Paragraph형 BlockDto로 직렬화되어(배경은 그 DTO의 `Background`에) 구 판독기와 호환된다.

셀 세로 정렬 `VAlign`(string?): `"Center"`/`"Bottom"`. 두 형태(래퍼·Paragraph형) 모두 셀 DTO에 `Background`처럼 붙는다. 기본값 Top은 생략하고, 없거나 파싱 실패면 Top.

#### `Type: "Divider"`

공통 필드만 사용한다(수평선). `MarginBottom` 기본 0(높이 자체에 간격 포함).

### 2.4 인라인: `InlineDto`

`Type` 판별자: `"Run"`(기본 — 쓰지 않는다), `"Image"`, 또는 `"Table"`. 알 수 없는 `Type`은 그 `Text`를 가진 Run으로 읽고 보고한다(블록과 같다).

#### `Type: "Run"`

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Text` | string? | 텍스트. `\n` = 하드 줄바꿈 |
| `Bold`, `Italic` | bool? | 굵게/기울임. 없으면 false |
| `FontSize` | number? | 글자 크기 **pt**(이전 px). 없거나 ≤0이면 10. 렌더 시 ×4/3로 px 변환 |
| `FontFamily` | string? | 글꼴 이름. 없으면 에디터 기본 글꼴. **주의: OS가 현지화한 이름(예: "맑은 고딕")이 저장될 수 있어 다른 OS에서 해석되지 않을 수 있음** |
| `Foreground` | string? | 글자색 (§2.5). 없으면 기본(검정) |
| `Background` | string? | 형광펜 배경색 |
| `Underline`, `Strikethrough` | bool? | 밑줄/취소선 (둘 다 가능). 없으면 false |
| `NavigateUri` | string? | 하이퍼링크 URL. 다른 장식(밑줄·취소선)이 없으면 밑줄로 렌더하고, 색은 `Foreground`를 따른다. 에디터는 http/https만 연다 |

#### `Type: "Image"` (인라인 이미지)

블록 이미지와 동일한 `ImageRef`/`ImageBase64`/`MimeType`/`Alt` 규칙. `Width`/`Height` 없으면 16. 논리 텍스트에서 1글자를 차지한다.

#### `Type: "Table"` (인라인 표, 마일스톤 B)

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Table` | BlockDto | 래핑된 표를 블록 표(`Type:"Table"`, §2.3)와 동일한 DTO로 직렬화 — 중첩 표·다중 블록 셀·스팬·열폭이 같은 재귀 규칙으로 왕복한다 |

논리 텍스트에서 1글자(U+FFFC)를 차지한다. HTML 내보내기는 `<table>`로(인라인 개념이 없어 베스트에포트), JSON/`.flow`는 위 `Table` DTO로 무손실 보존한다.

### 2.5 색상 문자열

`Avalonia.Media.Color.ToString()` 출력 = **`#AARRGGBB`** 16진 문자열(예: 불투명 빨강 `#ffff0000`). 읽기는 `Color.Parse`이므로 `#RRGGBB`, 명명 색상(`"Red"`)도 허용되지만, **쓰기는 항상 `#AARRGGBB`로 통일**한다. 파싱 실패 시 null(기본색) 처리. 단색(SolidColorBrush)만 직렬화된다 — 그라데이션 등은 저장 시 탈락.

### 2.6 예제

실제 출력은 한 줄이다(들여쓰기 없음). 읽기 쉽도록 줄을 나눠 보인다. 생략된 필드는 모두 기본값이다.

```json
{
  "Version": "1.0",
  "Blocks": [
    { "Inlines": [ { "Text": "제목", "Bold": true, "FontSize": 20 } ], "MarginBottom": 10, "HeadingLevel": 1 },
    {
      "Type": "Image",
      "ImageRef": "A4DD28DB6E6D3FC0D43CDBEF1E8EF161B353CE67D27E81D400F796BC77045AE6",
      "Width": 640, "Height": 480,
      "MarginTop": 0
    },
    {
      "Type": "Table",
      "ColumnWidths": [100, 100],
      "Cells": [[ { "Inlines": [ { "Text": "셀1" } ] }, { "Inlines": [] } ]]
    }
  ],
  "Images": {
    "A4DD28DB6E6D3FC0D43CDBEF1E8EF161B353CE67D27E81D400F796BC77045AE6": {
      "Data": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
      "MimeType": "image/png"
    }
  }
}
```

---

## 3. `.flow` 패키지 형식

표준 **ZIP 컨테이너**다(`System.IO.Compression`, 외부 의존성 없음). JSON 문자열 계약을 치환하지 않고 그 위에 얹힌 파일 교환 계층이다.

```
*.flow (ZIP)
├─ meta.json            ← 컨테이너 포맷 마커: {"format":"flow","version":"1.0"} (Deflate)
├─ document.json        ← §2의 JSON과 동일 스키마. 단, Images 풀 항목에 Data(base64)가 없고
│                          MimeType만 남는다 (Deflate 압축)
└─ images/<SHA256 hex>  ← 원본 인코딩 바이트. 엔트리 이름 = 풀 키 (무압축 Stored)
```

규칙:

- `document.json`의 `Images[키]`와 `images/키` 엔트리가 1:1 대응한다. base64가 빠지므로 같은 문서의 JSON 대비 약 25% 작다.
- 이미지 엔트리는 이미 압축된 형식(JPEG/PNG)이므로 **무압축(Stored)** 으로 저장한다.
- 로드 시: `images/` 엔트리의 MIME은 풀 메타에서 읽고, 메타가 없거나 평범한 `image/…` 형식이 아니면 **매직 넘버 스니핑**(png/jpeg/gif/bmp/webp)으로 결정한다.
- 로드 시 **문서가 참조하는 `images/` 엔트리만** 읽고, 엔트리 하나가 256 MB를 넘으면 읽지 않는다(zip 폭탄 방지, 라운드35).
- **손상은 예외다**: ZIP이 아니거나 깨졌으면 `InvalidDataException`, `document.json`이 없으면(다른 zip — .docx 등) `InvalidDataException`, `document.json`이 유효한 JSON이 아니면 `JsonException`.
- 파일 식별: 데모는 ZIP 매직 `PK`(0x50 0x4B) 스니핑으로 `.flow`와 일반 JSON을 구분한다.
- `meta.json`은 **컨테이너 포맷** 버전 마커다(`document.json`의 문서 포맷 버전과 같은 값으로 기록). 컨테이너 레이아웃이 독립적으로 진화할 여지를 남기고, 너무 새로운 패키지를 판독기가 구분할 수 있게 한다. 현재 판독기는 로직에서 사용하지 않으며 **없어도 무방**(이전 버전 `.flow`와 하위호환).

---

## 4. 호환성 정책

**판독기(reader) 의무**
- 모르는 JSON 필드는 무시한다.
- `Version`이 없으면 레거시(`"1"`)로 간주하고 폴백(`ImageBase64`, `IsListItem`)을 적용한다. 레거시 정수(`1`·`2`)와 SemVer 문자열(`"1.0"`)을 모두 읽는다.
- `Version`의 **메이저가 더 크면 에디터의 로드 경로는 거부한다**(`JsonException`) — 모르는 것을 빈 문단으로 바꿔 읽고, 저장하면 그걸 원본에 덮어쓰게 되기 때문. 같은 메이저의 더 새 판(예: `"1.4"`)은 가능한 만큼 읽고, 모르는 블록·인라인 `Type`은 보고한다. 공개 `Deserialize`는 메이저와 무관하게 관대하다.
- 없는 필드는 아래 필드 표의 "읽기 기본값"으로 읽는다. **이 기본값은 작성기가 생략에 쓰는 값과 같아야 한다** — 바꾸면 기존 파일의 의미가 바뀐다.

**작성기(writer) 의무**
- 항상 현재 포맷 버전(`DocumentSerializer.CurrentSchemaVersion` = `"1.0"`)을 기록한다.
- 판독기의 기본값과 같은 필드는 쓰지 않는다(§2.1). 2026-10-01 이전의 작성기는 모든 필드를 들여 써서 기록했다 — 두 형태 모두 같은 스키마이고, 판독기는 둘 다 읽는다(구 형태: `tests/AvaloniaRichEditor.Tests/Fixtures/format-1.0-verbose-kitchen-sink.json`).
- 이미지 바이트를 재인코딩하지 않는다(원본 보존). 풀 키는 반드시 바이트의 SHA-256 hex.
- 레거시 쓰기 필드(`ImageBase64`, `IsListItem`)는 **쓰지 않는다** (읽기 폴백 전용).

**구 판독기 호환을 확인한 방법(2026-10-01)**: 변경 전 트리가 문서 18개(`FormatFixpointTests`의 14종 + corpus 실문서 4개)를 쓰고 → 새 작성기가 다시 쓰고 → 변경 전 트리가 그걸 읽어 다시 쓴 결과가 자기 출력과 **바이트 단위로 같았다**(JSON·`.flow` 모두). 작성기의 생략 규칙을 넓힐 때는 같은 확인을 다시 할 것.

**스키마를 바꿀 때**
1. 기존 문서를 깨뜨리는 변경(필드 의미 변경·제거)이면 `CurrentSchemaVersion`을 올리고(SemVer 문자열 — 호환 깨짐은 메이저, 하위호환 추가는 마이너) 읽기 폴백을 추가한다. 필드 *추가*는 버전 증가 없이 가능하다(생략=기본값 규칙 유지).
2. 이 문서의 버전 이력 표(§2.2)와 필드 표를 갱신한다.
3. 왕복 테스트(`tests/`의 JSON/flow 라운드트립)와 레거시 로드 테스트를 추가한다.
