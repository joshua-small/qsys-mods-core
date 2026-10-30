using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    private sealed record Page(string Id, string Name, Func<ControlDraft, Editor> Create, Func<bool>? Visible = null);
    private sealed record Editor(FrameworkElement View, Func<object?> Read, Action<object> Publish);
    private sealed record Session(SettingsStore Store, Dictionary<string, Editor> Editors, ControlDraft Controls);
    private static readonly List<Page> pages = new();
    internal sealed record Setting(Func<JsonNode> Read, Func<JsonNode, object> Validate, Action<object> Publish, JsonNode Schema);
    internal static readonly Dictionary<string, Setting> Settings = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions SettingsFormat = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };
    private static readonly ConditionalWeakTable<Window, Session> sessions = new();
    private static bool installed;
    public static string PathName => Path.Combine(Host.SettingsDirectory, "preferences.json");
    public static ModSettings<T> Register<T>(string id, string name, Func<T> defaults, Func<T, PreferenceEditor<T>> create, Action<T>? validate = null)
        => RegisterWithControls(id, name, defaults, (value, _) => create(value), validate);
    public static ModSettings<T> RegisterWithControls<T>(string id, string name, Func<T> defaults,
        Func<T, ControlDraft, PreferenceEditor<T>> create, Action<T>? validate = null)
    {
        SettingsStore.ValidateId(id);
        if (pages.Any(p => p.Id == id)) throw new InvalidOperationException("Preferences already registered: " + id);
        var current = new SettingsStore(PathName).Read(id, defaults); validate?.Invoke(current);
        var settings = new ModSettings<T>(current);
        Settings.Add(id, new(() => JsonSerializer.SerializeToNode(settings.Value, SettingsFormat)!, node =>
        {
            var value = node.Deserialize<T>(SettingsFormat) ?? throw new ArgumentException("Empty preferences for " + id);
            validate?.Invoke(value); return value;
        }, obj => settings.Update((T)obj), SettingsFormat.GetJsonSchemaAsNode(typeof(T))));
        pages.Add(new(id, name, draft =>
        {
            // Re-read disk for every dialog; opening Preferences never writes a migration.
            var value = new SettingsStore(PathName).Read(id, defaults); validate?.Invoke(value);
            var editor = create(value, draft);
            return new(editor.View, () => { var next = editor.Read(); validate?.Invoke(next); return next!; }, obj => settings.Update((T)obj));
        }));
        Install(); return settings;
    }
    public static void RegisterControlsPage(string id, string name, Func<ControlDraft, FrameworkElement> create)
        => RegisterControlsPage(id, name, create, () => true);
    public static void RegisterControlsPage(string id, string name, Func<ControlDraft, FrameworkElement> create, Func<bool> visible)
    {
        SettingsStore.ValidateId(id);
        if (pages.Any(p => p.Id == id)) throw new InvalidOperationException("Preferences already registered: " + id);
        pages.Add(new(id, name, draft => new(create(draft), () => null, _ => { }), visible));
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
            var store = new SettingsStore(PathName);
            var session = new Session(store, new(), new(ModControls.Load(store)));
            var visible = pages.Where(p => p.Visible?.Invoke() != false).OrderBy(p => p.Name).ToArray();
            // Build before mutating the host so a bad page cannot leave a half-attached session.
            foreach (var page in visible) session.Editors.Add(page.Id, page.Create(session.Controls));
            foreach (var page in visible)
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
            var values = session.Editors.Select(p => (p.Key, Value: p.Value.Read())).Where(p => p.Value is not null)
                .ToDictionary(p => p.Key, p => p.Value!);
            var controls = session.Controls.Value;
            ModControls.Validate(controls);
            values.Add(ModControls.SettingsId, controls);
            var previous = ModControls.Current;
            ModControls.Publish(controls);
            try { session.Store.Save(values); }
            catch { ModControls.Publish(previous); throw; }
            foreach (var pair in values.Where(p => p.Key != ModControls.SettingsId)) session.Editors[pair.Key].Publish(pair.Value);
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
        window.ShowInTaskbar = true;
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowDialog();
    }
    public static void SelectPage(FrameworkElement source, string id)
    {
        if (Window.GetWindow(source) is not { } window) return;
        var tabs = Logical(window).OfType<TabControl>().Single();
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(t => Equals(t.Tag, "qsys-mod:" + id));
    }
    public static IEnumerable<DependencyObject> Logical(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Logical(child)) yield return descendant;
    }
}
