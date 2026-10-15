using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;

namespace QsysMods.Core;

/// <summary>The shared settings.json entry point. Start only after all mod registrations complete.</summary>
public static class SharedSettings
{
    public static string PathName => Path.Combine(Host.SettingsDirectory, "settings.json");
    public static string? Error => synchronizer?.Error ?? startupError;
    public static event Action? Changed;
    private static SettingsSynchronizer? synchronizer;
    private static DispatcherTimer? timer;
    private static string? startupError;
    internal static void Initialize(Window main)
    {
        if (timer is not null) return;
        Commands.Register(new("mods-core/settings-json", "Open Settings JSON…", "Q-SYS Mods Core", Open, () => true, InMenu: true));
        var statusCommand = Commands.Register(new("mods-core/settings-json-status", "Settings JSON Status…", "Q-SYS Mods Core", () =>
            MessageBox.Show(main, Error ?? "Live settings are synchronized. Edit settings.json and save to apply validated changes.", "Settings JSON"), () => true, InMenu: true));
        Commands.Register(new("mods-core/settings-json-retry", "Retry Settings JSON", "Q-SYS Mods Core", Retry, () => synchronizer is not null && Error is not null, InMenu: true));
        try
        {
            if (Host.IsLab && new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData }
                .Any(folder => !Environment.GetFolderPath(folder).StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Native Q-SYS preferences are not isolated in this lab. Start the disposable copy with native profile redirection before using live settings.");
            var target = new SharedSettingsTarget();
            synchronizer = new(PathName, target);
            var schemaPath = Path.Combine(Host.SettingsDirectory, "settings.schema.json");
            SettingsJson.Write(schemaPath, SettingsJson.Text(target.Schema()), SettingsJson.Read(schemaPath));
            synchronizer.Changed += () =>
            {
                if (synchronizer.Error is { } error) Host.Receipt("mods-core", "settings-json-rejected", new { error });
                foreach (var item in Preferences.Logical(main).OfType<MenuItem>().Where(m => ReferenceEquals(m.Command, statusCommand.Command)))
                    item.Header = synchronizer.Error is null ? "Settings JSON Status…" : "Settings JSON Error…";
                Changed?.Invoke();
            };
            timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(750) };
            timer.Tick += (_, _) =>
            {
                // Native dialogs hold drafts and Hard Links has an eager saver.
                // Do not overwrite a dialog's in-progress values or save its list halfway.
                if (main.IsEnabled && Application.Current.Windows.Cast<Window>().All(w => w.GetType().FullName != "QSC.QSys.windows.Dialogs.PreferencesDialog"))
                    synchronizer.Poll();
            };
            timer.Start();
            main.Closed += (_, _) => { timer.Stop(); synchronizer.Dispose(); };
        }
        catch (Exception ex) { synchronizer?.Dispose(); synchronizer = null; startupError = ex.GetBaseException().Message; Host.Error("mods-core", ex); Changed?.Invoke(); }
    }
    public static void Open()
    {
        if (synchronizer is null) throw new InvalidOperationException(Error ?? "Live settings are unavailable.");
        synchronizer.Recreate();
        Process.Start(new ProcessStartInfo(PathName) { UseShellExecute = true });
    }
    public static void Retry() => synchronizer?.Retry();
}

