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
   │     └─ InlineImage       ← 글자처럼 취급되는 작은 이미지 (논리적으로 1글자)
   ├─ TableBlock              ← 표. 각 셀(TableCell.Blocks)은 블록 리스트(문단·이미지·구분선·중첩 표)
   ├─ ImageBlock              ← 독립 블록 이미지
   └─ DividerBlock            ← 수평 구분선 (<hr>)
```

핵심 불변식:

- **인라인 이미지 = 1글자.** 오프셋·길이 계산에서 `InlineImage`는 항상 1문자(개념상 U+FFFC)로 센다.
- **하나의 Paragraph가 여러 줄을 가질 수 있다.** `Run.Text` 안의 `\n`은 하드 줄바꿈이다(Shift+Enter 소프트 줄바꿈, 붙여넣기·로드로 유입). 리스트 문단에서는 줄마다 별도의 마커가 그려진다.
- **표 셀은 블록 리스트다(`TableCell.Blocks`).** 셀은 여러 문단·블록이미지·구분선·중첩 표를 담을 수 있다(마일스톤 A). 셀 안 Enter는 문단을 분할한다(하드 `\n` 아님). 호환을 위해 평범한 1문단 셀은 레거시 단일-문단 형식으로 직렬화되고, 다중 블록/비문단 셀만 `Type="Cell"` 래퍼로 인코딩된다(중첩 표는 블록 DTO 재귀).
- **로드 시 정규화(NormalizeBlocks).** 문서의 처음/끝, 그리고 연속한 비문단 블록 사이에는 문단이 보장되도록 에디터가 빈 Paragraph를 삽입할 수 있다. 따라서 *직렬화 → 역직렬화 → 직렬화*에서 빈 문단이 추가될 수 있다(내용 손실은 없음).

---

## 2. JSON 형식 (포맷 v1.0)

### 2.1 직렬화 일반 규칙

- 인코딩: UTF-8, `System.Text.Json` 소스 생성 컨텍스트(AOT 호환), 들여쓰기 출력.
- **null인 필드는 생략**된다(`WhenWritingNull`). 숫자/불리언 기본값(`0`, `false`, `"Indent": 0` 등)은 기록된다.
- **판독기는 모르는 필드를 무시해야 한다**(System.Text.Json 기본 동작). 전방 호환의 근거.
- 파싱 실패 시 `Deserialize`는 예외 대신 **빈 문서**를 반환한다.

### 2.2 루트: `FlowDocumentDto`

```jsonc
{
  "Version": "1.0",              // 포맷 버전(SemVer 문자열). 레거시 정수(1·2)도 읽음, 없으면 "1"
  "Blocks": [ /* BlockDto[] */ ],
  "Images": {                    // 이미지 풀 (이미지가 없으면 생략)
    "<SHA256 hex(대문자)>": { "Data": "<base64>", "MimeType": "image/jpeg" }
  }
}
```

#### 버전 이력

> **버전 표기**: 포맷 버전은 이제 **SemVer 문자열**(`"Version": "1.0"`)이다. 판독기는 로직에서 이 값을 분기하지 않으며(정보용 스탬프), **레거시 정수형(`1`·`2`)도 그대로 읽는다**(숫자/문자열 모두 허용). 필드가 없으면 `"1"`(레거시)로 간주.

| 버전 | 변경 | 읽기 호환 |
|---|---|---|
| (없음)/`1` | 초기 형식. 이미지는 블록마다 인라인 base64(`ImageBase64`) | 항상 지원(레거시 폴백) |
| `2` | 문서 수준 `Images` 풀 도입. 블록은 `ImageRef`(SHA-256 hex 키)로 참조. 동일 이미지 1회 저장 | v1 필드(`ImageBase64`, `IsListItem`)는 읽기 폴백 유지 |
| `"1.0"` (현재) | 안정 기준선. 정수→SemVer 표기 전환 + 이미지 풀 + **글자 크기 pt** + **비례 줄 간격(`LineSpacing`)** | 레거시 정수 버전 문서를 그대로 읽음 |

- 풀 키 = **원본 인코딩 바이트의 SHA-256, 대문자 16진 문자열** (`Convert.ToHexString`).
- 로드 시 풀 항목은 한 번만 디코드되고, 같은 키를 참조하는 모든 블록이 **동일한 `byte[]` 인스턴스를 공유**한다.
- 이미지 바이트는 **원본 인코딩 그대로**(JPEG는 JPEG로) 저장한다 — 재인코딩 금지. `RawBytes` 없이 Bitmap만 있는 이미지는 PNG로 1회 인코딩 후 풀에 합류한다.
- **글자 크기 단위 = pt**: `FontSize`는 **pt**로 저장된다(이전 px). 런타임 마이그레이션은 없다 — 베타 시점에 외부 저장 문서가 없어 호환 부담이 없기 때문. 레거시 정수 버전(px 시절)으로 저장된 구 문서는 같은 숫자를 pt로 읽어 약 33% 크게 보인다. 포맷 버전 `"1.0"`이 pt 기준 형식을 표시한다.

### 2.3 블록: `BlockDto`

블록은 `Type` 판별자를 가진 평면(flat) 객체다. 값: `"Paragraph"`(기본), `"Table"`, `"Image"`, `"Divider"`. 알 수 없는 `Type`은 Paragraph로 읽힌다.

#### 공통 필드 (모든 블록)

| 필드 | 타입 | 쓰기 | 읽기 기본값 |
|---|---|---|---|
| `Indent` | number | 항상 | 0 (왼쪽 여백 px) |
| `MarginTop` | number? | 항상 | 없으면 **0** |
| `MarginBottom` | number? | 항상 | 없으면 **10** (Divider는 0) — 여백 도입 이전 문서의 기존 룩 유지 |

#### `Type: "Paragraph"`

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Inlines` | InlineDto[] | 인라인 목록 (아래 §2.4) |
| `TextAlignment` | string | Avalonia `TextAlignment` 이름(`"Left"`/`"Center"`/`"Right"`/`"Justify"` 등). 파싱 실패 시 Left |
| `LineHeight` | number? | 절대 줄 높이 px("고정값"). 없으면 NaN(=미설정). `LineSpacing` 설정 시 무시됨 |
| `LineSpacing` | number? | 비례 줄 간격 배수(1.0=단일, 1.5=1.5줄, 2.0=2배 — HWP %÷100). 글자 크기에 비례. 없으면 NaN. `LineHeight`보다 우선 |
| `MarginRight` | number? | 오른쪽 여백 px(줄바꿈 폭 축소). **문단 전용**. 없으면 0 |
| `ListType` | string | `"None"`/`"Bullet"`/`"Ordered"`. 파싱 실패 시 레거시 `IsListItem` 참조 |
| `ListMarker` | string? | 글머리표/번호 모양: `Disc`/`Circle`/`Square`/`Dash`(글머리표), `Decimal`/`DecimalParen`/`LowerAlpha`/`UpperAlpha`/`LowerRoman`(번호). 없으면 `Default`(•/"1.") |
| `IsListItem` | bool | **v1 레거시, 읽기 전용 폴백**: `ListType` 없고 true면 Bullet |
| `ListLevel` | int | 중첩 리스트 깊이 (0=최상위) |
| `HeadingLevel` | int | 0=본문, 1~6=h1~h6 |
| `Background` | string? | 문단/셀 배경색 (색상 형식은 §2.5) |
| `IsQuote` | bool | 인용 블록(blockquote) 여부 |

