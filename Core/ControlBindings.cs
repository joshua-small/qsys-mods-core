using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using QsysMods.Core;

namespace QsysMods.Core;

public sealed class ControlBindings : IDisposable
{
    private readonly Window window;
    private readonly Dictionary<RoutedCommand, InputGesture[]> originals = new();
    private readonly List<(UIElement Owner, InputBinding Binding)> removed = new(), added = new();
    private readonly List<WeakReference<UIElement>> scopes = new();
    private readonly List<(MenuItem Item, string Original)> hints = new();
    private BindingSettings current = new(new());
    public ControlBindings(Window window)
    {
        this.window = window;
        // The native floating Component Controls window has a separate input route.
        EventManager.RegisterClassHandler(Host.Type("QSC.QSys.windows.ComponentInfo"), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is not UIElement element || scopes.Any(s => s.TryGetTarget(out var target) && ReferenceEquals(target, element))) return;
            scopes.Add(new(element)); BindFloating(element);
        }), true);
    }
    public static KeyGesture? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var gesture = new KeyGestureConverter().ConvertFromInvariantString(text.Trim()) as KeyGesture ?? throw new ArgumentException("Enter a valid shortcut.");
        if (gesture.Key == Key.None || ((gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 && gesture.Key is not (>= Key.F1 and <= Key.F24)))
            throw new ArgumentException("Use Ctrl, Alt, or a function key so ordinary typing remains available.");
        return gesture;
    }
    public static string Format(KeyGesture? gesture) => ControlGestures.Serialize(gesture);
    private IEnumerable<KeyGesture> Effective(RegisteredCommand command, Dictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(command.Definition.Id, out var text)) return ControlGestures.ParseList(text);
        if (command.Definition.Native is { } native) return originals.TryGetValue(native, out var saved) ? saved.OfType<KeyGesture>() : native.InputGestures.OfType<KeyGesture>();
        return command.Definition.DefaultGesture is { } value ? new[] { value } : Array.Empty<KeyGesture>();
    }
    public string Display(string id, Dictionary<string, string> overrides) => ControlGestures.Join(Effective(Commands.All.Single(c => c.Definition.Id == id), overrides));
    public string Input(string id, Dictionary<string, string> overrides) =>
        Format(Effective(Commands.All.Single(c => c.Definition.Id == id), overrides).FirstOrDefault());
    internal BindingSettings MigrateAliases(BindingSettings settings)
    {
        var next = new Dictionary<string, string>(settings.Overrides);
        var editors = new Dictionary<string, string>(settings.EditorOverrides ?? new());
        foreach (var group in Commands.NativeAliases.Where(p => next.ContainsKey(p.Key)).GroupBy(p => p.Value))
        {
            var target = Commands.All.Single(c => c.Definition.Id == group.Key);
            var retained = Effective(target, settings.Overrides).ToList();
            var consent = ControlGestures.ParseList(editors.GetValueOrDefault(group.Key, "")).ToList();
            bool moved = false;
            foreach (var alias in group.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var keys = ControlGestures.ParseList(next[alias.Key]);
                retained.AddRange(keys); moved |= keys.Length > 0;
                consent.AddRange(ControlGestures.ParseList(editors.GetValueOrDefault(alias.Key, ""))
                    .Where(g => keys.Any(k => ControlGestures.Same(k, g))));
                next.Remove(alias.Key); editors.Remove(alias.Key);
            }
            // Clearing the old standalone action never clears the native action.
            // A populated alias restores an explicitly cleared native action, as
            // happens after confirming overwrite on the former duplicate row.
            if (moved) next[group.Key] = ControlGestures.Join(retained);
            var claims = ControlGestures.Join(consent.Where(g => retained.Any(k => ControlGestures.Same(k, g))));
            if (claims.Length > 0) editors[group.Key] = claims; else editors.Remove(group.Key);
        }
        return new(next, editors);
    }
    public BindingChange Prepare(string id, string? value, Dictionary<string, string> draft, Dictionary<string, string>? editorOverrides = null)
        => Prepare(new Dictionary<string, string?> { [id] = value }, draft, editorOverrides);
    public BindingChange Prepare(IReadOnlyDictionary<string, string?> requests, Dictionary<string, string> draft, Dictionary<string, string>? editorOverrides = null)
    {
        var desired = requests.ToDictionary(p => p.Key, p => p.Value is null
            ? Effective(Commands.All.Single(c => c.Definition.Id == p.Key), new()).ToArray() : ControlGestures.ParseList(p.Value));
        var change = BindingChanges.Prepare(draft, requests, desired, Commands.All.Where(Commands.IsAvailable).Select(c =>
            new BindingAssignment(c.Definition.Id, c.Definition.Name, Effective(c, draft).ToArray())), editorOverrides);
        var names = new List<string>(change.Conflicts);
        foreach (var pair in desired)
        {
            var selected = Commands.All.Single(c => c.Definition.Id == pair.Key);
            var editorConflicts = OriginalInputs(selected.Definition.Native is null && selected.Definition.AllowInText)
            .Where(input => pair.Value.Any(g => g.Key == input.Key && g.Modifiers == input.Modifiers)
            && !Commands.All.Any(c => ReferenceEquals(c.Definition.Native, input.Command) || ReferenceEquals(c.Command, input.Command)))
            .ToArray();
            if (editorConflicts.Length > 0)
                change.EditorOverrides[pair.Key] = ControlGestures.Join(editorConflicts.Select(i => new KeyGesture(i.Key, i.Modifiers)));
            names.AddRange(editorConflicts.Select(input =>
            (input.Command is RoutedUICommand ui ? ui.Text : input.Command is RoutedCommand routed ? routed.Name : "Editor binding")
                + " (" + Format(new KeyGesture(input.Key, input.Modifiers)) + ")"));
        }
        return change with { Conflicts = names.Distinct().ToArray() };
    }
    public void Validate(BindingSettings settings)
    {
        if (settings.Overrides is null || settings.Overrides.Count > 10000) throw new ArgumentException("Invalid shortcut preferences.");
        foreach (var pair in settings.Overrides)
        {
            var command = Commands.All.FirstOrDefault(c => c.Definition.Id == pair.Key);
            foreach (var gesture in ControlGestures.ParseList(pair.Value))
            {
                // Conflict transfer can retain a provider's ordinary-key alternative.
                if (command is not null && Effective(command, new()).Any(g => ControlGestures.Same(g, gesture))) continue;
                Parse(Format(gesture)); // Retain bindings for temporarily absent mods.
            }
        }
        if (settings.EditorOverrides?.Count > 10000) throw new ArgumentException("Invalid editor shortcut preferences.");
        foreach (var pair in settings.EditorOverrides ?? new())
        {
            var command = Commands.All.FirstOrDefault(c => c.Definition.Id == pair.Key);
            foreach (var gesture in ControlGestures.ParseList(pair.Value))
                if (command is not null && !Effective(command, settings.Overrides).Any(g => ControlGestures.Same(g, gesture)))
                    throw new ArgumentException("An editor overwrite must match an assigned shortcut.");
        }
        var occupied = new Dictionary<(Key, ModifierKeys), RegisteredCommand>();
        foreach (var command in Commands.All.Where(Commands.IsAvailable))
            foreach (var gesture in Effective(command, settings.Overrides))
            {
                var key = (gesture.Key, gesture.Modifiers);
                if (occupied.TryGetValue(key, out var other) && other.Definition.Id != command.Definition.Id)
                {
                    // Host commands may intentionally share a key in different native contexts.
                    if (other.Definition.Native is not null && command.Definition.Native is not null
                        && Effective(other, new()).Any(g => ControlGestures.Same(g, gesture))
                        && Effective(command, new()).Any(g => ControlGestures.Same(g, gesture))) continue;
                    throw new ArgumentException(Format(gesture) + " conflicts: " + other.Definition.Name + " / " + command.Definition.Name);
                }
                occupied[key] = command;
            }
        // Explicit editor InputBindings are warned about in Prepare and suppressed
        // reversibly by Apply. They must not turn a confirmed overwrite into an error.
    }
    public void Apply(BindingSettings settings)
    {
        Validate(settings);
        var previous = current;
        try { ApplyCore(settings); }
        catch { ApplyCore(previous); throw; }
    }
    private void ApplyCore(BindingSettings settings)
    {
        Restore(); current = settings;
        SuppressEditorBindings(Elements(window));
        foreach (var command in Commands.All.Where(Commands.IsAvailable))
        {
            var def = command.Definition;
            if (def.Native is { } native)
            {
                if (!settings.Overrides.ContainsKey(def.Id)) continue;
                originals.TryAdd(native, native.InputGestures.Cast<InputGesture>().ToArray());
                native.InputGestures.Clear();
                foreach (var element in Elements(window))
                    foreach (var input in element.InputBindings.Cast<InputBinding>().Where(b => ReferenceEquals(b.Command, native)).ToArray())
                    { element.InputBindings.Remove(input); removed.Add((element, input)); }
            }
            foreach (var gesture in Effective(command, settings.Overrides)) Add(window, command, gesture);
        }
        foreach (var weak in scopes.ToArray()) if (weak.TryGetTarget(out var scope)) BindFloating(scope);
        foreach (var item in Menus((ItemsControl)window.FindName("mainMenu")))
        {
            var entry = Commands.All.FirstOrDefault(c => ReferenceEquals(c.Command, item.Command) || ReferenceEquals(c.Definition.Native, item.Command));
            if (entry is null || (entry.Definition.Native is not null && !settings.Overrides.ContainsKey(entry.Definition.Id))) continue;
            hints.Add((item,item.InputGestureText)); item.InputGestureText = Display(entry.Definition.Id, settings.Overrides);
        }
        CommandManager.InvalidateRequerySuggested();
    }
    private void Add(UIElement target, RegisteredCommand entry, KeyGesture gesture)
    {
        // Routed commands preserve the host's CanExecute checks and focus route.
        var binding = new KeyBinding(entry.Command, gesture) { CommandTarget = window };
        target.InputBindings.Add(binding); added.Add((target, binding));
    }
    private void BindFloating(UIElement target)
    {
        SuppressEditorBindings(Elements(target), floating: true);
        foreach (var command in Commands.All.Where(c => Commands.IsAvailable(c) && c.Definition.Native is null && c.Definition.AllowInText))
            foreach (var gesture in Effective(command, current.Overrides))
                if (!target.InputBindings.OfType<KeyBinding>().Any(b => b.Key == gesture.Key && b.Modifiers == gesture.Modifiers)) Add(target, command, gesture);
    }
    private IEnumerable<KeyBinding> OriginalInputs(bool includeFloating) => Elements(window)
        .Concat(includeFloating ? scopes.SelectMany(s => s.TryGetTarget(out var target) ? Elements(target) : []) : [])
        .Distinct().SelectMany(e => e.InputBindings.OfType<KeyBinding>())
        .Where(b => !added.Any(a => ReferenceEquals(a.Binding, b)))
        .Concat(removed.Where(r => includeFloating || Window.GetWindow(r.Owner) == window).Select(r => r.Binding).OfType<KeyBinding>()).Distinct();
    private void SuppressEditorBindings(IEnumerable<UIElement> elements, bool floating = false)
    {
        // Only keys explicitly approved in the conflict prompt may displace an
        // unregistered editor binding. Retained alternatives get no extra ownership.
        var claimed = Commands.All.Where(c => Commands.IsAvailable(c) && current.EditorOverrides?.ContainsKey(c.Definition.Id) == true)
            .Where(c => !floating || (c.Definition.Native is null && c.Definition.AllowInText))
            .SelectMany(c => ControlGestures.ParseList(current.EditorOverrides![c.Definition.Id])
                .Select(g => (Command: c, Gesture: g))).ToArray();
        foreach (var element in elements)
            foreach (var input in element.InputBindings.OfType<KeyBinding>().ToArray())
            {
                if (added.Any(a => ReferenceEquals(a.Binding, input))) continue;
                if (!claimed.Any(c => c.Gesture.Key == input.Key && c.Gesture.Modifiers == input.Modifiers
                    && !ReferenceEquals(c.Command.Command, input.Command) && !ReferenceEquals(c.Command.Definition.Native, input.Command))) continue;
                element.InputBindings.Remove(input); removed.Add((element, input));
            }
    }
    private static IEnumerable<UIElement> Elements(DependencyObject root)
    {
        if (root is UIElement element) yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private void Restore()
    {
        foreach (var (item, original) in hints) item.InputGestureText = original; hints.Clear();
        foreach (var (owner, binding) in added) owner.InputBindings.Remove(binding); added.Clear();
        foreach (var (owner, binding) in removed) owner.InputBindings.Add(binding); removed.Clear();
        foreach (var pair in originals) { pair.Key.InputGestures.Clear(); foreach (var gesture in pair.Value) pair.Key.InputGestures.Add(gesture); }
    }
    private static IEnumerable<MenuItem> Menus(ItemsControl parent)
    { foreach (var item in parent.Items.OfType<MenuItem>()) { yield return item; foreach (var child in Menus(item)) yield return child; } }
    public void Dispose() => Restore();
}
