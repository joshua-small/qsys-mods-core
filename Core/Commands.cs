using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace QsysMods.Core;

public sealed record ModCommand(string Id, string Name, string Group, Action Execute, Func<bool> CanExecute,
    KeyGesture? DefaultGesture = null, bool InMenu = false, RoutedCommand? Native = null, bool AllowInText = false)
{
    public string? When { get; init; }
    public string? DefaultBinding { get; init; }
    public Func<bool>? ShortcutContext { get; init; }
    public bool AllowOrdinaryKey { get; init; }
}
public sealed record RegisteredCommand(ModCommand Definition, RoutedUICommand Command);
public sealed record CommandDefault(string ProviderId, string ProviderName, string Binding, string When, Func<bool>? Context);

/// <summary>UI-thread registry. Menus remain usable without a shortcut provider.</summary>
public static class Commands
{
    private static readonly Dictionary<string, RegisteredCommand> commands = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> providers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, CommandDefault> defaults = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string Target, string Provider)> nativeAliases = new(StringComparer.Ordinal);
    private static readonly Dictionary<MenuItem, HashSet<string>> menuHints = new();
    private static readonly List<(CommandBinding Binding, MenuItem? Menu)> installed = new();
    private static Window? window;
    public static IReadOnlyCollection<RegisteredCommand> All => commands.Values;
    public static event Action? Changed;
    public static RegisteredCommand Register(ModCommand definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || commands.ContainsKey(definition.Id) || nativeAliases.ContainsKey(definition.Id)) throw new InvalidOperationException("Duplicate or empty command ID: " + definition.Id);
        if (definition.DefaultBinding is { } value)
            foreach (var sequence in ControlSequence.ParseList(value)) sequence.Validate(definition.AllowOrdinaryKey && definition.ShortcutContext is not null);
        var command = new RegisteredCommand(definition, new RoutedUICommand(definition.Name, definition.Id, typeof(Commands)));
        commands.Add(definition.Id, command);
        if (definition.Native is null && Mods.InitializingId is { } provider) providers.Add(definition.Id, provider);
        if (window is not null) Attach(command);
        Changed?.Invoke(); return command;
    }
    /// <summary>Supply keys for a canonical native action. User assignments always win.</summary>
    public static void RegisterDefaultBinding(RoutedCommand native, string binding, string providerName, string when, Func<bool>? context = null)
    {
        if (Mods.InitializingId is not { } provider) throw new InvalidOperationException("Declare defaults during mod initialization.");
        foreach (var sequence in ControlSequence.ParseList(binding)) sequence.Validate();
        if (!defaults.TryAdd(NativeId(native), new(provider, providerName, binding, when, context)))
            throw new ArgumentException("A default is already declared for this action; use a reviewed shortcut proposal for competing changes.");
    }
    public static CommandDefault? DefaultFor(string id) => defaults.TryGetValue(id, out var value) && Mods.IsReady(value.ProviderId) ? value : null;
    /// <summary>Retire an old mod action in favor of one native action and its current provider.
    /// Saved keys migrate on load; this does not create a second bindable command.</summary>
    public static void RegisterNativeAlias(string oldId, RoutedCommand native)
    {
        if (Mods.InitializingId is not { } provider || !oldId.StartsWith(provider + "/", StringComparison.Ordinal)
            || oldId.Length <= provider.Length + 1 || native.OwnerType == typeof(Commands)
            || commands.ContainsKey(oldId) || !nativeAliases.TryAdd(oldId, (NativeId(native), provider)))
            throw new ArgumentException("Invalid or duplicate native command alias.");
    }
    internal static IReadOnlyDictionary<string, string> NativeAliases => nativeAliases
        .Where(p => Mods.IsReady(p.Value.Provider) && commands.ContainsKey(p.Value.Target))
        .ToDictionary(p => p.Key, p => p.Value.Target, StringComparer.Ordinal);
    /// <summary>Show this mod action's current keys beside an existing host menu item.
    /// The menu's command, click handlers and availability are left intact.</summary>
    public static void RegisterMenuHint(MenuItem item, string commandId)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Mods.InitializingId is not { } provider || !commands.ContainsKey(commandId) || ProviderId(commandId) != provider)
            throw new ArgumentException("Menu hints must refer to an action owned by the initializing mod.");
        if (!menuHints.TryGetValue(item, out var ids)) menuHints.Add(item, ids = new(StringComparer.Ordinal));
        ids.Add(commandId);
    }
    internal static IEnumerable<RegisteredCommand> MenuCommands(MenuItem item) => commands.Values.Where(c => IsAvailable(c)
        && (ReferenceEquals(c.Command, item.Command) || (c.Definition.Native is { } native && ReferenceEquals(native, item.Command))
            || (menuHints.TryGetValue(item, out var ids) && ids.Contains(c.Definition.Id))));
    internal static void Initialize(Window target)
    {
        if (window is not null) return;
        window = target;
        foreach (var command in commands.Values) Attach(command);
        target.Closed += (_, _) =>
        {
            foreach (var item in installed) { target.CommandBindings.Remove(item.Binding); if (item.Menu?.Parent is ItemsControl parent) parent.Items.Remove(item.Menu); }
            installed.Clear(); menuHints.Clear(); window = null;
        };
    }
    private static void Attach(RegisteredCommand entry)
    {
        var def = entry.Definition;
        var binding = new CommandBinding(entry.Command, (_, e) =>
        {
            e.Handled = true;
            try { if (Host.Enabled && IsAvailable(entry) && def.CanExecute()) def.Execute(); }
            catch (Exception ex) { Host.Error("mods-core", ex, true); }
        }, (_, e) =>
        {
            try { e.CanExecute = Host.Enabled && window!.IsEnabled && IsAvailable(entry) && def.CanExecute(); } catch { e.CanExecute = false; }
            e.Handled = true;
        });
        window!.CommandBindings.Add(binding);
        MenuItem? menu = null;
        if (def.InMenu)
        {
            var tools = (MenuItem)window.FindName("mainMenu_Tools");
            var mods = tools.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Header, "Q-SYS Mods"));
            if (mods is null) { mods = new MenuItem { Header = "Q-SYS Mods" }; tools.Items.Add(mods); }
            menu = new MenuItem { Header = def.Name, Command = entry.Command, CommandTarget = window };
            mods.Items.Add(menu);
        }
        installed.Add((binding, menu));
    }
    public static void DiscoverNative(Window target)
    {
        foreach (var command in target.CommandBindings.Cast<CommandBinding>().Select(b => b.Command).OfType<RoutedCommand>()
            .Where(c => c.OwnerType != typeof(Commands)).Distinct().ToArray())
        {
            string id = NativeId(command);
            if (commands.ContainsKey(id)) continue;
            var native = command;
            var label = command is RoutedUICommand ui && !string.IsNullOrWhiteSpace(ui.Text) ? ui.Text : command.Name;
            Register(new(id, label.Replace("_", ""), "Q-SYS", () => native.Execute(null, NativeTarget(target)),
                () => native.CanExecute(null, NativeTarget(target)), native.InputGestures.OfType<KeyGesture>().FirstOrDefault(), Native: native, AllowInText: true));
        }
    }
    public static string NativeId(RoutedCommand command) => "native/" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(command.OwnerType.FullName + "/" + command.Name))).ToLowerInvariant()[..20];
    public static string? ProviderId(string commandId) => providers.GetValueOrDefault(commandId);
    public static bool IsAvailable(RegisteredCommand entry) => ProviderId(entry.Definition.Id) is not { } id || Mods.IsReady(id);
    public static IInputElement NativeTarget(Window target) => Keyboard.FocusedElement is { } focused && focused is DependencyObject obj && Window.GetWindow(obj) == target
        ? focused : target;
    public static RoutedCommand Native(string name)
    {
        var type = Host.Type("QSC.QSys.Commands." + name);
        return (RoutedCommand)(type.GetField("Command", Host.Static)?.GetValue(null) ?? type.GetProperty("Command", Host.Static)?.GetValue(null)
            ?? throw new MissingMemberException(type.FullName, "Command"));
    }
}
