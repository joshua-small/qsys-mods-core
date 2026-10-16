using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace QsysMods.Core;

internal sealed record ChordEntry(RegisteredCommand Command, ControlSequence Sequence);

public static class ControlInput
{
    public static bool IsTextInput() => IsTextInput(Keyboard.FocusedElement as DependencyObject);
    internal static bool IsTextInput(DependencyObject? focused)
    {
        for (var current = focused; current is not null; current = Parent(current))
            if (current is TextBoxBase or PasswordBox || current is ComboBox { IsEditable: true }
                || current.GetType().FullName?.Contains("WebView", StringComparison.OrdinalIgnoreCase) == true
                || IsLegacyEditor(current.GetType())) return true;
        return false;
    }
    // Legacy Designer scripts focus EditorView under SyntaxEditor, rather than
    // TextBoxBase. Match the editor's base type without a vendor assembly dependency.
    private static bool IsLegacyEditor(Type? type)
    {
        for (; type is not null; type = type.BaseType)
            if (type.FullName == "ActiproSoftware.Windows.Controls.SyntaxEditor.SyntaxEditor") return true;
        return false;
    }
    public static DependencyObject? Parent(DependencyObject current) => current is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(current) : current is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(current);
    internal static bool Available(RegisteredCommand command, Window main, Window target)
    {
        try
        {
            if (!Host.Enabled || !main.IsEnabled || !target.IsEnabled || !Commands.IsAvailable(command)) return false;
            if (target != main && (command.Definition.Native is not null || !command.Definition.AllowInText)) return false;
            var context = Commands.DefaultFor(command.Definition.Id)?.Context ?? command.Definition.ShortcutContext;
            if (context is not null && !context()) return false;
            if (!command.Definition.AllowInText && IsTextInput()) return false;
            return command.Definition.CanExecute();
        }
        catch { return false; }
    }
    public static Key ActualKey(KeyEventArgs e) => e.Key switch
    { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };
}

/// <summary>Pure chord state; the window adapter handles time/focus and displays pending choices.</summary>
public sealed class ControlChordState
{
    public string? Prefix { get; private set; }
    private (string Id, ControlSequence Sequence)[] pending = [];
    public bool Waiting => pending.Length > 0;
    public void Cancel() { pending = []; Prefix = null; }
    public string? Feed(ControlStroke stroke, IEnumerable<(string Id, ControlSequence Sequence)> candidates, out bool consumed)
    {
        if (stroke.IsModifier) { consumed = false; return null; }
        if (Waiting)
        {
            var match = pending.FirstOrDefault(p => p.Sequence.Second == stroke);
            Cancel(); consumed = true;
            return stroke is { Key: Key.Escape, Modifiers: ModifierKeys.None } ? null : match.Id;
        }
        var matches = candidates.Where(c => c.Sequence.First == stroke).ToArray();
        consumed = matches.Length > 0;
        if (!consumed) return null;
        if (matches.Any(m => !m.Sequence.IsChord)) return matches.Single(m => !m.Sequence.IsChord).Id;
        pending = matches; Prefix = stroke.Label; return null;
    }
}

