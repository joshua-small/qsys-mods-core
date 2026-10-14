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
}
public sealed record RegisteredCommand(ModCommand Definition, RoutedUICommand Command);

/// <summary>UI-thread registry. Menus remain usable without a shortcut provider.</summary>
public static class Commands
{
    private static readonly Dictionary<string, RegisteredCommand> commands = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> providers = new(StringComparer.Ordinal);
    private static readonly List<(CommandBinding Binding, MenuItem? Menu)> installed = new();
    private static Window? window;
    public static IReadOnlyCollection<RegisteredCommand> All => commands.Values;
    public static event Action? Changed;
    public static RegisteredCommand Register(ModCommand definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || commands.ContainsKey(definition.Id)) throw new InvalidOperationException("Duplicate or empty command ID: " + definition.Id);
        var command = new RegisteredCommand(definition, new RoutedUICommand(definition.Name, definition.Id, typeof(Commands)));
        commands.Add(definition.Id, command);
        if (definition.Native is null && Mods.InitializingId is { } provider) providers.Add(definition.Id, provider);
        if (window is not null) Attach(command);
        Changed?.Invoke(); return command;
    }
    internal static void Initialize(Window target)
    {
        if (window is not null) return;
        window = target;
        foreach (var command in commands.Values) Attach(command);
        target.Closed += (_, _) =>
        {
            foreach (var item in installed) { target.CommandBindings.Remove(item.Binding); if (item.Menu?.Parent is ItemsControl parent) parent.Items.Remove(item.Menu); }
            installed.Clear(); window = null;
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
