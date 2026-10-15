using System.Windows;
using System.Windows.Controls.Primitives;

namespace QsysMods.Core;

public sealed partial class ControlBindings
{
    private readonly Dictionary<FrameworkElement, List<(ButtonBase Button, string Original)>> popupHints = new();
    private bool popupHintsDisposed;

    private static void InitializePopupHints(ControlBindings owner)
    {
        // Class handlers cannot be unregistered. Retain only a weak owner and
        // ignore disposed engines; open popup state is released on Unloaded.
        var weak = new WeakReference<ControlBindings>(owner);
        EventManager.RegisterClassHandler(Host.Type("QSC.QSys.windows.PopupToolBar"), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => {
                if (weak.TryGetTarget(out var engine) && !engine.popupHintsDisposed && sender is FrameworkElement popup)
                    engine.RefreshPopupToolbar(popup);
            }), true);
    }
    internal void RefreshPopupToolbar(FrameworkElement popup)
    {
        if (!popupHints.TryGetValue(popup, out var originals))
        {
            // The native icon toolbar uses string tooltips. Preserve rich or
            // provider-owned tooltip objects and buttons without a command.
            originals = Elements(popup).OfType<ButtonBase>()
                .Where(b => b.ToolTip is string && b.Command is not null)
                .Select(b => (b, (string)b.ToolTip)).ToList();
            popupHints.Add(popup, originals);
            popup.Unloaded += PopupToolbarUnloaded;
        }
        foreach (var (button, original) in originals)
        {
            var entries = Commands.All.Where(c => Commands.IsAvailable(c)
                && (ReferenceEquals(c.Command, button.Command) || ReferenceEquals(c.Definition.Native, button.Command)));
            string keys = string.Join(" or ", entries.SelectMany(c => Effective(c, current.Overrides)).Distinct().Select(g => g.Label));
            button.ToolTip = keys.Length == 0 ? original : original + " (" + keys + ")";
        }
    }
    private void PopupToolbarUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement popup) RestorePopupToolbar(popup);
    }
    private void RestorePopupToolbar(FrameworkElement popup)
    {
        if (!popupHints.Remove(popup, out var originals)) return;
        foreach (var (button, original) in originals) button.ToolTip = original;
        popup.Unloaded -= PopupToolbarUnloaded;
    }
}
