using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using QsysMods.Core;

namespace Designer.Modules.SettingsLab;

public sealed class Probe : ModModule
{
    private static readonly string Output = Path.Combine(AppContext.BaseDirectory, "live-evidence");
    private static readonly List<string> checks = [];
    private static readonly Dictionary<string, int> runs = new();
    private DispatcherTimer? timer;
    private int attempts;
    public Probe()
    {
        if (!Host.IsLab || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".settings-live-lab.json"))) return;
        Mods.Register("settings-lab", main =>
        {
            foreach (var id in new[] { "first", "second" }) Register(id, new(Key.F9, ModifierKeys.Control | ModifierKeys.Alt));
            Register("native", new(Key.S, ModifierKeys.Control));
            Register("prefix", null, "Ctrl+Alt+F10"); Register("chord", null, "Ctrl+Alt+F10 > P");
            Register("physical", null, "Ctrl+Alt+F11 > P");
            Register("menu", new(Key.F, ModifierKeys.Alt));
            timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += async (_, _) =>
            {
                if (++attempts < 10 || !main.IsEnabled || !File.Exists(SharedSettings.PathName)) return;
                timer.Stop(); Directory.CreateDirectory(Output);
                if (File.Exists(Path.Combine(Output, "result.json")))
                {
                    await Task.Delay(2500); Dump();
                    var model = GeneralModel();
                    var expected = Read();
                    var actual = new { Theme = Host.Property(model, "Theme").ToString(), AudioBuffer = (int)Host.Property(model, "AudioBuffer"),
                        TraceColorAudio = Host.Property(model, "TraceColorAudio").ToString(), Gap = new SettingsStore(Preferences.PathName).Read("better-packing", () => new Packing()).Gap };
                    bool success = SharedSettings.Error is null && actual.Theme == expected["qsys"]!["general"]!["Theme"]!.GetValue<string>()
                        && actual.AudioBuffer == expected["qsys"]!["general"]!["AudioBuffer"]!.GetValue<int>()
                        && actual.TraceColorAudio == expected["qsys"]!["general"]!["TraceColorAudio"]!.GetValue<string>()
                        && actual.Gap == expected["mods"]!["better-packing"]!["Gap"]!.GetValue<int>();
                    File.WriteAllText(Path.Combine(Output, "restart.json"), JsonSerializer.Serialize(new { success, actual, shortcutsLoaded = Mods.IsReady("better-shortcuts"), error = SharedSettings.Error }, new JsonSerializerOptions { WriteIndented = true }));
                    return;
                }
                await Run(main);
            };
            timer.Start();
        });
    }
    private static void Register(string id, KeyGesture? gesture, string? binding = null) => Commands.Register(new("settings-lab/" + id, "Lab " + id, "Settings validation", () =>
    { runs[id] = runs.GetValueOrDefault(id) + 1; Dump(); }, () => true, gesture, InMenu: true) { DefaultBinding = binding });
    private static JsonObject Read() => SettingsJson.Parse(File.ReadAllText(SharedSettings.PathName));
    private static JsonNode Good() => JsonNode.Parse(File.ReadAllText(Path.Combine(Host.SettingsDirectory, "settings.last-good.json")))!;
    private static void Edit(Action<JsonObject> change) { var doc = Read(); change(doc); File.WriteAllText(SharedSettings.PathName, SettingsJson.Text(doc)); }
    private static async Task Until(Func<bool> ready)
    { for (int i = 0; i < 60; i++) { await Task.Delay(250); if (ready()) return; } throw new TimeoutException("Live settings timeout: " + SharedSettings.Error); }
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
    private static object GeneralModel() => Host.Property(Activator.CreateInstance(Host.Type("QSC.QSys.windows.Dialogs.GeneralSettings"))!, "Model");
    private static Window OpenPreferences()
    { var prefs = (Window)Activator.CreateInstance(Host.Type("QSC.QSys.windows.Dialogs.PreferencesDialog"))!; prefs.Owner = Application.Current.MainWindow; prefs.Show(); return prefs; }
    private static void Save(Window prefs) => Preferences.Logical(prefs).OfType<Button>().Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static object? SyncState()
    {
        var sync = typeof(SharedSettings).GetField("synchronizer", Host.Static)!.GetValue(null);
        return sync is null ? null : new { baseline = Host.Field(sync, "baseline"), live = ((ISettingsTarget)Host.Field(sync, "target")).Capture() };
    }
    private static void Dump() => File.WriteAllText(Path.Combine(Output, "state.json"), JsonSerializer.Serialize(new {
        error = SharedSettings.Error, runs,
        sync = SyncState(),
        conflicts = ModControls.Bindings.DefaultConflicts(ModControls.Current.Bindings.Overrides),
        bindings = Commands.All.Where(c => c.Definition.Id.StartsWith("settings-lab/")).ToDictionary(c => c.Definition.Id, c => ModControls.Bindings.Display(c.Definition.Id, ModControls.Current.Bindings.Overrides))
    }, new JsonSerializerOptions { WriteIndented = true }));
    private static async Task Run(Window main)
    {
        Window? prefs = null;
        try
        {
            Check(main.Title.StartsWith("untitled", StringComparison.OrdinalIgnoreCase), "Blank disposable design");
            var nativePath = (string)Host.Type("QSC.Common.SettingsManager").GetProperty("Path", Host.Static)!.GetValue(null)!;
            Check(nativePath.StartsWith(Path.Combine(AppContext.BaseDirectory, "profile"), StringComparison.OrdinalIgnoreCase), "Native preferences path is isolated");
            Check(SharedSettings.Error is null, "Shared settings initializes in Designer");
            var conflicts = ModControls.Bindings.DefaultConflicts(new());
            Check(new[] { "first", "second", "native", "prefix", "chord" }.All(id => conflicts.Any(c => c.CommandId == "settings-lab/" + id)), "Duplicate, native and chord-prefix defaults stay inactive");
            Check(ModControls.Bindings.Display("settings-lab/physical", new()) == "Ctrl+Alt+F11 > P", "Nonconflicting chord default remains active");
            Edit(doc => { doc["qsys"]!["general"]!["Theme"] = "Light"; doc["mods"]!["better-packing"]!["Gap"] = 37;
                doc["qsys"]!["hardLinks"] = new JsonArray(new JsonObject { ["Name"] = "Disposable", ["IpAddress"] = "192.0.2.1", ["IsEnabled"] = false }); });
            await Until(() => Good()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 37);
            Check(Host.Property(GeneralModel(), "Theme").ToString() == "Light", "Live JSON updates native General preferences");
            Check(new SettingsStore(Preferences.PathName).Read("better-packing", () => new Packing()).Gap == 37, "Live JSON persists registered mod preferences");
            prefs = OpenPreferences(); await Task.Delay(500);
            var packing = Preferences.Logical(prefs).OfType<TabItem>().Single(t => Equals(t.Tag, "qsys-mod:better-packing"));
            Check(Preferences.Logical(packing).OfType<TextBox>().Single().Text == "37", "Native mod page displays live JSON value");
            Edit(doc => doc["mods"]!["better-packing"]!["Gap"] = 41);
            await Task.Delay(2200);
            Check(Good()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 37, "File application pauses while native Preferences is open");
            prefs.Close(); prefs = null;
            await Until(() => Good()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 41);
            Check(SharedSettings.Error is null, "File application resumes after Cancel");
            prefs = OpenPreferences(); await Task.Delay(300);
            packing = Preferences.Logical(prefs).OfType<TabItem>().Single(t => Equals(t.Tag, "qsys-mod:better-packing"));
            Preferences.Logical(packing).OfType<TextBox>().Single().Text = "43";
            Save(prefs); prefs = null;
            await Until(() => Read()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 43);
            Check(Good()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 43, "Native Preferences OK projects back to JSON");
            var accepted = File.ReadAllText(SharedSettings.PathName);
            File.WriteAllText(SharedSettings.PathName, "{ unfinished"); await Until(() => SharedSettings.Error is not null);
            Check(new SettingsStore(Preferences.PathName).Read("better-packing", () => new Packing()).Gap == 43, "Malformed JSON preserves live values");
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            Edit(doc => doc["mods"]!["better-packing"]!["Gap"] = -1); await Until(() => SharedSettings.Error is not null);
            Check(Good()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 43, "Invalid mod value preserves last-good recovery");
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            Edit(doc => doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"] = new JsonObject { ["settings-lab/first"] = "Ctrl+Alt+F9", ["settings-lab/second"] = "Ctrl+Alt+F9" });
            await Until(() => SharedSettings.Error is not null);
            Check(ModControls.Current.Bindings.Overrides.Count == 0, "Duplicate explicit JSON assignments are rejected together");
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            Edit(doc => doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"] = new JsonObject { ["settings-lab/first"] = "Ctrl+Alt+F9", ["settings-lab/second"] = "Ctrl+Alt+F9 > P" });
            await Until(() => SharedSettings.Error is not null);
            Check(ModControls.Current.Bindings.Overrides.Count == 0, "Explicit chord-prefix JSON conflict is rejected");
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            Edit(doc => doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"] = new JsonObject { ["settings-lab/menu"] = "Alt+F" });
            await Until(() => SharedSettings.Error is not null);
            Check(SharedSettings.Error!.Contains("EditorOverrides") && ModControls.Current.Bindings.Overrides.Count == 0, "JSON menu takeover requires matching overwrite consent");
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            var menuReset = ModControls.Bindings.Prepare("settings-lab/menu", null, new());
            Check(menuReset.Overrides.GetValueOrDefault("settings-lab/menu") == "Alt+F" && menuReset.EditorOverrides.GetValueOrDefault("settings-lab/menu") == "Alt+F", "Reviewed native-menu reset records explicit assignment and consent");
            Edit(doc => { doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"] = JsonSerializer.SerializeToNode(menuReset.Overrides);
                doc["mods"]![ModControls.SettingsId]!["Bindings"]!["EditorOverrides"] = JsonSerializer.SerializeToNode(menuReset.EditorOverrides); });
            await Until(() => ModControls.Current.Bindings.Overrides.GetValueOrDefault("settings-lab/menu") == "Alt+F");
            Check(SharedSettings.Error is null && ModControls.Bindings.Display("settings-lab/menu", ModControls.Current.Bindings.Overrides) == "Alt+F", "Reviewed native-menu reset applies through live JSON");
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => ModControls.Current.Bindings.Overrides.Count == 0 && SharedSettings.Error is null);
            Edit(doc => doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"] = new JsonObject { ["settings-lab/first"] = "Ctrl+Alt+F9" });
            await Until(() => ModControls.Current.Bindings.Overrides.ContainsKey("settings-lab/first"));
            Check(ModControls.Bindings.Display("settings-lab/second", ModControls.Current.Bindings.Overrides) == "", "Explicit user choice wins over competing default");
            var plan = ModControls.Bindings.Prepare("settings-lab/second", "Ctrl+Alt+F9", ModControls.Current.Bindings.Overrides);
            Check(plan.Conflicts.Any(n => n.Contains("Lab first")) && plan.Overrides["settings-lab/first"] == "", "Shared conflict plan identifies and clears prior owner");
            Edit(doc => doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"] = JsonSerializer.SerializeToNode(plan.Overrides));
            await Until(() => ModControls.Current.Bindings.Overrides.GetValueOrDefault("settings-lab/first") == "");
            Check(ModControls.Bindings.Display("settings-lab/second", ModControls.Current.Bindings.Overrides) == "Ctrl+Alt+F9", "Conflict transfer applies live in Designer");
            Edit(doc => doc["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"]!["settings-lab/physical"] = "Ctrl+Alt+F12 > P");
            await Until(() => ModControls.Current.Bindings.Overrides.ContainsKey("settings-lab/physical"));
            Check(ModControls.Bindings.Display("settings-lab/physical", ModControls.Current.Bindings.Overrides) == "Ctrl+Alt+F12 > P", "Chord remapping applies from live settings");
            accepted = File.ReadAllText(SharedSettings.PathName);
            int buffer = (int)Host.Property(GeneralModel(), "AudioBuffer");
            using (var locked = new FileStream(Path.Combine(nativePath, "Prefs.xml"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Edit(doc => { doc["qsys"]!["general"]!["AudioBuffer"] = buffer + 1; doc["mods"]!["better-packing"]!["Gap"] = 77; });
                await Until(() => SharedSettings.Error is not null);
                Check(SharedSettings.Error!.Contains("restored") && (int)Host.Property(GeneralModel(), "AudioBuffer") == buffer, "Swallowed native save error is detected and rolled back");
                Check(Good()["mods"]!["better-packing"]!["Gap"]!.GetValue<int>() == 43, "Failed native save cannot commit mod values or recovery");
            }
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            var linkManager = Host.Type("QSC.QSys.Discovery.HardLinkManager");
            string namesPath = (string)linkManager.GetProperty("NamesFileName", Host.Static)!.GetValue(null)!;
            using (var locked = new FileStream(namesPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Edit(doc => doc["qsys"]!["hardLinks"]![0]!["Name"] = ""); await Until(() => SharedSettings.Error is not null);
                Check(SharedSettings.Error!.Contains("restored") && Good()["qsys"]!["hardLinks"]![0]!["Name"]!.GetValue<string>() == "Disposable", "Swallowed Hard Link names save error is detected and rolled back");
            }
            File.WriteAllText(SharedSettings.PathName, accepted); await Until(() => SharedSettings.Error is null);
            Edit(doc => doc["qsys"]!["hardLinks"]![0]!["Name"] = ""); await Until(() => Good()["qsys"]!["hardLinks"]![0]!["Name"]!.GetValue<string>() == "");
            linkManager.GetMethod("Load", Host.Static, null, Type.EmptyTypes, null)!.Invoke(null, null);
            Check(((System.Collections.IEnumerable)linkManager.GetProperty("Links", Host.Static)!.GetValue(null)!).Cast<object>().All(l => (string)Host.Property(l, "Name") == ""), "Cleared Hard Link names remain cleared after native reload");
            Edit(doc => { doc["qsys"]!["general"]!["AudioBuffer"] = buffer + 2; doc["qsys"]!["general"]!["Theme"] = "Dark"; doc["qsys"]!["general"]!["TraceColorAudio"] = "#FF112233"; });
            await Until(() => Good()["qsys"]!["general"]!["AudioBuffer"]!.GetValue<int>() == buffer + 2);
            Check(Host.Property(GeneralModel(), "Theme").ToString() == "Dark" && Host.Property(GeneralModel(), "TraceColorAudio").ToString() == "#FF112233", "Successful native theme, audio buffer and trace-color edits persist after recovery");
            prefs = OpenPreferences(); await Task.Delay(300);
            var model = Host.Property(Host.Field(prefs, "_General"), "Model");
            model.GetType().GetProperty("AudioBuffer")!.SetValue(model, buffer + 3);
            Save(prefs); prefs = null;
            await Until(() => Read()["qsys"]!["general"]!["AudioBuffer"]!.GetValue<int>() == buffer + 3);
            Check(Good()["qsys"]!["general"]!["AudioBuffer"]!.GetValue<int>() == buffer + 3, "Native General OK projects back into live JSON");
            prefs = OpenPreferences(); await Task.Delay(300);
            var shortcuts = Preferences.Logical(prefs).OfType<TabItem>().Single(t => Equals(t.Tag, "qsys-mod:better-shortcuts"));
            Preferences.Logical(prefs).OfType<TabControl>().Single().SelectedItem = shortcuts;
            await Task.Delay(300);
            var table = Preferences.Logical(shortcuts).OfType<DataGrid>().Single(t => t.Name == "ShortcutTable");
            void Select(string id) => table.SelectedItem = table.Items.Cast<object>().Single(row => (string)Host.Property(row, "Id") == "settings-lab/" + id);
            void Click(string label) => Preferences.Logical(shortcuts).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            object Recorder() => Preferences.Logical(shortcuts).OfType<FrameworkElement>().Single(e => e.Name == "ShortcutRecorder");
            void Capture(Key key, ModifierKeys modifiers) => Recorder().GetType().GetMethod("Capture", Host.Instance)!.Invoke(Recorder(), [key, modifiers, false]);
            Select("first"); Click("Change shortcut"); Capture(Key.F9, ModifierKeys.Control | ModifierKeys.Alt);
            Check(Preferences.Logical(shortcuts).OfType<TextBlock>().Any(t => t.Text.Contains("Lab second")) && Preferences.Logical(shortcuts).OfType<Button>().Any(b => Equals(b.Content, "Overwrite") && b.IsEnabled), "Better Shortcuts displays the shared conflict owner and Overwrite");
            Click("Cancel"); Check(ModControls.Current.Bindings.Overrides["settings-lab/first"] == "", "Cancel in the conflict editor preserves the live assignment");
            Click("Change shortcut"); Capture(Key.F9, ModifierKeys.Control | ModifierKeys.Alt); Host.Call(Recorder(), "Confirm");
            Check(ModControls.Current.Bindings.Overrides["settings-lab/first"] == "", "Confirmed overwrite remains staged until Preferences OK");
            Save(prefs); prefs = null; await Until(() => Read()["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"]!["settings-lab/first"]!.GetValue<string>() == "Ctrl+Alt+F9");
            Check(ModControls.Current.Bindings.Overrides["settings-lab/second"] == "", "Editor overwrite saves to Core and projects into settings JSON");
            prefs = OpenPreferences(); await Task.Delay(300);
            shortcuts = Preferences.Logical(prefs).OfType<TabItem>().Single(t => Equals(t.Tag, "qsys-mod:better-shortcuts"));
            Preferences.Logical(prefs).OfType<TabControl>().Single().SelectedItem = shortcuts; await Task.Delay(300);
            table = Preferences.Logical(shortcuts).OfType<DataGrid>().Single(t => t.Name == "ShortcutTable");
            Select("physical"); Click("Change shortcut"); Capture(Key.F11, ModifierKeys.Control | ModifierKeys.Alt);
            Host.Call(Host.Field(Recorder(), "recording"), "NextStroke"); Capture(Key.P, ModifierKeys.None);
            Check((string)Host.Property(Recorder(), "Gesture") == "Ctrl+Alt+F11 > P", "Native recorder captures a two-stroke candidate");
            Host.Call(Recorder(), "Confirm"); Save(prefs); prefs = null;
            await Until(() => Read()["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"]!["settings-lab/physical"]!.GetValue<string>() == "Ctrl+Alt+F11 > P");
            Check(Preferences.Logical(main).OfType<MenuItem>().Any(m => Equals(m.Header, "Lab physical") && m.InputGestureText == "Ctrl+Alt+F11 then P"), "Editor-saved chord updates the native menu hint and JSON");
            Dump(); File.WriteAllText(Path.Combine(Output, "result.json"), JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { prefs?.Close(); Dump(); File.WriteAllText(Path.Combine(Output, "result.json"), JsonSerializer.Serialize(new { success = false, checks, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true })); }
    }
    private sealed record Packing(int Gap = 20);
}
