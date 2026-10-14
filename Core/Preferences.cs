using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using HarmonyLib;

namespace QsysMods.Core;

public sealed record PreferenceEditor<T>(FrameworkElement View, Func<T> Read);
public sealed class ModSettings<T>
{
    public T Value { get; private set; }
    public event Action? Changed;
    internal ModSettings(T value) => Value = value;
    internal void Update(T value)
    {
        Value = value;
        foreach (var callback in Changed?.GetInvocationList() ?? [])
            try { ((Action)callback)(); } catch (Exception ex) { Host.Error("mods-core", ex); }
    }
}

public static class Preferences
{
    private sealed record Page(string Id, string Name, Func<Editor> Create);
    private sealed record Editor(FrameworkElement View, Func<object> Read, Action<object> Publish);
    private sealed record Session(SettingsStore Store, Dictionary<string, Editor> Editors);
    private static readonly List<Page> pages = new();
    private static readonly ConditionalWeakTable<Window, Session> sessions = new();
    private static bool installed;
    public static string PathName => Path.Combine(Host.SettingsDirectory, "preferences.json");
    public static ModSettings<T> Register<T>(string id, string name, Func<T> defaults, Func<T, PreferenceEditor<T>> create, Action<T>? validate = null)
    {
        SettingsStore.ValidateId(id);
        if (pages.Any(p => p.Id == id)) throw new InvalidOperationException("Preferences already registered: " + id);
        var current = new SettingsStore(PathName).Read(id, defaults); validate?.Invoke(current);
        var settings = new ModSettings<T>(current);
        pages.Add(new(id, name, () =>
        {
            // Re-read disk for every dialog; opening Preferences never writes a migration.
            var value = new SettingsStore(PathName).Read(id, defaults); validate?.Invoke(value);
            var editor = create(value);
            return new(editor.View, () => { var next = editor.Read(); validate?.Invoke(next); return next!; }, obj => settings.Update((T)obj));
        }));
        Install(); return settings;
    }
    public static void Install()
    {
        if (installed || !Host.Enabled) return;
        var type = Host.Type("QSC.QSys.windows.Dialogs.PreferencesDialog");
        var ok = type.GetMethod("OnOk", Host.Instance) ?? throw new MissingMethodException(type.FullName, "OnOk");
        if (Harmony.GetPatchInfo(ok)?.Owners.Any() is true) throw new InvalidOperationException("Preferences save is already modified.");
        new Harmony("qsys.mods.core.preferences").Patch(ok, prefix: new HarmonyMethod(typeof(Preferences), nameof(BeforeSave)));
        EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent, new RoutedEventHandler(Loaded), true);
        installed = true;
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window || sessions.TryGetValue(window, out _)) return;
        try
        {
            var tabs = Logical(window).OfType<TabControl>().Single();
            var session = new Session(new SettingsStore(PathName), new());
            // Build before mutating the host so a bad page cannot leave a half-attached session.
            foreach (var page in pages.OrderBy(p => p.Name)) session.Editors.Add(page.Id, page.Create());
            foreach (var page in pages.OrderBy(p => p.Name))
            {
                var view = session.Editors[page.Id].View;
                var foreground = Host.Type("ActiproSoftware.Windows.Themes.AssetResourceKeys").GetProperty("ContainerForegroundLowNormalBrushKey", Host.Static)!.GetValue(null)!;
                view.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, foreground);
                tabs.Items.Add(new TabItem { Header = page.Name, Tag = "qsys-mod:" + page.Id, Content = view });
            }
            sessions.Add(window, session);
        }
        catch (Exception ex) { Host.Error("mods-core", ex, true); }
    }
    private static bool BeforeSave(object __instance)
    {
        if (__instance is not Window window || !sessions.TryGetValue(window, out var session)) return true;
        try
        {
            var values = session.Editors.ToDictionary(p => p.Key, p => p.Value.Read());
            session.Store.Save(values);
            foreach (var pair in values) session.Editors[pair.Key].Publish(pair.Value);
            return true;
        }
        catch (Exception ex) { Host.Error("mods-core", ex, true); return false; }
    }
    public static void Open(string? id = null)
    {
        var window = (Window)Activator.CreateInstance(Host.Type("QSC.QSys.windows.Dialogs.PreferencesDialog"))!;
        window.Owner = Application.Current.MainWindow;
        if (id is not null) window.Loaded += (_, _) =>
        {
            var tabs = Logical(window).OfType<TabControl>().Single();
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Tag, "qsys-mod:" + id)) ?? tabs.SelectedItem;
        };
        window.ShowDialog();
    }
    public static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Logical(child)) yield return descendant;
    }
}
