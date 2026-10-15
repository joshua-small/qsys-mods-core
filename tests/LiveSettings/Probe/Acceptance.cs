using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HarmonyLib;
using QsysMods.Core;

namespace Designer.Modules.SettingsLab;

public sealed partial class Probe
{
    private static void AcceptanceReceipt(string mode, bool success, Exception? error = null) =>
        File.WriteAllText(Path.Combine(Output, $"acceptance-{Environment.ProcessId}.json"),
            JsonSerializer.Serialize(new { mode, pid = Environment.ProcessId, success, checks,
                error = error?.ToString(), settingsError = SharedSettings.Error }, SettingsJson.Format));

    private static object Synchronizer() => typeof(SharedSettings).GetField("synchronizer", Host.Static)!.GetValue(null)!;
    private static async Task Accepted(Action<JsonObject> change, string name)
    {
        Edit(change);
        var desired = Read();
        await Until(() => SharedSettings.Error is not null || SettingsJson.Same(Good(), desired));
        Check(SharedSettings.Error is null && SettingsJson.Same(Good(), desired), name + ": " + SharedSettings.Error);
        File.WriteAllText(Path.Combine(Output, "acceptance-progress.json"), JsonSerializer.Serialize(new { checks }, SettingsJson.Format));
    }
    private static async Task Acceptance(Window main, string mode)
    {
        Window? prefs = null;
        try
        {
            if (mode == "competing")
            {
                await Task.Delay(2000);
                Check(SharedSettings.Error?.Contains("session.lock") == true, "Second Designer refuses the active JSON lease");
                Check(Synchronizer() is null, "Second Designer has no active settings synchronizer");
                AcceptanceReceipt(mode, true); return;
            }
            Check(SharedSettings.Error is null, "Live settings starts without error");
            await Task.Delay(2200);
            Check(SettingsJson.Same(Read(), Good()), "Initial file and last-good agree");
            if (mode == "retained")
            {
                Check(SettingsJson.Same(Read(), ((ISettingsTarget)Host.Field(Synchronizer(), "target")).Capture()), "Restart retains every native and mod value");
                StartContextReceipt(main);
                AcceptanceReceipt(mode, true); return;
            }
            if (mode == "audio") { await AudioAcceptance(); AcceptanceReceipt(mode, true); return; }
            if (mode == "recovery") { await RecoveryAcceptance(); AcceptanceReceipt(mode, true); return; }
            if (mode == "contexts") { await ContextAcceptance(main); AcceptanceReceipt(mode, true); return; }
            var original = (JsonObject)Read().DeepClone();
            var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(Host.SettingsDirectory, "settings.schema.json")))!;
            var definitions = (JsonObject)schema["properties"]!["qsys"]!["properties"]!["general"]!["properties"]!;
            var model = GeneralModel();
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "profile", "Assets"));
            foreach (var pair in definitions)
            {
                string key = pair.Key;
                var old = original["qsys"]!["general"]![key]!;
                JsonNode next;
                if (pair.Value!["enum"] is JsonArray choices)
                    next = choices.First(c => !SettingsJson.Same(c, old))!.DeepClone();
                else if (pair.Value["type"]!.GetValue<string>() == "boolean") next = JsonValue.Create(!old.GetValue<bool>())!;
                else if (key == "PeriodicHoldoffMinutes") next = JsonValue.Create(17)!;
                else if (key == "AudioBuffer") next = JsonValue.Create(47)!;
                else if (key.StartsWith("TraceColor")) next = JsonValue.Create("#FF234567")!;
                else if (key == "ProxyAddress") next = JsonValue.Create("http://127.0.0.1:9")!;
                else if (key == "AssetDirectory") next = JsonValue.Create(Path.Combine(AppContext.BaseDirectory, "profile", "Assets"))!;
                else if (key == "TraceWireStyle") next = JsonValue.Create(TypeDescriptor.GetProperties(model)[key]!.Converter.GetStandardValues()!.Cast<string>().First(v => v != old.GetValue<string>()))!;
                else throw new InvalidOperationException("Uncovered General field " + key);
                await Accepted(doc => doc["qsys"]!["general"]![key] = next.DeepClone(), "JSON applies General." + key);
                prefs = OpenPreferences(); await Task.Delay(100);
                var native = Host.Property(Host.Field(prefs, "_General"), "Model");
                var property = native.GetType().GetProperty(key)!;
                object restored = property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, old.GetValue<string>())
                    : property.PropertyType == typeof(System.Windows.Media.Color) ? System.Windows.Media.ColorConverter.ConvertFromString(old.GetValue<string>())!
                    : old.Deserialize(property.PropertyType)!;
                property.SetValue(native, restored); Save(prefs); prefs = null;
                await Until(() => SharedSettings.Error is not null || SettingsJson.Same(Read()["qsys"]!["general"]![key], old));
                Check(SharedSettings.Error is null && SettingsJson.Same(Good()["qsys"]!["general"]![key], old), "Native OK projects General." + key);
            }
            Check(definitions.Count == 20, "All 20 General fields exercised in both directions");
            // Hold native drafts and the edited file at the same time. Neither side
            // may silently win when Preferences closes; explicit Retry acknowledges it.
            prefs = OpenPreferences(); await Task.Delay(200);
            var draft = Host.Property(Host.Field(prefs, "_General"), "Model");
            draft.GetType().GetProperty("AudioBuffer")!.SetValue(draft, 53);
            Edit(doc => doc["qsys"]!["general"]!["AudioBuffer"] = 59);
            string candidate = File.ReadAllText(SharedSettings.PathName);
            Save(prefs); prefs = null;
            await Until(() => SharedSettings.Error is not null);
            Check(SharedSettings.Error!.Contains("both changed"), "Simultaneous native/file edits are rejected");
            Check((int)Host.Property(GeneralModel(), "AudioBuffer") == 53 && File.ReadAllText(SharedSettings.PathName) == candidate, "Both competing native/file values are preserved");
            await Task.Delay(1700);
            Check(SharedSettings.Error is not null, "Conflict remains visible until explicit acknowledgment");
            SharedSettings.Retry();
            await Until(() => SharedSettings.Error is null && (int)Host.Property(GeneralModel(), "AudioBuffer") == 59);
            Check(SettingsJson.Same(Read(), Good()), "Explicit Retry applies reconciled file and updates recovery");
            // Missing-file recovery uses the running native state, without resetting it.
            string missing = SharedSettings.PathName + ".acceptance-retained";
            File.Move(SharedSettings.PathName, missing, true);
            await Until(() => SharedSettings.Error is not null);
            Host.Call(Synchronizer(), "Recreate");
            Check(SettingsJson.Same(Read(), Good()) && (int)Host.Property(GeneralModel(), "AudioBuffer") == 59, "Recreate preserves native state and repairs the missing file");
            AcceptanceReceipt(mode, true);
        }
        catch (Exception ex) { prefs?.Close(); AcceptanceReceipt(mode, false, ex); }
    }

    private static DispatcherTimer? contextTimer;
    private static async Task ContextAcceptance(Window main)
    {
        // Routed-event integration in the actual host, separate from physical input.
        int ordinary = 0, optedIn = 0;
        foreach (var (id, binding, allow) in new[] { ("single", "F19", false), ("chord", "F20 > G", false),
            ("opt-single", "F21", true), ("opt-chord", "F22 > G", true) })
            Commands.Register(new("settings-lab/context-" + id, id, "Context acceptance",
                () => { if (allow) optedIn++; else ordinary++; }, () => true, AllowInText: allow) { DefaultBinding = binding });
        void Stroke(Window target, Key key)
        {
            var source = PresentationSource.FromVisual(target)!;
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
        }
        void All(Window target) { foreach (var key in new[] { Key.F19, Key.F20, Key.G, Key.F21, Key.F22, Key.G }) Stroke(target, key); }
        main.Activate(); main.Focus(); Keyboard.Focus(main); await Task.Delay(200);
        Check(!ControlInput.IsTextInput() && Keyboard.Modifiers == ModifierKeys.None, "Context fixture has neutral keyboard state");
        All(main); Check(ordinary == 2 && optedIn == 2, "Main window routes single keys and chords once");
        var text = Visual(main).OfType<TextBox>().First(t => t.IsVisible && t.IsEnabled && !t.IsReadOnly);
        text.Focus(); await Task.Delay(100);
        Check(ControlInput.IsTextInput(), "Native text input is recognized");
        All(main); Check(ordinary == 2 && optedIn == 4, "Native text input blocks defaults and permits both opted-in forms");
        main.Focus(); Keyboard.Focus(main);
        Commands.Native("NamedComponentInfo").Execute(null, main); await Task.Delay(300);
        var component = Application.Current.Windows.Cast<Window>().SelectMany(Visual)
            .First(e => e.GetType().FullName == "QSC.QSys.windows.ComponentInfo");
        var floating = Window.GetWindow(component)!;
        Check(floating != main && floating.IsVisible, "Native Component Controls has its own window route");
        All(floating); Check(ordinary == 2 && optedIn == 6, $"Floating Component Controls blocks defaults and permits both opted-in forms (ordinary={ordinary}, optedIn={optedIn})");
        var prefs = (Window)Activator.CreateInstance(Host.Type("QSC.QSys.windows.Dialogs.PreferencesDialog"))!;
        prefs.Owner = main;
        Exception? modalError = null;
        _ = main.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                // ShowDialog disables owner HWNDs, not their WPF IsEnabled
                // property. Input belongs to the dialog's route while it is open.
                Check(prefs.IsVisible && prefs.Owner == main, "Native Preferences is open as an owned modal dialog");
                All(prefs);
                Check(ordinary == 2 && optedIn == 6, "Modal Preferences does not dispatch main or floating shortcuts");
            }
            catch (Exception ex) { modalError = ex; }
            finally { prefs.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        prefs.ShowDialog();
        if (modalError is not null) throw modalError;
        floating.Close();
    }
    private static IEnumerable<DependencyObject> Visual(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visual(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void StartContextReceipt(Window main)
    {
        contextTimer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(250) };
        contextTimer.Tick += (_, _) =>
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            var ancestors = new List<string>();
            for (var item = focused; item is not null; item = ControlInput.Parent(item)) ancestors.Add(item.GetType().FullName!);
            File.WriteAllText(Path.Combine(Output, "context.json"), JsonSerializer.Serialize(new {
                pid = Environment.ProcessId, runs, textInput = ControlInput.IsTextInput(), ancestors,
                mainEnabled = main.IsEnabled, error = SharedSettings.Error,
                windows = Application.Current.Windows.Cast<Window>().Select(w => new { w.Title, type = w.GetType().FullName, w.IsActive, w.IsEnabled })
            }, SettingsJson.Format));
        };
        contextTimer.Start();
    }

    private static async Task AudioAcceptance()
    {
        var before = Read()["qsys"]!["general"]!.DeepClone();
        var type = Host.Type("QSC.QSys.Audio.Monitor");
        var monitor = type.GetProperty("Current", Host.Static)!.GetValue(null)!;
        try
        {
            foreach (var api in new[] { "WASAPI", "DirectSound", "WavOut" })
            foreach (var quality in new[] { "Default128kbps", "Legacy64kbps" })
            {
                await Accepted(doc => { var general = doc["qsys"]!["general"]!; general["AudioApi"] = api; general["AudioQuality"] = quality; general["AudioBuffer"] = 47; }, "Audio settings " + api + "/" + quality);
                // No Core connection or audio payload: this exercises the native
                // receiver/decoder and Windows output initialization with silence.
                Host.Call(monitor, "Start"); await Task.Delay(200);
                var player = type.GetField("_DefaultPlayer", Host.Instance)!.GetValue(monitor);
                Check(player is not null && Host.Property(player, "PlaybackState").ToString() == "Playing"
                    && type.GetField("_Decoder", Host.Instance)!.GetValue(monitor) is not null,
                    "Native silent output starts " + api + "/" + quality);
                Host.Call(monitor, "Stop");
                Check(type.GetField("_DefaultPlayer", Host.Instance)!.GetValue(monitor) is null && type.GetField("_Decoder", Host.Instance)!.GetValue(monitor) is null,
                    "Native output and decoder stop " + api + "/" + quality);
            }
        }
        finally { Host.Call(monitor, "Stop"); }
        await Accepted(doc => doc["qsys"]!["general"] = before.DeepClone(), "Audio settings restored");
    }

    private static FileStream? recoveryLock;
    private static bool lockNextRecovery;
    public static void AfterRecoveryWrite(string path)
    {
        if (!lockNextRecovery || !path.EndsWith("settings.last-good.json", StringComparison.Ordinal)) return;
        lockNextRecovery = false;
        recoveryLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static async Task RecoveryAcceptance()
    {
        var harmony = new Harmony("qsys.settings.lab.recovery-fault");
        var write = typeof(SettingsJson).GetMethod(nameof(SettingsJson.Write))!;
        harmony.Patch(write, postfix: new HarmonyMethod(typeof(Probe), nameof(AfterRecoveryWrite)));
        string goodPath = Path.Combine(Host.SettingsDirectory, "settings.last-good.json");
        string good = File.ReadAllText(goodPath);
        int before = (int)Host.Property(GeneralModel(), "AudioBuffer");
        try
        {
            lockNextRecovery = true;
            Edit(doc => doc["qsys"]!["general"]!["AudioBuffer"] = before + 1);
            string failed = File.ReadAllText(SharedSettings.PathName);
            await Until(() => SharedSettings.Error is not null);
            Check((bool)Host.Property(Synchronizer(), "Faulted"), "A failed recovery write stops live synchronization");
            Check(File.ReadAllText(goodPath) == good && File.ReadAllText(SharedSettings.PathName) == failed, "Failed recovery preserves last-good and edited file");
            string error = SharedSettings.Error!;
            File.Move(SharedSettings.PathName, SharedSettings.PathName + ".failed-acceptance", true);
            bool refused = false;
            try { Host.Call(Synchronizer(), "Recreate"); } catch { refused = true; }
            Check(refused && !File.Exists(SharedSettings.PathName) && File.ReadAllText(goodPath) == good && SharedSettings.Error == error,
                "Faulted missing-file reopen preserves recovery and visible error");
            recoveryLock?.Dispose(); recoveryLock = null;
            File.WriteAllText(SharedSettings.PathName, good);
            await Task.Delay(1700);
            Check((bool)Host.Property(Synchronizer(), "Faulted") && SharedSettings.Error == error, "Restoring the last-good file cannot silently resume a faulted session");
        }
        finally { recoveryLock?.Dispose(); recoveryLock = null; lockNextRecovery = false; harmony.Unpatch(write, HarmonyPatchType.All, harmony.Id); }
    }
}
