using System.Windows;
using System.Windows.Controls;
using QsysMods.Core;

internal static partial class Program
{
    static void EditorTests()
    {
        // Designer's Legacy editor focuses an EditorView beneath SyntaxEditor,
        // not a TextBox. Exercise that visual ancestry without vendor binaries.
        var view = new Canvas();
        var editor = new DerivedSyntaxEditor { Child = new Border { Child = view } };
        Check(ControlInput.IsTextInput(view), "Legacy editor descendants exclude ordinary shortcuts");
        Check(ControlInput.IsTextInput(editor), "Derived Legacy editor hosts retain text-input protection");
        ((Border)editor.Child).Child = null;
        var canvas = new Border { Child = view };
        Check(!ControlInput.IsTextInput(view), "Leaving the editor returns to ordinary canvas eligibility");
        Check(!ControlInput.IsTextInput(new ActiproSoftware.Windows.Controls.SyntaxEditor.UnrelatedPanel()), "Other Actipro panels remain eligible");
        Check(ControlInput.IsTextInput(new TextBox()), "Native text input remains protected");
        Check(ControlInput.IsTextInput(new ComboBox { IsEditable = true }), "Editable selectors remain protected");
        Check(!ControlInput.IsTextInput(new ComboBox()) && !ControlInput.IsTextInput(null), "Non-editable selectors and absent focus remain ordinary input");
    }
    private sealed class DerivedSyntaxEditor : ActiproSoftware.Windows.Controls.SyntaxEditor.SyntaxEditor { }
}

namespace ActiproSoftware.Windows.Controls.SyntaxEditor
{
    public class SyntaxEditor : Border { }
    public class UnrelatedPanel : Border { }
}