internal sealed class ControlChordRouter : IDisposable
{
    private readonly Window main;
    private readonly Func<IEnumerable<ChordEntry>> entries;
    private readonly Action<RegisteredCommand, Window> execute;
    private readonly Func<RegisteredCommand, Window, bool> available;
    private readonly ControlChordState state = new();
    private readonly HashSet<Window> windows = new();
    private readonly HashSet<Key> held = new();
    private readonly DispatcherTimer timeout;
    private readonly Popup notice = new() { AllowsTransparency = true, StaysOpen = true, Focusable = false, IsHitTestVisible = false, Placement = PlacementMode.Bottom };
    private Window? pendingWindow;
    private IInputElement? pendingFocus;
    private ChordEntry[] pendingEntries = [];
    internal ControlChordRouter(Window main, Func<IEnumerable<ChordEntry>> entries, Action<RegisteredCommand, Window> execute,
        Func<RegisteredCommand, Window, bool>? available = null, bool attachMain = true)
    {
        this.main = main; this.entries = entries; this.execute = execute;
        this.available = available ?? ((command, target) => ControlInput.Available(command, main, target));
        timeout = new DispatcherTimer(DispatcherPriority.Input, main.Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) => Cancel(); if (attachMain) Attach(main);
    }
    internal void Attach(Window window)
    {
        if (!windows.Add(window)) return;
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown), true);
        window.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(KeyUp), true);
        window.PreviewMouseDown += MouseDown;
        window.PreviewGotKeyboardFocus += FocusChanged;
        window.Deactivated += Deactivated; window.Closed += Closed;
    }
    private void KeyDown(object sender, KeyEventArgs e)
    {
        var target = (Window)sender;
        var key = ControlInput.ActualKey(e);
        var stroke = new ControlStroke(key, e.KeyboardDevice.Modifiers);
        if (state.Waiting && (pendingWindow != target || pendingFocus != Keyboard.FocusedElement)) Cancel();
        if (stroke.IsModifier) return;
        if (e.IsRepeat || held.Contains(key)) { if (held.Contains(key) || state.Waiting) e.Handled = true; return; }
        var available = state.Waiting ? pendingEntries : entries().Where(c => c.Sequence.First == stroke && this.available(c.Command, target)).ToArray();
        var id = state.Feed(stroke, available.Select(c => (c.Command.Definition.Id, c.Sequence)), out var consumed);
        if (!consumed) return;
        e.Handled = true; held.Add(key);
        if (state.Waiting)
        {
            pendingWindow = target; pendingFocus = Keyboard.FocusedElement;
            pendingEntries = available.Where(c => c.Sequence.First == stroke).ToArray();
            Show(target); timeout.Start();
        }
        else
        {
            var command = id is null ? null : available.FirstOrDefault(c => c.Command.Definition.Id == id)?.Command;
            Cancel();
            if (command is not null && this.available(command, target)) execute(command, target);
        }
    }
    private void KeyUp(object sender, KeyEventArgs e)
    {
        var key = ControlInput.ActualKey(e);
        // Escape down may be intercepted before WPF sees it. If its release
        // reaches this route, do not leave a chord armed.
        if (key == Key.Escape && state.Waiting) { Cancel(); e.Handled = true; }
        if (held.Remove(key) || (state.Waiting && key is Key.LeftAlt or Key.RightAlt)) e.Handled = true;
    }
    private void Show(Window target)
    {
        var lines = pendingEntries.Select(c => c.Sequence.Second!.Value.Label + " — " + c.Command.Definition.Name).Distinct();
        notice.Child = new Border { Background = new SolidColorBrush(Color.FromRgb(35, 35, 38)), BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12),
            Child = new TextBlock { Foreground = Brushes.White, MaxWidth = 620, TextWrapping = TextWrapping.Wrap,
                Text = state.Prefix + ": waiting for second key\n" + string.Join("\n", lines) + "\nEscape cancels • 10 seconds" } };
        notice.PlacementTarget = target; notice.HorizontalOffset = 24;
        notice.VerticalOffset = -Math.Min(target.ActualHeight - 20, 65 + pendingEntries.Length * 18); notice.IsOpen = true;
    }
    public void Cancel() { state.Cancel(); timeout.Stop(); notice.IsOpen = false; pendingWindow = null; pendingFocus = null; pendingEntries = []; }
    private void MouseDown(object sender, MouseButtonEventArgs e) => Cancel();
    private void FocusChanged(object sender, KeyboardFocusChangedEventArgs e) { if (state.Waiting && e.NewFocus != pendingFocus) Cancel(); }
    private void Deactivated(object? sender, EventArgs e) { Cancel(); held.Clear(); }
    private void Closed(object? sender, EventArgs e) { if (sender is Window window) Detach(window); Cancel(); }
    private void Detach(Window window)
    {
        window.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown));
        window.RemoveHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(KeyUp));
        window.PreviewMouseDown -= MouseDown; window.PreviewGotKeyboardFocus -= FocusChanged;
        window.Deactivated -= Deactivated; window.Closed -= Closed; windows.Remove(window);
    }
    public void Dispose() { Cancel(); foreach (var window in windows.ToArray()) Detach(window); }
}
