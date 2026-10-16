using System.Windows;
using System.Windows.Input;

namespace QsysMods.Core;

// An opt-in operation window edits the owner's document. Ordinary dialogs and
// their text editors keep their own command routes.
internal sealed class ControlHistoryWindow : IDisposable
{
    private readonly Window main, window;
    private readonly ControlChordRouter keys;
    private readonly List<CommandBinding> bindings = [];
    private readonly Action<ControlHistoryWindow> closed;
    private bool disposed;

    internal ControlHistoryWindow(Window main, Window window, Func<IEnumerable<ChordEntry>> entries,
        Action<ControlHistoryWindow> closed)
    {
        this.main = main; this.window = window; this.closed = closed;
        foreach (var command in new[] { ApplicationCommands.Undo, ApplicationCommands.Redo })
        {
            var binding = new CommandBinding(command, (_, e) =>
            {
                if (!Eligible(e.OriginalSource as DependencyObject)) return;
                e.Handled = true;
                if (command.CanExecute(null, main)) command.Execute(null, main);
            }, (_, e) =>
            {
                if (!Eligible(e.OriginalSource as DependencyObject)) return;
                e.CanExecute = command.CanExecute(null, main); e.Handled = true;
            });
            window.CommandBindings.Add(binding); bindings.Add(binding);
        }
        keys = new(main, entries, (entry, _) => entry.Definition.Native!.Execute(null, main),
            (entry, target) => target == window && Eligible(Keyboard.FocusedElement as DependencyObject)
                && IsHistory(entry) && Commands.IsAvailable(entry) && entry.Definition.Native!.CanExecute(null, main), attachMain: false);
        keys.Attach(window);
        window.Closed += Closed;
    }

    internal static bool IsHistory(RegisteredCommand entry) => entry.Definition.Native is { } native
        && (ReferenceEquals(native, ApplicationCommands.Undo) || ReferenceEquals(native, ApplicationCommands.Redo));

    internal bool Eligible(DependencyObject? source) => !disposed && Host.Enabled && main.IsEnabled && window.IsEnabled
        && !ControlInput.IsTextInput(source) && !ControlInput.IsTextInput(Keyboard.FocusedElement as DependencyObject);

    private void Closed(object? sender, EventArgs e) => Dispose();
    internal void CancelPendingShortcut() => keys.Cancel();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; keys.Dispose();
        foreach (var binding in bindings) window.CommandBindings.Remove(binding);
        window.Closed -= Closed; closed(this);
    }
}