#### `Type: "Image"` (블록 이미지)

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `ImageRef` | string? | `Images` 풀 키 (현행 작성 방식) |
| `ImageBase64` | string? | **v1 레거시 읽기 폴백**: 인라인 base64. `ImageRef`가 풀에서 해석되면 무시 |
| `MimeType` | string? | `ImageBase64` 바이트의 MIME. 없으면 `image/png`(레거시는 항상 PNG였음) |
| `Width`, `Height` | number? | 표시 크기 px. NaN이면 생략하고, 없으면 NaN(자연 크기, 렌더 폴백 200) |

#### `Type: "Table"`

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Rows`, `Columns` | int | 행/열 수. **로드 시 `Cells` 격자에서 재계산**되므로 참고값 |
| `ColumnWidths` | number[] | 열 너비 px (열 수만큼) |
| `RowHeights` | number[] | 행 최소 높이 px. **빈 배열 또는 0 = 자동(내용 높이)** |
| `Cells` | BlockDto[][] | 행 우선(row-major) **밀집 격자**. 평범한 1문단 셀은 Paragraph형 BlockDto(레거시 호환), 다중 블록·비문단 셀은 `Type:"Cell"` 래퍼(아래). 병합으로 가려진 칸도 자리는 유지 |
| `ColSpans`, `RowSpans` | int[][] | 셀 병합 격자(밀집, `Cells`와 같은 크기). 앵커 셀=병합 칸 수(평범한 셀은 1), **가려진(covered) 셀=0**. 없으면 전부 1×1 |

병합 규약: 병합 영역의 왼쪽-위 셀이 **앵커**이며 `ColSpans[r][c]`/`RowSpans[r][c]`에 병합 크기를 갖는다. 영역 내 나머지 칸은 두 배열 모두 0으로 마킹되고, 그 칸의 `Cells` 내용은 무시된다(빈 문단 권장). 격자는 항상 직사각형이어야 한다.

다중 블록 셀(`Type: "Cell"`): 한 셀이 여러 문단·블록이미지·구분선·중첩 표를 담으면 `Cells[r][c]`를 셀 래퍼로 인코딩한다.

| 필드 | 타입 | 의미 |
|---|---|---|
| `Type` | string | `"Cell"` |
| `Blocks` | BlockDto[] | 셀의 블록 리스트(재귀 — 중첩 `Type:"Table"` 포함 가능) |
| `Background` | string? | 셀 배경색 (§2.5) |

평범한 1문단 셀은 이 래퍼 없이 Paragraph형 BlockDto로 직렬화되어(배경은 그 DTO의 `Background`에) 구 판독기와 호환된다.

#### `Type: "Divider"`

공통 필드만 사용한다(수평선). `MarginBottom` 기본 0(높이 자체에 간격 포함).

### 2.4 인라인: `InlineDto`

`Type` 판별자: `"Run"`(기본) 또는 `"Image"`.

#### `Type: "Run"`

| 필드 | 타입 | 의미 / 읽기 규칙 |
|---|---|---|
| `Text` | string? | 텍스트. `\n` = 하드 줄바꿈 |
| `Bold`, `Italic` | bool | 굵게/기울임 |
| `FontSize` | number | 글자 크기 **pt**(이전 px). 기본 10, 읽을 때 ≤0이면 10. 렌더 시 ×4/3로 px 변환 |
| `FontFamily` | string? | 글꼴 이름. 없으면 에디터 기본 글꼴. **주의: OS가 현지화한 이름(예: "맑은 고딕")이 저장될 수 있어 다른 OS에서 해석되지 않을 수 있음** |
| `Foreground` | string? | 글자색 (§2.5). 없으면 기본(검정) |
| `Background` | string? | 형광펜 배경색 |
| `Underline`, `Strikethrough` | bool | 밑줄/취소선 (둘 다 가능) |
| `NavigateUri` | string? | 하이퍼링크 URL. 있으면 파랑+밑줄로 렌더, 에디터는 http/https만 연다 |

#### `Type: "Image"` (인라인 이미지)

블록 이미지와 동일한 `ImageRef`/`ImageBase64`/`MimeType` 규칙. `Width`/`Height` 없으면 16. 논리 텍스트에서 1글자를 차지한다.

### 2.5 색상 문자열

`Avalonia.Media.Color.ToString()` 출력 = **`#AARRGGBB`** 16진 문자열(예: 불투명 빨강 `#ffff0000`). 읽기는 `Color.Parse`이므로 `#RRGGBB`, 명명 색상(`"Red"`)도 허용되지만, **쓰기는 항상 `#AARRGGBB`로 통일**한다. 파싱 실패 시 null(기본색) 처리. 단색(SolidColorBrush)만 직렬화된다 — 그라데이션 등은 저장 시 탈락.