internal sealed class SharedSettingsTarget : ISettingsTarget
{
    private readonly NativePreferences native = new();
    private SettingsStore? attemptedStore;
    private IReadOnlyDictionary<string, JsonElement>? storeBefore;
    private bool nativeAttempted, controlsAttempted, storeSaved;
    private readonly List<string> published = new();
    private static readonly JsonSerializerOptions Strict = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver(), UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };
    public JsonObject Capture()
    {
        var mods = new JsonObject();
        foreach (var pair in new SettingsStore(Preferences.PathName).Snapshot()) mods[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        foreach (var pair in Preferences.Settings) mods[pair.Key] = pair.Value.Read();
        mods[ModControls.SettingsId] = JsonSerializer.SerializeToNode(ModControls.Current, Strict);
        return new() { ["$schema"] = "./settings.schema.json", ["schemaVersion"] = 1, ["designerVersion"] = "10.5.0.0", ["qsys"] = native.Capture(), ["mods"] = mods };
    }
    private Dictionary<string, object> Prepare(JsonObject document)
    {
        native.Validate((JsonObject)document["qsys"]!);
        var result = new Dictionary<string, object>();
        foreach (var pair in (JsonObject)document["mods"]!)
        {
            SettingsStore.ValidateId(pair.Key);
            if (pair.Value is not JsonObject value) throw new ArgumentException("Mod preferences must be objects: " + pair.Key);
            if (pair.Key == ModControls.SettingsId)
            {
                var controls = value.Deserialize<ControlSettings>(Strict) ?? throw new ArgumentException("Missing shared controls.");
                ModControls.Validate(controls); result.Add(pair.Key, controls);
            }
            else result.Add(pair.Key, Preferences.Settings.TryGetValue(pair.Key, out var setting) ? setting.Validate(value) : JsonSerializer.SerializeToElement(value));
        }
        return result;
    }
    public void Validate(JsonObject document)
    {
        var prepared = Prepare(document);
        // Populate optional typed defaults before the application/readback check.
        // Unknown sections remain unchanged and inert.
        foreach (var pair in prepared.Where(p => p.Key == ModControls.SettingsId || Preferences.Settings.ContainsKey(p.Key)))
            document["mods"]![pair.Key] = JsonSerializer.SerializeToNode(pair.Value, pair.Value.GetType(), Strict);
    }
    public void Apply(JsonObject document)
    {
        nativeAttempted = controlsAttempted = storeSaved = false; published.Clear();
        var prepared = Prepare(document);
        var store = new SettingsStore(Preferences.PathName); // stale-save guard spans native application
        attemptedStore = store; storeBefore = store.Snapshot();
        var before = Capture();
        nativeAttempted = true;
        native.Apply((JsonObject)document["qsys"]!);
        var controls = (ControlSettings)prepared[ModControls.SettingsId];
        if (!SettingsJson.Same(before["mods"]![ModControls.SettingsId], document["mods"]![ModControls.SettingsId])) { controlsAttempted = true; ModControls.Publish(controls); }
        if (!SettingsJson.Same(before["mods"], document["mods"])) { store.Save(prepared); storeSaved = true; }
        foreach (var pair in Preferences.Settings)
            if (!SettingsJson.Same(before["mods"]![pair.Key], document["mods"]![pair.Key])) { published.Add(pair.Key); pair.Value.Publish(prepared[pair.Key]); }
    }
    public void Restore(JsonObject document)
    {
        var errors = new List<Exception>();
        void Recover(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        if (nativeAttempted) Recover(native.Restore);
        if (controlsAttempted) Recover(() => ModControls.Publish(document["mods"]![ModControls.SettingsId]!.Deserialize<ControlSettings>(Strict)!));
        // A failed stale save has written nothing: never roll it back over a
        // different process's newer preferences. Successful writes use that same
        // store's optimistic guard during recovery, including removal of new keys.
        if (storeSaved) Recover(() => attemptedStore!.Restore(storeBefore!));
        foreach (var id in published) Recover(() => Preferences.Settings[id].Publish(Preferences.Settings[id].Validate(document["mods"]![id]!)));
        if (errors.Count > 0) throw new AggregateException("Some settings could not be restored; newer files were preserved.", errors);
    }
    public JsonObject Schema()
    {
        var mods = new JsonObject();
        foreach (var pair in Preferences.Settings) mods[pair.Key] = EmbedSchema(pair.Value.Schema, pair.Key);
        mods[ModControls.SettingsId] = EmbedSchema(Strict.GetJsonSchemaAsNode(typeof(ControlSettings)), ModControls.SettingsId);
        return new() { ["$schema"] = "https://json-schema.org/draft/2020-12/schema", ["title"] = "Q-SYS and Mods Preferences", ["type"] = "object",
            ["additionalProperties"] = false, ["required"] = new JsonArray("schemaVersion", "designerVersion", "qsys", "mods"), ["properties"] = new JsonObject
            {
                ["$schema"] = new JsonObject { ["type"] = "string" }, ["schemaVersion"] = new JsonObject { ["const"] = 1 },
                ["designerVersion"] = new JsonObject { ["const"] = "10.5.0.0" }, ["qsys"] = native.Schema(),
                ["mods"] = new JsonObject { ["type"] = "object", ["propertyNames"] = new JsonObject { ["pattern"] = "^[a-z][a-z0-9-]{1,63}$" }, ["properties"] = mods,
                    ["additionalProperties"] = new JsonObject { ["type"] = "object", ["description"] = "Preferences retained for a mod which is not currently loaded." } }
            } };
    }
    private static JsonNode EmbedSchema(JsonNode schema, string id)
    {
        var result = schema.DeepClone();
        void Visit(JsonNode? node)
        {
            if (node is JsonObject value)
            {
                if (value["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var text) && text.StartsWith('#'))
                    value["$ref"] = "#/properties/mods/properties/" + id + text[1..];
                foreach (var pair in value) Visit(pair.Value);
            }
            else if (node is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(result); return result;
    }
}
