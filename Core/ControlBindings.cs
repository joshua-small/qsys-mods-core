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
    private readonly Dictionary<ContextMenu, List<(MenuItem Item, string Original)>> contextHints = new();
    private BindingSettings current = new(new());
    private readonly ControlChordRouter chords;
    private ChordEntry[] resolvedEntries = [];
    public ControlBindings(Window window)
    {
        this.window = window;
        chords = new(window, () => resolvedEntries, (entry, target) => Execute(entry, target));
        window.AddHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler(ContextOpening), true);
        // The native floating Component Controls window has a separate input route.
        EventManager.RegisterClassHandler(Host.Type("QSC.QSys.windows.ComponentInfo"), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is not UIElement element || scopes.Any(s => s.TryGetTarget(out var target) && ReferenceEquals(target, element))) return;
            scopes.Add(new(element));
            // Actipro can load the control before reparenting it into its final
            // floating presentation source. Follow that move, including redocking.
            PresentationSource.AddSourceChangedHandler(element, ScopeSourceChanged);
            BindFloating(element);
        }), true);
    }
    private void ScopeSourceChanged(object sender, SourceChangedEventArgs e)
    {
        if (sender is UIElement element && e.NewSource is not null) BindFloating(element);
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
    private bool ManagedDefault(RegisteredCommand command) => command.Definition.DefaultBinding is not null || Commands.DefaultFor(command.Definition.Id) is not null;
    private bool ModDefault(RegisteredCommand command) => command.Definition.Native is null || ManagedDefault(command);
    private IEnumerable<ControlSequence> RawEffective(RegisteredCommand command, Dictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(command.Definition.Id, out var text)) return ControlSequence.ParseList(text);
        if (Commands.DefaultFor(command.Definition.Id) is { } declared) return ControlSequence.ParseList(declared.Binding);
        if (command.Definition.DefaultBinding is { } binding) return ControlSequence.ParseList(binding);
        if (command.Definition.Native is { } native) return (originals.TryGetValue(native, out var saved) ? saved.OfType<KeyGesture>() : native.InputGestures.OfType<KeyGesture>()).Select(ControlSequence.From);
        return command.Definition.DefaultGesture is { } value ? [ControlSequence.From(value)] : [];
    }
    private IEnumerable<ControlSequence> Effective(RegisteredCommand command, Dictionary<string, string> overrides)
    {
        var values = RawEffective(command, overrides);
        if (overrides.ContainsKey(command.Definition.Id) || !ModDefault(command)) return values;
        // An upgrade never takes a user's key. Competing mod defaults both stay
        // inactive; registration order must not decide which mod wins.
        return values.Where(s => DefaultConflictNames(command, s, overrides).Length == 0);
    }
    private string[] DefaultConflictNames(RegisteredCommand command, ControlSequence sequence, Dictionary<string, string> overrides)
    {
        var names = Commands.All.Where(c => c != command && Commands.IsAvailable(c)
            && RawEffective(c, overrides).Any(sequence.Conflicts)).Select(c => c.Definition.Name).ToList();
        names.AddRange(ProtectedInputs(command, overrides, command.Definition.AllowInText).Where(b => sequence.First == new ControlStroke(b.Key, b.Modifiers))
            .Select(_ => "Native/editor input binding"));
        names.AddRange(MenuAccessKeys().Where(m => m.Stroke == sequence.First).Select(m => m.Name + " menu access key"));
        return names.Distinct().ToArray();
    }
    // Registered native actions may also have direct InputBindings absent from
    // their InputGestures. Only an explicit native override/managed declaration
    // removes those bindings; otherwise they still require takeover consent.
    private IEnumerable<KeyBinding> ProtectedInputs(RegisteredCommand command, Dictionary<string, string> overrides, bool includeFloating = false) =>
        OriginalInputs(includeFloating).Where(b => !ReferenceEquals(b.Command, command.Command)
            && !ReferenceEquals(b.Command, command.Definition.Native)
            && !Commands.All.Any(c => c.Definition.Native is { } native && ReferenceEquals(native, b.Command)
                && (overrides.ContainsKey(c.Definition.Id) || ManagedDefault(c))));
    /// <summary>Defaults left inactive by Core, including chord-prefix conflicts.</summary>
    public IReadOnlyList<DefaultShortcutConflict> DefaultConflicts(Dictionary<string, string> overrides) => Commands.All
        .Where(c => Commands.IsAvailable(c) && ModDefault(c) && !overrides.ContainsKey(c.Definition.Id))
        .SelectMany(c => RawEffective(c, overrides).Select(s => new DefaultShortcutConflict(c.Definition.Id, s.Serialize(), DefaultConflictNames(c, s, overrides))))
        .Where(c => c.Owners.Length > 0).ToArray();
    public string DefaultDisplay(string id) => ControlSequence.Join(RawEffective(Commands.All.Single(c => c.Definition.Id == id), new()));
    public string Display(string id, Dictionary<string, string> overrides) => ControlSequence.Join(Effective(Commands.All.Single(c => c.Definition.Id == id), overrides));
    public string Input(string id, Dictionary<string, string> overrides) =>
        Effective(Commands.All.Single(c => c.Definition.Id == id), overrides).FirstOrDefault()?.Serialize() ?? "";
    internal BindingSettings MigrateAliases(BindingSettings settings)
    {
        var next = new Dictionary<string, string>(settings.Overrides);
        var editors = new Dictionary<string, string>(settings.EditorOverrides ?? new());
        foreach (var group in Commands.NativeAliases.Where(p => next.ContainsKey(p.Key)).GroupBy(p => p.Value))
        {
            var target = Commands.All.Single(c => c.Definition.Id == group.Key);
            var retained = Effective(target, settings.Overrides).ToList();
            var consent = ControlSequence.ParseList(editors.GetValueOrDefault(group.Key, "")).ToList();
            bool moved = false;
            foreach (var alias in group.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var keys = ControlSequence.ParseList(next[alias.Key]);
                retained.AddRange(keys); moved |= keys.Length > 0;
                consent.AddRange(ControlSequence.ParseList(editors.GetValueOrDefault(alias.Key, ""))
                    .Where(keys.Contains));
                next.Remove(alias.Key); editors.Remove(alias.Key);
            }
            // Clearing the old standalone action never clears the native action.
            // A populated alias restores an explicitly cleared native action, as
            // happens after confirming overwrite on the former duplicate row.
            if (moved) next[group.Key] = ControlSequence.Join(retained);
            var claims = ControlSequence.Join(consent.Where(retained.Contains));
            if (claims.Length > 0) editors[group.Key] = claims; else editors.Remove(group.Key);
        }
        return new(next, editors);
    }
    public BindingChange Prepare(string id, string? value, Dictionary<string, string> draft, Dictionary<string, string>? editorOverrides = null)
        => Prepare(new Dictionary<string, string?> { [id] = value }, draft, editorOverrides);
    public BindingChange Prepare(IReadOnlyDictionary<string, string?> requests, Dictionary<string, string> draft, Dictionary<string, string>? editorOverrides = null)
    {
        var desired = requests.ToDictionary(p => p.Key, p => p.Value is null
            ? RawEffective(Commands.All.Single(c => c.Definition.Id == p.Key), new()).ToArray() : ControlSequence.ParseList(p.Value));
        var change = SequenceChanges.Prepare(draft, requests, desired, Commands.All.Where(Commands.IsAvailable).Select(c =>
            new SequenceAssignment(c.Definition.Id, c.Definition.Name, RawEffective(c, draft).ToArray())), editorOverrides);
        var names = new List<string>(change.Conflicts);
        foreach (var pair in desired)
        {
            var selected = Commands.All.Single(c => c.Definition.Id == pair.Key);
            var editorConflicts = ProtectedInputs(selected, change.Overrides, selected.Definition.Native is null && selected.Definition.AllowInText)
            .Where(input => pair.Value.Any(g => g.First.Key == input.Key && g.First.Modifiers == input.Modifiers))
            .ToArray();
            if (editorConflicts.Length > 0)
            {
                change.EditorOverrides[pair.Key] = ControlSequence.Join(pair.Value.Where(g => editorConflicts.Any(i => g.First == new ControlStroke(i.Key, i.Modifiers))));
                // A reviewed Reset is an explicit choice when a native/editor
                // binding must yield; leaving it implicit would suppress it again.
                change.Overrides[pair.Key] = ControlSequence.Join(pair.Value);
            }
            names.AddRange(editorConflicts.Select(input =>
            (input.Command is RoutedUICommand ui ? ui.Text : input.Command is RoutedCommand routed ? routed.Name : "Editor binding")
                + " (" + Format(new KeyGesture(input.Key, input.Modifiers)) + ")"));
        }
        // Native access keys are not InputBindings. Preview routing can take
        // them only after the same explicit overwrite confirmation.
        foreach (var pair in desired)
        {
            var matches = MenuAccessKeys().Where(m => pair.Value.Any(g => g.First == m.Stroke)).ToArray();
            if (matches.Length == 0) continue;
            names.AddRange(matches.Select(m => m.Name + " menu access key (" + m.Stroke.Label + ")"));
            change.Overrides[pair.Key] = ControlSequence.Join(pair.Value);
            change.EditorOverrides[pair.Key] = ControlSequence.Join(ControlSequence.ParseList(change.EditorOverrides.GetValueOrDefault(pair.Key, ""))
                .Concat(pair.Value.Where(g => matches.Any(m => m.Stroke == g.First))));
        }
        return change with { Conflicts = names.Distinct().ToArray() };
    }
    public void Validate(BindingSettings settings)
    {
        if (settings.FormatVersion is < 1 or > 2 || settings.Overrides is null || settings.Overrides.Count > 10000) throw new ArgumentException("Unsupported shortcut preferences format.");
        foreach (var pair in settings.Overrides)
        {
            var command = Commands.All.FirstOrDefault(c => c.Definition.Id == pair.Key);
            foreach (var gesture in ControlSequence.ParseList(pair.Value))
            {
                // Conflict transfer can retain a provider's ordinary-key alternative.
                if (command is not null && RawEffective(command, new()).Contains(gesture)) continue;
                gesture.Validate(command?.Definition is { AllowOrdinaryKey: true, ShortcutContext: not null });
            }
        }
        if (settings.EditorOverrides?.Count > 10000) throw new ArgumentException("Invalid editor shortcut preferences.");
        foreach (var pair in settings.EditorOverrides ?? new())
        {
            var command = Commands.All.FirstOrDefault(c => c.Definition.Id == pair.Key);
            foreach (var gesture in ControlSequence.ParseList(pair.Value))
                if (command is not null && !Effective(command, settings.Overrides).Contains(gesture))
                    throw new ArgumentException("An editor overwrite must match an assigned shortcut.");
        }
        var occupied = new List<(ControlSequence Sequence, RegisteredCommand Command)>();
        foreach (var command in Commands.All.Where(Commands.IsAvailable))
            foreach (var gesture in Effective(command, settings.Overrides))
            {
                if (settings.Overrides.ContainsKey(command.Definition.Id)
                    && !ControlSequence.ParseList(settings.EditorOverrides?.GetValueOrDefault(command.Definition.Id, "") ?? "").Contains(gesture)
                    && (ProtectedInputs(command, settings.Overrides, command.Definition.Native is null && command.Definition.AllowInText).Any(b => gesture.First == new ControlStroke(b.Key, b.Modifiers))
                        || MenuAccessKeys().Any(m => m.Stroke == gesture.First)))
                    throw new ArgumentException(gesture.Label + " conflicts with a native/editor binding or menu access key and requires matching EditorOverrides consent.");
                foreach (var collision in occupied.Where(o => o.Sequence.Conflicts(gesture) && (o.Command != command || o.Sequence != gesture)))
                {
                    var other = collision.Command;
                    // Host commands may intentionally share a key in different native contexts.
                    if (other.Definition.Native is not null && command.Definition.Native is not null
                        && !ManagedDefault(other) && !ManagedDefault(command)
                        && RawEffective(other, new()).Contains(gesture)
                        && RawEffective(command, new()).Contains(gesture)) continue;
                    throw new ArgumentException(gesture.Label + " conflicts: " + other.Definition.Name + " / " + command.Definition.Name);
                }
                occupied.Add((gesture, command));
            }
        // Prepare supplies scoped consent; direct settings edits must supply it
        // too. Apply suppresses those bindings reversibly after validation.
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
                if (!settings.Overrides.ContainsKey(def.Id) && !ManagedDefault(command)) continue;
                originals.TryAdd(native, native.InputGestures.Cast<InputGesture>().ToArray());
                native.InputGestures.Clear();
                foreach (var element in Elements(window))
                    foreach (var input in element.InputBindings.Cast<InputBinding>().Where(b => ReferenceEquals(b.Command, native)).ToArray())
                    { element.InputBindings.Remove(input); removed.Add((element, input)); }
            }
            foreach (var gesture in Effective(command, settings.Overrides).Where(g => !RoutedInPreview(command, g))) Add(window, command, gesture);
        }
        foreach (var weak in scopes.ToArray()) if (weak.TryGetTarget(out var scope)) BindFloating(scope);
        foreach (var item in Menus((ItemsControl)window.FindName("mainMenu")))
        {
            var entries = Commands.MenuCommands(item).ToArray();
            if (!entries.Any(c => c.Definition.Native is null || settings.Overrides.ContainsKey(c.Definition.Id) || ManagedDefault(c))) continue;
            hints.Add((item,item.InputGestureText));
            item.InputGestureText = string.Join(" or ", entries.SelectMany(c => Effective(c, settings.Overrides)).Distinct().Select(g => g.Label));
        }
        resolvedEntries = Entries().ToArray();
        // File edits can apply while a context menu stays open. Keep its hint
        // current, while preserving the native text restored when it closes.
        foreach (var menu in contextHints.Keys.ToArray()) RefreshContextMenu(menu);
        CommandManager.InvalidateRequerySuggested();
    }
    private void Add(UIElement target, RegisteredCommand entry, ControlSequence sequence)
    {
        var gesture = new KeyGesture(sequence.First.Key, sequence.First.Modifiers);
        // Routed commands preserve the host's CanExecute checks and focus route.
        var binding = new KeyBinding(entry.Command, gesture) { CommandTarget = window };
        target.InputBindings.Add(binding); added.Add((target, binding));
    }
    private void BindFloating(UIElement target)
    {
        SuppressEditorBindings(Elements(target), floating: true);
        foreach (var command in Commands.All.Where(c => Commands.IsAvailable(c) && c.Definition.Native is null && c.Definition.AllowInText))
            foreach (var gesture in Effective(command, current.Overrides).Where(g => !RoutedInPreview(command, g)))
                if (!target.InputBindings.OfType<KeyBinding>().Any(b => b.Key == gesture.First.Key && b.Modifiers == gesture.First.Modifiers)) Add(target, command, gesture);
        if (Window.GetWindow(target) is { } owner) chords.Attach(owner);
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
            .SelectMany(c => ControlSequence.ParseList(current.EditorOverrides![c.Definition.Id])
                .Select(g => (Command: c, Gesture: g))).ToArray();
        foreach (var element in elements)
            foreach (var input in element.InputBindings.OfType<KeyBinding>().ToArray())
            {
                if (added.Any(a => ReferenceEquals(a.Binding, input))) continue;
                if (!claimed.Any(c => c.Gesture.First.Key == input.Key && c.Gesture.First.Modifiers == input.Modifiers
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
        chords.Cancel();
        resolvedEntries = [];
        foreach (var (item, original) in hints) item.InputGestureText = original; hints.Clear();
        foreach (var (owner, binding) in added) owner.InputBindings.Remove(binding); added.Clear();
        foreach (var (owner, binding) in removed) owner.InputBindings.Add(binding); removed.Clear();
        foreach (var pair in originals) { pair.Key.InputGestures.Clear(); foreach (var gesture in pair.Value) pair.Key.InputGestures.Add(gesture); }
    }
    private static IEnumerable<MenuItem> Menus(ItemsControl parent)
    { foreach (var item in parent.Items.OfType<MenuItem>()) { yield return item; foreach (var child in Menus(item)) yield return child; } }
    private void ContextOpening(object sender, ContextMenuEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null; source = ControlInput.Parent(source))
            if (source is FrameworkElement { ContextMenu: { } menu }) { RefreshContextMenu(menu); break; }
    }
    internal void RefreshContextMenu(ContextMenu menu)
    {
        RestoreContextMenu(menu);
        var originals = new List<(MenuItem, string)>();
        foreach (var item in Menus(menu))
        {
            var entries = Commands.MenuCommands(item).ToArray();
            if (!entries.Any(c => c.Definition.Native is null || current.Overrides.ContainsKey(c.Definition.Id) || ManagedDefault(c))) continue;
            originals.Add((item, item.InputGestureText));
            item.InputGestureText = string.Join(" or ", entries.SelectMany(c => Effective(c, current.Overrides)).Distinct().Select(g => g.Label));
        }
        contextHints.Add(menu, originals);
        menu.Closed += ContextClosed;
    }
    private void ContextClosed(object sender, RoutedEventArgs e) { if (sender is ContextMenu menu) RestoreContextMenu(menu); }
    private void RestoreContextMenu(ContextMenu menu)
    {
        if (!contextHints.Remove(menu, out var originals)) return;
        foreach (var (item, text) in originals) item.InputGestureText = text;
        menu.Closed -= ContextClosed;
    }
    // Mod keys must use the same text/floating/modal guards as chords. A direct
    // KeyBinding to the menu command bypasses AllowInText during WPF routing.
    private bool RoutedInPreview(RegisteredCommand command, ControlSequence sequence) => command.Definition.Native is null || sequence.IsChord || ManagedDefault(command)
        || command.Definition.ShortcutContext is not null || MenuAccessKeys().Any(m => m.Stroke == sequence.First);
    private IEnumerable<(ControlStroke Stroke, string Name)> MenuAccessKeys()
    {
        if (window.FindName("mainMenu") is not ItemsControl menu) yield break;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            var label = item.Header as string ?? ""; int marker = label.IndexOf('_');
            if (marker < 0 || marker + 1 >= label.Length) continue;
            if (Enum.TryParse<Key>(label[(marker + 1)..(marker + 2)].ToUpperInvariant(), out var key))
                yield return (new(key, ModifierKeys.Alt), label.Replace("_", ""));
        }
    }
    private IEnumerable<ChordEntry> Entries() => Commands.All.Where(Commands.IsAvailable).SelectMany(c => Effective(c, current.Overrides)
        .Where(s => RoutedInPreview(c, s)).Select(s => new ChordEntry(c, s)));
    private void Execute(RegisteredCommand command, Window target)
    {
        // Native actions retain the pre-overlay focus route; no native fallthrough
        // after a claimed stroke, including a temporarily unavailable action.
        try { if (ControlInput.Available(command, window, target)) command.Definition.Execute(); }
        catch (Exception ex) { Host.Error("mods-core", ex, true); }
    }
    public void Dispose()
    {
        Restore(); chords.Dispose();
        foreach (var weak in scopes) if (weak.TryGetTarget(out var scope)) PresentationSource.RemoveSourceChangedHandler(scope, ScopeSourceChanged);
        window.RemoveHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler(ContextOpening));
        foreach (var menu in contextHints.Keys.ToArray()) RestoreContextMenu(menu);
    }
}
