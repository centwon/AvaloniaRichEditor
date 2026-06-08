using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace AvaloniaRichEditor.Controls;

// Accessibility peer: exposes the editor to screen readers as an editable text control whose value is
// the document's plain text. A full ITextProvider (ranges/attributes) is future work; IValueProvider
// already lets assistive tech read (and, when not read-only, replace) the content.
internal sealed class RichEditorAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private readonly RichEditor _owner;

    public RichEditorAutomationPeer(RichEditor owner) : base(owner) => _owner = owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(RichEditor);

    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;

    // IValueProvider
    public bool IsReadOnly => _owner.IsReadOnly;

    public string? Value => _owner.GetPlainText();

    public void SetValue(string? value)
    {
        if (_owner.IsReadOnly) return;
        _owner.LoadHtml(System.Net.WebUtility.HtmlEncode(value ?? string.Empty));
    }
}
