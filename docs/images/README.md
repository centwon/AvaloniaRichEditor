# README 이미지

| 파일 | 쓰이는 곳 | 담고 있는 것 |
|---|---|---|
| `screenshot.png` | README 상단 (히어로) | 블록 이미지, 병합된 음영 헤더 표, 중첩 표, 문장 안에 흐르는 인라인 표, A4 머리글/바닥글/쪽번호 |
| `screenshot-text.png` | README "기능" 절 머리 | 인라인 서식, 정렬 4종, 중첩 글머리·번호 목록 |
| `demo.gif` | README 상단 (히어로 아래) | 입력 → 서식 → 열 드래그 → 셀 입력(행 성장) → A4 페이지 뷰 |

둘 다 데모 앱의 샘플 문서([`samples/AvaloniaRichEditor.Demo/SampleDocument.cs`](../../samples/AvaloniaRichEditor.Demo/SampleDocument.cs))
1페이지와 2페이지다. 문서가 곧 기능 투어라, 문서를 고치면 스크린샷도 같이 갱신하면 된다.

## `demo.gif`는 찍는 게 아니라 만든다

화면 녹화가 아니라 **렌더**다. [`tools/readme-shots`](../../tools/readme-shots)가 헤드리스 창에 실제
입력을 넣고 Skia가 그린 프레임을 모아 Pillow로 묶는다. 다시 만들려면:

```
dotnet run --project tools/readme-shots/readme-shots.csproj -c Release -- C:/tmp/shots
python tools/readme-shots/make-gif.py C:/tmp/shots docs/images/demo.gif 700
```

커서와 제목표시줄이 없는 것이 정상이다(녹화가 아니라서). 크기는 1.3 MB 선을 넘기지 않는 게 좋다 —
GitHub가 README를 열 때마다 내려보낸다.

## 정지 스크린샷을 다시 찍는 법

```
dotnet run --project samples/AvaloniaRichEditor.Demo/AvaloniaRichEditor.Demo.csproj
```

창을 1000x1600 정도로 두고 각 페이지가 온전히 보이게 스크롤한 뒤 **창만** 캡처한다
(바탕화면·작업 표시줄이 들어가지 않게). 폭 1000~1800 px, 500 KB 이하.

## 이미지 문법 — 바꾸지 말 것 (둘 다 실제로 당한 것)

**`README.md`(패키지 README)에서는 raw HTML을 쓰지 말 것.** `<p align="center"><img …></p>`는 GitHub에서는
가운데 정렬되고 폭도 지정되지만, **nuget.org는 HTML을 렌더하지 않고 태그를 글자 그대로 찍는다** — 1.2.0
패키지 페이지에 `<p align="center"> <img src="…"` 가 본문으로 노출됐다. 그림 두 장이 다 깨진 채로.
그래서 `![alt](url)` 순수 Markdown만 쓴다. 가운데 정렬과 폭 지정을 잃지만, 한쪽에서 깨지는 것보다 낫다.

`README.ko.md`는 패키지에 안 들어가고 GitHub만 보므로 HTML을 써도 된다.

⚠️ **패키지 페이지는 새 버전을 내야 고쳐진다.** README는 `.nupkg` 안에 들어가므로, 이미 올라간 버전의
페이지는 손댈 수 없다.

## 링크 형식 — 바꾸지 말 것

`README.md`의 이미지 URL은 **절대 경로(raw.githubusercontent.com)** 다. 이 파일은 패키지 README
(`PackageReadmeFile`)이고 nuget.org는 리포 상대 경로를 해석하지 않으므로, 상대 경로로 바꾸면
**패키지 페이지에서만 조용히 깨진다.** `README.ko.md`는 패키지에 들어가지 않으므로 상대 경로가 맞다.

raw URL은 `main`에 올라간 커밋을 가리키므로, 이미지를 바꾸면 push 후에 반영된다.

## 알려진 사항

- 툴바가 **한국어**로 나온다. 데모가 기본 로케일을 따르기 때문이고, 현지화가 있다는 증거이기도 하다.
  영어 UI 스크린샷을 원하면 데모에서 `RichEditorLocalization.Language`를 바꾼 뒤 다시 찍으면 된다.
- 그림에 리사이즈 핸들(파란 사각형)이 보이지 않는 것이 정상이다. 2026-08-08부터 핸들은 **그림을
  선택했을 때만** 그려진다. 선택 안 된 그림에는 옅은 외곽선만 남는다.
