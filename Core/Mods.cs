using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace QsysMods.Core;

/// <summary>Native loader adapter; Core owns visible command menus.</summary>
public abstract class ModModule : IDesignerModule
{
    public string Description => "Q-SYS Mods extension";
    public string MenuTitle => GetType().Name;
    public string Parent => "mainMenu_Tools";
    public string NestedMenu => "Q-SYS Mods";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };
}

public static class Mods
{
    private static readonly Dictionary<string, string> status = new();
    public static string? InitializingId { get; private set; }
    public static bool IsReady(string id) => status.GetValueOrDefault(id) == "Ready";
    public static string Status(string id) => status.GetValueOrDefault(id, "Not loaded");
    private static readonly Dictionary<string, Action<Window>> pending = new();
    private static readonly HashSet<string> registered = new();
    private static DispatcherTimer? timer;
    private static Window? ready;
    private static int attempts;
    public static void Register(string id, Action<Window> initialize)
    {
        SettingsStore.ValidateId(id);
        if (!Host.Enabled) return;
        if (!registered.Add(id)) throw new InvalidOperationException("Mod already registered: " + id);
        status[id] = "Waiting to initialize";
        if (ready is not null) { Run(id, initialize, ready); ModControls.CompleteInitialization(); return; }
        pending.Add(id, initialize);
        if (timer is not null) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += Tick; timer.Start();
    }
    private static void Tick(object? sender, EventArgs args)
    {
        if (++attempts > 120) { timer!.Stop(); Host.Error("mods-core", new TimeoutException("Designer commands did not become ready.")); return; }
        if (Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true } main) return;
        if (!main.CommandBindings.Cast<CommandBinding>().Any(b => ReferenceEquals(b.Command, Commands.Native("ColorSchemeToggle")))) return;
        timer!.Stop(); ready = main;
        try
        {
            Preferences.Install(); Commands.Initialize(main); ModControls.Initialize(main);
            Commands.Register(new("mods-core/preferences", "Mod Preferences…", "Q-SYS Mods Core", () => Preferences.Open(), () => true, InMenu: true));
            foreach (var pair in pending.ToArray()) Run(pair.Key, pair.Value, main);
            pending.Clear();
            ModControls.CompleteInitialization();
            // The host creates a menu item for every loader adapter. Replace those with registered operations.
            foreach (var item in Menus((ItemsControl)main.FindName("mainMenu")).Where(m => m.DataContext is ModModule).ToArray())
                item.Visibility = Visibility.Collapsed;
            Host.Receipt("mods-core", "ready", new { mods = registered.Order().ToArray(), commands = Commands.All.Select(c => c.Definition.Id).ToArray() });
        }
        catch (Exception ex) { Host.Error("mods-core", ex, true); }
    }
    private static void Run(string id, Action<Window> initialize, Window window)
    {
        status[id] = "Initializing"; InitializingId = id;
        try
        {
            if (id is "better-shortcuts" or "better-packing" && initialize.Method.DeclaringType?.Assembly.GetName().Version < new Version(0, 1, 1, 0))
                throw new InvalidOperationException("Update " + id + " to 0.1.1 or newer with Mods Core. Older builds install competing control handlers.");
            initialize(window); status[id] = "Ready";
        }
        catch (Exception ex) { status[id] = "Initialization failed"; Host.Error(id, ex, true); }
        finally { InitializingId = null; }
    }
    private static IEnumerable<MenuItem> Menus(ItemsControl parent)
    { foreach (var item in parent.Items.OfType<MenuItem>()) { yield return item; foreach (var child in Menus(item)) yield return child; } }
}