### 2.6 예제

```json
{
  "Version": "1.0",
  "Blocks": [
    {
      "Type": "Paragraph",
      "Inlines": [
        { "Type": "Run", "Text": "제목", "Bold": true, "Italic": false,
          "FontSize": 20, "Underline": false, "Strikethrough": false }
      ],
      "TextAlignment": "Left", "MarginTop": 0, "MarginBottom": 10,
      "ListType": "None", "HeadingLevel": 1, "Indent": 0,
      "IsQuote": false, "ListLevel": 0, "IsListItem": false
    },
    {
      "Type": "Image",
      "ImageRef": "A4DD28DB6E6D3FC0D43CDBEF1E8EF161B353CE67D27E81D400F796BC77045AE6",
      "Width": 640, "Height": 480,
      "Indent": 0, "MarginTop": 0, "MarginBottom": 10
    },
    {
      "Type": "Table", "Rows": 1, "Columns": 2,
      "ColumnWidths": [100, 100], "RowHeights": [],
      "Cells": [[
        { "Type": "Paragraph", "Inlines": [ { "Type": "Run", "Text": "셀1", "Bold": false, "Italic": false, "FontSize": 10, "Underline": false, "Strikethrough": false } ], "HeadingLevel": 0, "Indent": 0, "IsQuote": false, "ListLevel": 0, "IsListItem": false },
        { "Type": "Paragraph", "Inlines": [], "HeadingLevel": 0, "Indent": 0, "IsQuote": false, "ListLevel": 0, "IsListItem": false }
      ]],
      "ColSpans": [[1, 1]], "RowSpans": [[1, 1]],
      "Indent": 0, "MarginTop": 0, "MarginBottom": 10
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
- 로드 시: `document.json`이 없으면 빈 문서. `images/` 엔트리의 MIME은 풀 메타에서 읽고, 메타가 없으면 **매직 넘버 스니핑**(png/jpeg/gif/bmp/webp)으로 결정한다.
- 손상된 ZIP/JSON은 예외 없이 빈 문서를 반환한다.
- 파일 식별: 데모는 ZIP 매직 `PK`(0x50 0x4B) 스니핑으로 `.flow`와 일반 JSON을 구분한다.
- `meta.json`은 **컨테이너 포맷** 버전 마커다(`document.json`의 문서 포맷 버전과 같은 값으로 기록). 컨테이너 레이아웃이 독립적으로 진화할 여지를 남기고, 너무 새로운 패키지를 판독기가 구분할 수 있게 한다. 현재 판독기는 로직에서 사용하지 않으며 **없어도 무방**(이전 버전 `.flow`와 하위호환).

---

## 4. 호환성 정책

**판독기(reader) 의무**
- 모르는 JSON 필드는 무시한다.
- `Version`이 없으면 레거시(`"1"`)로 간주하고 폴백(`ImageBase64`, `IsListItem`)을 적용한다. 레거시 정수(`1`·`2`)와 SemVer 문자열(`"1.0"`)을 모두 읽는다.
- `Version`이 현재 지원 버전보다 커도 가능한 만큼 읽는다(현재 구현은 버전 검사로 거부하지 않음).

**작성기(writer) 의무**
- 항상 현재 포맷 버전(`DocumentSerializer.CurrentSchemaVersion` = `"1.0"`)을 기록한다.
- 이미지 바이트를 재인코딩하지 않는다(원본 보존). 풀 키는 반드시 바이트의 SHA-256 hex.
- 레거시 쓰기 필드(`ImageBase64`, `IsListItem`)는 **쓰지 않는다** (읽기 폴백 전용).

**스키마를 바꿀 때**
1. 기존 문서를 깨뜨리는 변경(필드 의미 변경·제거)이면 `CurrentSchemaVersion`을 올리고(SemVer 문자열 — 호환 깨짐은 메이저, 하위호환 추가는 마이너) 읽기 폴백을 추가한다. 필드 *추가*는 버전 증가 없이 가능하다(생략=기본값 규칙 유지).
2. 이 문서의 버전 이력 표(§2.2)와 필드 표를 갱신한다.
3. 왕복 테스트(`tests/`의 JSON/flow 라운드트립)와 레거시 로드 테스트를 추가한다.
