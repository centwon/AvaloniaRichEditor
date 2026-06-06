# Avalonia RichTextBox Port Project

## 🎯 프로젝트 목표 (Project Goal)
이 프로젝트의 최종 목표는 WPF의 방대한 `RichTextBox` 및 `FlowDocument` 프레임워크를 Avalonia UI로 이식하는 것입니다. 
기존 Avalonia 생태계에는 완벽한 네이티브 RichTextBox가 없으며, WPF의 내부 렌더링 엔진(PTS, 비관리형 C++)을 그대로 가져올 수 없으므로 **"순수 C#과 Avalonia의 TextLayout 엔진만을 사용하여 바닥부터(From-Scratch) 독자적인 렌더링 및 레이아웃 엔진을 구축하는 것"**이 핵심입니다.

---

## 🗺️ 구현 로드맵 및 단계별 계획 (Implementation Plan)

### [완료] Phase 1: 기반 모델 및 렌더링 엔진 구축
- **작업 내용**: WPF의 문서 객체 모델(Document Object Model)을 모방하고, 이를 화면에 그려내는 뷰어(Viewer) 역할 구현.
- **구현 항목**:
  - `TextElement`, `Block`, `Paragraph`, `Inline`, `Run`, `FlowDocument` 구조 설계.
  - `CustomRichTextBox` 컨트롤 생성 및 `Render(DrawingContext)` 메서드 오버라이드.
  - Avalonia `FormattedText`를 이용해 글꼴 굵기(Bold), 색상(Foreground) 등 다중 서식 렌더링 성공.

### [진행 예정] Phase 2: 에디터 상호작용 (커서 및 키보드 입력)
- **작업 내용**: 단순히 문서를 보여주는 것을 넘어, 커서(Caret)를 표시하고 글자를 입력/삭제할 수 있는 에디터로 발전.
- **구현 항목**:
  - 화면 상의 좌표(X, Y)를 문서 내의 글자 인덱스로 변환하는 히트 테스트(Hit-Testing) 구현.
  - 깜빡이는 커서(Caret) 렌더링.
  - `KeyDown` 및 `TextInput` 이벤트를 캡처하여 `FlowDocument` 데이터 모델 업데이트 로직 작성.

### [진행 예정] Phase 3: 텍스트 선택 및 서식 변경 (Text Selection & Formatting)
- **작업 내용**: 마우스 드래그를 통한 텍스트 블록 선택 기능 및 선택 영역에 서식 부여 기능.
- **구현 항목**:
  - `TextPointer`, `TextRange`, `TextSelection` 클래스 포팅 및 구현.
  - 선택 영역(Selection)의 배경색 하이라이팅 렌더링.
  - `Ctrl+B`, `Ctrl+I` 등의 단축키 입력 시 선택된 `TextRange`의 서식 데이터 변경 알고리즘 작성.

### [진행 예정] Phase 4: 포맷 파서 및 클립보드 (Format Parsers & Clipboard)
- **작업 내용**: 문서를 파일로 입출력하고 운영체제 클립보드와 연동.
- **구현 항목**:
  - RTF, HTML, XAML 포맷 파서 구현.
  - OS 클립보드 복사/붙여넣기 시 서식 유지 로직.

### [진행 예정] Phase 5: 고급 레이아웃 및 최적화
- **작업 내용**: 다양한 인라인 요소 지원 및 대용량 문서 성능 최적화.
- **구현 항목**:
  - 이미지 삽입(`InlineUIContainer`), 표(`Table`), 목록(`List`) 지원.
  - 화면에 보이는 영역만 렌더링하는 UI 가상화(Virtualization) 로직 추가.

---
**마지막 업데이트**: 2026년 6월 (Phase 1 완료 및 VS 소스 저장소 이관)
