using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace AvaloniaRichEditor.Controls;

// Accessibility peer: exposes the editor to screen readers as an editable text control whose value is
// the document's plain text. Avalonia's public automation model has no ITextProvider (caret/range/
// attribute navigation), so IValueProvider is the ceiling here — it lets assistive tech read (and,
// when not read-only, replace) the content, same as Avalonia's own TextBox.
internal sealed class RichEditorAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private readonly RichEditor _owner;

    public RichEditorAutomationPeer(RichEditor owner) : base(owner) => _owner = owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(RichEditor);

    // Falls back to a sensible default when the host hasn't set AutomationProperties.Name.
    protected override string? GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? "Rich text editor" : name;
    }

    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;

    // IValueProvider
    public bool IsReadOnly => _owner.IsReadOnly;

    public string? Value => _owner.GetPlainText();

    public void SetValue(string? value)
    {
        if (_owner.IsReadOnly) return;
        // One <p> per line so a multi-line value keeps its breaks (HtmlEncode alone collapses '\n' into
        // a single paragraph); each line is encoded so '<'/'&' stay literal. Mirrors GetPlainText, which
        // joins paragraphs with '\n', so the value round-trips.
        var sb = new System.Text.StringBuilder();
        foreach (var line in (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            sb.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(line)).Append("</p>");
        _owner.LoadHtml(sb.ToString());
    }

    // Raised by the owner when IsReadOnly toggles, so assistive tech learns the control's editability
    // changed. (Per-keystroke Value notifications are intentionally NOT raised: with only the Value
    // pattern they would make screen readers re-announce the whole document on every keystroke — the
    // correct fix would be ITextProvider, which Avalonia's public automation model doesn't expose.)
    public void NotifyReadOnlyChanged(bool oldValue, bool newValue)
        => RaisePropertyChangedEvent(ValuePatternIdentifiers.IsReadOnlyProperty, oldValue, newValue);
}
