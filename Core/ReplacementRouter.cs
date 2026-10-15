using System.Windows;
using System.Windows.Input;

namespace QsysMods.Core;

public enum ReplacementAvailability { OutsideScope, Blocked, Ready }
public sealed record ReplacementContext(ReplacementAvailability Availability, object? Target = null);
public sealed record CommandReplacement(ControlProposal Proposal, RoutedCommand Native,
    Func<ReplacementContext> ReadContext, Action<object?> Execute);

/// <summary>One preview router for all approved replacements, including native menu entry points.</summary>
public sealed class ReplacementRouter : IDisposable
{
    private readonly UIElement root;
    private readonly Func<IEnumerable<CommandReplacement>> active;
    private readonly Action<Exception> error;
    public ReplacementRouter(UIElement root, Func<IEnumerable<CommandReplacement>> active, Action<Exception> error)
    {
        this.root = root; this.active = active; this.error = error;
        CommandManager.AddPreviewCanExecuteHandler(root, CanExecute);
        CommandManager.AddPreviewExecutedHandler(root, Execute);
    }
    private (CommandReplacement Replacement, ReplacementContext Context)? Resolve(ICommand command)
    {
        (CommandReplacement, ReplacementContext)? result = null;
        foreach (var item in active().Where(r => ReferenceEquals(r.Native, command)))
        {
            var context = item.ReadContext();
            if (context.Availability == ReplacementAvailability.OutsideScope) continue;
            if (result is not null) throw new InvalidOperationException("Multiple replacement providers matched this context. Review mod controls.");
            result = (item, context);
        }
        return result;
    }
    private void CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        try
        {
            if (Resolve(e.Command) is not { } selected) return;
            e.Handled = true;
            e.CanExecute = selected.Context.Availability == ReplacementAvailability.Ready;
            e.ContinueRouting = !e.CanExecute;
        }
        catch { e.Handled = true; e.CanExecute = false; e.ContinueRouting = true; }
    }
    private void Execute(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            if (Resolve(e.Command) is not { } selected) return;
            e.Handled = true; // Never fall through after a rejected or partially executed mutation.
            if (selected.Context.Availability == ReplacementAvailability.Ready)
                selected.Replacement.Execute(selected.Context.Target);
        }
        catch (Exception ex) { e.Handled = true; error(ex); }
    }
    public void Dispose()
    {
        CommandManager.RemovePreviewCanExecuteHandler(root, CanExecute);
        CommandManager.RemovePreviewExecutedHandler(root, Execute);
    }
}
