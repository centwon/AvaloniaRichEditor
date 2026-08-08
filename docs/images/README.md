# README 이미지

| 파일 | 쓰이는 곳 | 담고 있는 것 |
|---|---|---|
| `screenshot.png` | README 상단 (히어로) | 블록 이미지, 병합된 음영 헤더 표, 중첩 표, 문장 안에 흐르는 인라인 표, A4 머리글/바닥글/쪽번호 |
| `screenshot-text.png` | README "기능" 절 머리 | 인라인 서식, 정렬 4종, 중첩 글머리·번호 목록 |

둘 다 데모 앱의 샘플 문서([`samples/AvaloniaRichEditor.Demo/SampleDocument.cs`](../../samples/AvaloniaRichEditor.Demo/SampleDocument.cs))
1페이지와 2페이지다. 문서가 곧 기능 투어라, 문서를 고치면 스크린샷도 같이 갱신하면 된다.

## 다시 찍는 법

```
dotnet run --project samples/AvaloniaRichEditor.Demo/AvaloniaRichEditor.Demo.csproj
```

창을 1000x1600 정도로 두고 각 페이지가 온전히 보이게 스크롤한 뒤 **창만** 캡처한다
(바탕화면·작업 표시줄이 들어가지 않게). 폭 1000~1800 px, 500 KB 이하.

## 링크 형식 — 바꾸지 말 것

`README.md`의 이미지 URL은 **절대 경로(raw.githubusercontent.com)** 다. 이 파일은 패키지 README
(`PackageReadmeFile`)이고 nuget.org는 리포 상대 경로를 해석하지 않으므로, 상대 경로로 바꾸면
**패키지 페이지에서만 조용히 깨진다.** `README.ko.md`는 패키지에 들어가지 않으므로 상대 경로가 맞다.

raw URL은 `main`에 올라간 커밋을 가리키므로, 이미지를 바꾸면 push 후에 반영된다.

## 알려진 사항

- 툴바가 **한국어**로 나온다. 데모가 기본 로케일을 따르기 때문이고, 현지화가 있다는 증거이기도 하다.
  영어 UI 스크린샷을 원하면 데모에서 `RichEditorLocalization.Language`를 바꾼 뒤 다시 찍으면 된다.
- 그림 오른쪽 아래의 작은 파란 사각형은 **리사이즈 핸들**이다. 선택 여부와 무관하게 항상 그려지는 것이
  현재 동작이고(화면 전용 — 인쇄·PDF에는 없다), 스크린샷에도 그대로 들어간다.
