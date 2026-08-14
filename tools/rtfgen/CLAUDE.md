# tools/rtfgen

**솔루션(`AvaloniaRichEditor.slnx`)에 일부러 넣지 않았다.** 빌드·CI 대상이 아니라 손으로 돌리는 개발
도구다. 여기를 건드려도 CI는 영향받지 않는다.

## 왜 있나

이 프로젝트의 **reader는 이 프로젝트의 writer에 관대하다.** 그래서 자체 왕복 테스트는 자기 자신과
합의할 뿐이고, 나가는 바이트가 Word·HWP·브라우저에게 무엇을 말하는지는 **구조적으로 못 본다.**
라운드9 결함 5건이 전부 이 사각지대에서 나왔다.

## 쓰는 법

```
dotnet run --project tools/rtfgen                          # 문서 8종 생성
dotnet run --project tools/rtfgen -- --read <file.rtf>     # 우리 reader가 만든 표 구조 출력
dotnet run --project tools/rtfgen -- --recycle <f.rtf> 4   # n회 재읽기 — 누적 결함 탐지
powershell -File tools/rtfgen/Verify-Word.ps1              # Word COM으로 28항목 자동 측정
```

`--recycle`은 **누적**을 잡는다. 바닥글 `/` 증식, 이미지 밑 빈 문단 증식, 용지 크기 소실이 전부 그
형태였고 셋 다 **한 번만 돌리면 정상으로 보인다.**

## 함정 (전부 실측으로 배운 것)

- ⚠️ **Word는 읽기 전용으로만 쓸 것.** `Documents.Add` + `SaveAs2`(Word에게 참조 파일을 쓰게 하기)는
  헤드리스에서 **멈춘다** — 10분 타임아웃 + 보이지 않는 WINWORD 프로세스 잔류.
- ⚠️ **`.ps1`은 UTF-8 BOM으로 저장할 것.** Windows PowerShell 5.1은 BOM이 없으면 ANSI로 읽어 한글이
  깨지고 파서 에러가 난다.
- ⚠️ **`Rows.Item(n).Cells`는 세로 병합 표에서 예외를 던진다.** `Range.Cells`를 순회해 `RowIndex`로
  그룹핑할 것(`CellsPerRow` 참조).
- ⚠️ **`new TableBlock(r, c) { ColumnWidths = { … } }`는 덮어쓰지 않고 뒤에 붙는다.** 생성자가 이미
  컬럼당 100을 넣어둬서 선언한 값이 읽히지 않는다. `Widths(...)` 헬퍼를 쓸 것.

## 방침

인터롭 검증은 **리포트 주도**다(2026-08-08 합의). 선제 전수 파기는 수확이 줄어든다.
"Word/HWP에서 이상하다"는 리포트가 오면 이 도구로 재현하는 것이 이 폴더가 존재하는 이유다.

자세한 배경과 `merge-probe/`(가로 병합 표기 결정의 실측 근거)는 [`README.md`](README.md) 참고.
