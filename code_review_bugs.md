# AvaloniaRichTextBoxPort - Code Review & Bug Report (with Solutions)

이 문서는 소스 코드를 직접 수정하지 않고, 정적 분석(Static Analysis)을 통해 발견된 잠재적 버그와 이에 대한 명확한 해결책을 정리한 마크다운 문서입니다.

---

## 1. 텍스트 줄바꿈 시 Hit-Testing (마우스 클릭) 오작동
*   **원인:** `GetLocalIndexClosestToPoint` 메서드에서 마우스 좌표(p.X)를 글자 인덱스로 변환할 때, `(p.X / 너비) * 글자수` 방식의 단순 비례식을 사용 중입니다. 텍스트가 여러 줄로 줄바꿈(Word Wrapping)되었을 경우 두 번째 줄 이하를 클릭하면 전혀 엉뚱한 위치가 선택됩니다.
*   **해결책:** Avalonia에서 제공하는 **`FormattedText.HitTestPoint(Point)`** API를 사용해야 합니다. 이 함수는 다국어 래핑이나 다중 줄바꿈 상태에서도 정확히 마우스가 클릭한 글자의 인덱스(TextPosition)와 IsTrailing(글자 앞/뒤 여부) 정보를 완벽하게 계산해 줍니다.

## 2. 드래그 삭제 (`TextRange.Delete`) 후 단락 쓰레기값 방치 및 서식 깨짐
*   **원인:** 블록 지정 후 지우기를 눌렀을 때, 범위 내의 글자(`Run`)만 지우고 텅 빈 `Paragraph` 객체들을 트리에 방치합니다. 여러 문단을 걸쳐 지웠을 경우 윗 문단과 아랫 문단이 하나로 합쳐져야(Merge) 하는데 이 로직이 누락되어 레이아웃이 찢어지거나 렌더링 에러가 납니다.
*   **해결책:** 
    1. 범위 내의 글자들을 지운 후, `Start` 단락과 `End` 단락이 다르면 `End` 단락에 남은 글자(`Inlines`)를 모두 `Start` 단락 뒤로 옮겨서 붙입니다 (Paragraph Merge).
    2. 그 사이 공간에 속해 있던 텅 빈 `Paragraph` 객체들은 `Document.Blocks` (또는 표 셀) 리스트에서 `Remove()`하여 메모리에서 완전히 제거하는 정리(Cleanup) 과정을 추가해야 합니다.

## 3. NullReference 예외 (NRE) 크래시 위험 (CS8602~4)
*   **원인:** 문서의 첫 줄이나 끝 줄에서 위/아래 방향키를 누르거나, 초기 상태에서 `_caretPosition.Paragraph`가 `null`일 때 안전 장치 없이 속성에 접근하여 앱이 튕깁니다.
*   **해결책:** `GetPreviousParagraph()`, `GetNextParagraph()` 로직의 반환값과 `_caretPosition` 사용 직전에 **엄격한 `if (x == null) return;` 안전 검사(Null Check)**를 도입해야 합니다.

## 4. `Clone()` 수행 시 부모 노드(`Parent`) 미아 발생
*   **원인:** `Undo/Redo` 시 전체 문서를 깊은 복사(`Clone()`)하는데, 자식 노드들을 복제한 뒤 자식의 `Parent` 속성에 새로 생성된 자신(this)을 할당해 주지 않아 탐색 트리가 끊어집니다.
*   **해결책:** 모든 `TextElement` 파생 클래스의 `Clone()` 구현부에서 하위 요소 목록(`Inlines`, `Cells`, `Blocks`)을 추가할 때 **반드시 `child.Parent = this;` 연산을 수행하도록 강제**해야 합니다.

## 5. 다중 줄(Multi-line) 블록 선택(Selection) 하이라이트 렌더링 버그
*   **원인:** 드래그하여 텍스트를 파란색으로 반전(Selection)시킬 때, 텍스트가 줄바꿈된 상태라면 단순히 시작/끝 좌표로 네모를 그리면 엉뚱한 여백까지 파란색으로 칠해집니다.
*   **해결책:** Avalonia의 **`FormattedText.HitTestTextRange(startIndex, length)`**를 호출하면 텍스트가 줄바꿈된 모양 그대로 정교한 시각적 사각형(Rects) 배열을 반환해 줍니다. 이 Rect들을 순회하며 투명한 파란색 붓(`FillRectangle`)으로 칠하도록 `RenderSelection` 로직을 전면 수정해야 합니다.

## 6. HTML 붙여넣기 시 파서 크래시 (Fallback 누락)
*   **원인:** 클립보드에 손상되거나 복잡한 HTML(웹사이트의 괴상한 태그들)이 들어왔을 때, 파서가 파싱에 실패하면 앱 전체가 멈출 수 있습니다.
*   **해결책:** `PasteFromClipboardAsync` 내부의 HTML 처리 로직을 `try-catch`로 감싸고, 파싱 중 알 수 없는 예외가 발생하면 즉시 **일반 텍스트(`GetTextAsync`) 모드로 폴백(Fallback)**하여 단순 텍스트로라도 붙여넣기가 성공하도록 안전망을 설계해야 합니다.

## 7. 표 렌더링(Table Render) 높이 엇갈림
*   **원인:** 같은 줄(Row)에서 셀별로 글자 크기나 줄 수가 다를 때 `rowMaxHeight`는 잘 계산되지만, 정작 글자를 그릴 때는 위쪽 여백(`yOffset + 5`)으로 고정하여 렌더링하기 때문에 수직 정렬이 틀어져 보입니다.
*   **해결책:** 셀 내부에 글자를 그릴 때 `(rowMaxHeight - FormattedText.Height) / 2` 처럼 수직 중앙 정렬 수식을 반영하거나, 최소한 패딩 값(Padding)을 정밀하게 연산하여 `context.DrawText` 좌표에 반영해 주어야 합니다.
