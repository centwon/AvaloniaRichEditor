# rtfgen — 외부 앱 상호운용 재현 도구

이 프로젝트의 **reader는 이 프로젝트의 writer에 관대하다.** 그래서 자체 왕복 테스트는 자기 자신과 합의할
뿐이고, 나가는 바이트가 Word·HWP·브라우저에게 무엇을 말하는지는 **보지 못한다.** 라운드 4~9의 결함이
반복해서 그 자리에서 나왔다.

이건 그 사각지대를 겨냥한 도구다. 솔루션(`AvaloniaRichEditor.slnx`)에는 **일부러 넣지 않았다** —
빌드·CI 대상이 아니라 필요할 때 손으로 돌리는 개발 도구다.

## 1. 문서 생성

```
dotnet run --project tools/rtfgen
```

`tools/rtfgen/bin/Debug/net10.0/out/`에 `.rtf` 4종 + `.html` 4종. 라운드 4~9가 바꾼 출력만 모았고,
**문서 안에 절 번호와 "…이면 실패" 문구가 들어 있어** 체크리스트 없이도 화면만 보고 판정할 수 있다.

| 파일 | 다루는 것 |
|---|---|
| `01-kitchen-sink` | 연속 공백 · 링크 색 · 목록/마커 · 빈 줄 · 문단 서식 · 이미지 · 구분선 · 인라인 표 |
| `02-empty` | 빈 문서 저장 |
| `03-tables` | 테두리 · 병합 · 셀 배경 · 셀 안 서식/다중 문단/이미지 · 중첩 표 · 표 뒤 이미지 |
| `04-page-chrome` | 머리글/바닥글/쪽번호 · 용지 크기 (A4, 2페이지 이상) |

## 2. Word로 자동 측정

```
powershell -File tools/rtfgen/Verify-Word.ps1
```

Word COM으로 열어 **객체 모델에 질문한다**(`Range.Cells`, `Borders.LineStyle`, `Headers(1).Range.Text`,
`ComputeStatistics`). 28항목 PASS/FAIL. 스크린샷 판독보다 정확하다 — 실제로 육안으로는 링크 색을
잘못 봤고 측정이 맞았다.

### 함정 3가지 (전부 실측으로 배운 것)

- ⚠️ **읽기 전용으로만 쓸 것.** `Documents.Add` + `SaveAs2`(Word에게 참조 파일을 쓰게 하기)는 헤드리스에서
  **멈춘다**. 10분 타임아웃 + 보이지 않는 WINWORD 프로세스 잔류.
- ⚠️ **`.ps1`은 UTF-8 BOM으로 저장할 것.** Windows PowerShell 5.1은 BOM이 없으면 ANSI로 읽어 한글이
  깨지고 **파서 에러**가 난다.
- ⚠️ **`Rows.Item(n).Cells`는 세로 병합 표에서 예외를 던진다.** `Range.Cells`를 순회해 `RowIndex`로
  그룹핑할 것(`CellsPerRow` 참조).

## 3. HWP

자동화가 막혀 있다(실측 2026-08-08): `HWPFrame.HwpObject`는 만들어지지만
`RegisterModule("FilePathCheckDLL", …)`이 예외 없이 `False`를 반환하고(보안 모듈 미설치) `Open`도 `False`다.
다만 **HWP 창에는 파일이 열린다** — HWP가 못 읽는 게 아니라 자동화 경로만 막힌 것이다.

→ [`CHECKLIST-HWP.md`](CHECKLIST-HWP.md)로 사람이 본다. 21개 전부가 아니라 **9개**만 보면 되도록 좁혀 뒀다.

## 4. `merge-probe/` — 가로 병합 결정의 근거

`\clmgf`/`\clmrg`에서 **기하 표기**로 바꾼 판단의 실측 자료다. 한 번의 병합만 다른 방식으로 쓴 최소 문서들.

```
powershell -File tools/rtfgen/Read-Merge.ps1        # Word가 무엇을 만드는지
dotnet run --project tools/rtfgen -- --read tools/rtfgen/merge-probe/V6-geometry-2rows.rtf
```

| | Word 16이 만드는 것 |
|---|---|
| V1~V3, V5, V7 (`\clmgf`/`\clmrg`, 마커 위치·테두리·내용 위치 변형) | 병합 **안 함**. 첫 셀을 **폭 0**으로 접고 옆 칸을 빈 칸으로 남긴다 |
| V4, V6 (기하 — span 오른쪽 끝에 `\cellx` 하나) | 의도한 표 그대로 ✅ |

HWP도 기하 표기에서 정상임을 사람이 확인했다(2026-08-08). 두 소비자가 서로 다른 표기를 원하지 않는다.

## 5. 그 밖의 모드

```
dotnet run --project tools/rtfgen -- --read <file.rtf>          # 우리 reader가 만든 표 구조 출력
dotnet run --project tools/rtfgen -- --recycle <file.rtf> 4     # n회 재읽기 — 누적 결함 탐지
```

`--recycle`은 **누적**을 잡는다. 라운드6의 바닥글 `/` 증식, 이미지 밑 빈 문단 증식, 그리고 용지 크기
소실이 전부 이 형태였다 — **한 번만 돌리면 정상으로 보인다.**

## 방침

인터롭 검증은 **리포트 주도**다(2026-08-08 합의). 선제적으로 전수 파는 것은 수확이 줄어든다.
"Word/HWP에서 이상하다"는 리포트가 오면 이 도구로 재현하는 것이 이 폴더가 존재하는 이유다.
