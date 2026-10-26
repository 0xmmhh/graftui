#pragma warning disable CS0618 // TextView is marked obsolete in Terminal.Gui 2.5.0 (its successor is tui-cs/Editor, not used here). It is still the multi-line editor the commit body needs.
using Terminal.Gui.Views;

namespace graftui.Ui.Views;

/// <summary>
/// The commit message body: a multi-line editor. Kept as a thin subclass so the obsolete base type is referenced in one file only.
/// Tab is left to the app (the message box moves focus with it), so TabKeyAddsTab is set off by the owner.
/// </summary>
public sealed class BodyTextView : TextView
{
}
