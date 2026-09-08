# readme-shots

README에 쓰는 애니메이션(`docs/images/demo.gif`)을 **렌더해서** 만든다. 화면 녹화가 아니다.

`tools/rtfgen`과 같은 성격이라 **솔루션·CI 밖**에 있다. 손으로 돌리는 개발 도구다.

## 왜 렌더인가

에디터를 헤드리스 창에 띄우되 **실제 Skia**로 그린다(픽셀 렌더 테스트와 같은 설정,
`UseHeadlessDrawing = false`). 그리고 각 장면을 **진짜 입력**으로 실행한다 — 열 경계에 `MouseDown` →
`MouseMove` → `MouseUp`, 글자는 입력기 경로(`KeyTextInput`)로. 그래서 한 프레임은 *사용자가 만들어낼 수
있는 상태를 컨트롤이 스스로 그린 그림*이다. 보기 좋으라고 모델을 직접 찔러 만든 장면은 하나도 없다.

대신 **화면 녹화에 있는 두 가지가 없다**: 마우스 커서와 창 제목표시줄. 일어나지 않은 일을 그려 넣는
셈이라 일부러 넣지 않았다.

## 쓰는 법

```
dotnet run --project tools/readme-shots/readme-shots.csproj -c Release -- C:/tmp/shots
python tools/readme-shots/make-gif.py C:/tmp/shots docs/images/demo.gif 700
```

1단계가 PNG 프레임을 한 틱에 하나씩 쓰고, 2단계가 Pillow로 묶는다(ffmpeg 없이 돈다). 현재 117프레임 ·
11.7초 · 1.3 MB. **크기는 기능이다** — GitHub는 README를 열 때마다 이 파일을 내려보낸다. 커지면
`make-gif.py`의 폭(700)이나 색 수(128)를 먼저 줄인다.

## 장면 순서 (`Program.cs`)

1. 제목 아래 빈 문단에 문장 입력
2. 뒷부분을 선택해 굵게 + 파랑
3. 표 열 경계를 끌어 넓히기 ← 정지 이미지가 못 하는 부분
4. 빈 셀을 클릭해 입력, 행이 늘어남
5. A4 페이지 뷰로 전환

## 함정

- **좌표를 손으로 적지 말 것.** 열 핸들 위치는 렌더러가 기록한 `_columnBoundaries`에서 읽는다
  (`OnPointerPressed`가 히트테스트하는 바로 그 목록). 처음엔 키보드 탐색(Ctrl+Home, ↓)으로 캐럿을
  옮겼는데 **제목 안에 들어가서** 타이핑이 제목을 망가뜨렸다. 기하에서 뽑아 클릭하는 쪽이 맞다.
- **로케일과 글꼴을 고정한다.** 데모는 OS 로케일을 따르므로 툴바가 한국어로 나온다. 영어 README용이라
  `RichEditorLocalization.Language = "en"`, 그리고 툴바의 글꼴 이름이 본문과 어긋나지 않게
  `DefaultFontFamily = "Inter"`(번들 글꼴)로 둔다.
- 정지 스크린샷(`screenshot.png`, `screenshot-text.png`)은 **이 도구가 만든 것이 아니다.** 사람이 데모
  창을 캡처한 것이고, 갱신 방법은 [`docs/images/README.md`](../../docs/images/README.md)에 있다.
