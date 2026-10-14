using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using QsysMods.Core;

static class Program
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static void Reject(Action action, string name) { try { action(); } catch { checks++; return; } throw new Exception(name); }
    [STAThread] static void Main()
    {
        Host.SettingsDirectory = Path.Combine(Path.GetTempPath(), "qsys-settings-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Host.SettingsDirectory);
        var window = new Window(); NameScope.SetNameScope(window, new NameScope());
        var menu = new Menu(); var tools = new MenuItem { Header = "Tools" }; menu.Items.Add(tools); window.Content = menu;
        window.RegisterName("mainMenu", menu); window.RegisterName("mainMenu_Tools", tools);
        Commands.Initialize(window); ModControls.Initialize(window);
        var settings = Preferences.Register("example-mod", "Example", () => new Example(20, "keep"), value => new(new Grid(), () => value),
            value => { if (value.Gap < 0) throw new ArgumentException("Gap cannot be negative"); });
        Preferences.Register("optional-mod", "Optional", () => new Optional(), value => new(new Grid(), () => value));
        int notifications = 0; settings.Changed += () => notifications++;
        new SettingsStore(Preferences.PathName).Save(new Dictionary<string, object> { ["missing-mod"] = new { Preserve = 42 } });
        var target = new SharedSettingsTarget(); var initial = target.Capture();
        Check(!initial.ToJsonString().Contains("fixture-secret") && !initial.ToJsonString().Contains("fixture-user"), "Native credentials are not exposed");
        Check(((JsonObject)initial["qsys"]!["general"]!).Count == 20, "Visible General preferences are represented");
        target.Validate(initial); Check(FakeGeneral.Saves == 0 && notifications == 0, "Validation has no native or mod side effects");
        var optional = (JsonObject)initial.DeepClone(); optional["mods"]!["optional-mod"] = new JsonObject { ["Gap"] = 7 };
        target.Validate(optional); Check(optional["mods"]!["optional-mod"]!["Enabled"]!.GetValue<bool>(), "Optional typed defaults normalize before readback");
        var edit = (JsonObject)initial.DeepClone(); edit["qsys"]!["general"]!["Theme"] = "Dark"; edit["mods"]!["example-mod"]!["Gap"] = 33;
        target.Validate(edit); target.Apply(edit);
        Check(FakeGeneral.Saved.Theme == Choice.Dark && settings.Value.Gap == 33 && notifications == 1, "One apply updates both native and registered mod settings");
        Check(new SettingsStore(Preferences.PathName).Read("example-mod", () => new Example(0, "" )).Gap == 33, "Mod values persist in the existing store");
        Check(new SettingsStore(Preferences.PathName).Contains("missing-mod"), "Absent-mod values survive apply");
        target.Apply(edit); Check(notifications == 1 && FakeGeneral.Saves == 1, "Unchanged settings do not republish or resave");
        void Bad(string key, JsonNode? value)
        { var bad = (JsonObject)edit.DeepClone(); ((JsonObject)bad["qsys"]!["general"]!)[key] = value; Reject(() => target.Validate(bad), "Reject invalid native " + key); }
        Bad("Theme", JsonValue.Create("Bad")); Bad("Theme", JsonValue.Create(1)); Bad("Theme", null);
        Bad("PeriodicHoldoffMinutes", JsonValue.Create(-1)); Bad("PeriodicHoldoffMinutes", JsonValue.Create(2000000000));
        Bad("AudioBuffer", JsonValue.Create(-1)); Bad("TraceColorAudio", JsonValue.Create("blue"));
        Bad("TraceWireStyle", JsonValue.Create("unknown")); Bad("ProxyAddress", JsonValue.Create("invalid")); Bad("ProxyPassword", JsonValue.Create("not-exported"));
        var badMod = (JsonObject)edit.DeepClone(); badMod["mods"]!["example-mod"]!["Gap"] = -1;
        Reject(() => target.Validate(badMod), "Registered mod validator runs");
        badMod["mods"]!["example-mod"]!["Gap"] = 2; badMod["mods"]!["example-mod"]!["Typo"] = true;
        Reject(() => target.Validate(badMod), "Typed mod field typos are rejected");
        var invalidKeys = (JsonObject)edit.DeepClone(); invalidKeys["mods"]![ModControls.SettingsId]!["Bindings"]!["Overrides"]!["missing/action"] = "invalid key";
        Reject(() => target.Validate(invalidKeys), "Shared shortcuts use Core validation");
        Check(FakeGeneral.Saved.Theme == Choice.Dark && settings.Value.Gap == 33, "Invalid candidates leave applied values intact");
        edit["qsys"]!["hardLinks"] = new JsonArray(new JsonObject { ["Name"] = "Test", ["IpAddress"] = "192.0.2.1", ["IsEnabled"] = true });
        target.Validate(edit); target.Apply(edit);
        Check(FakeLinks.Links.Count == 1 && File.Exists(FakeLinks.JsonFileName), "Hard Links apply through native manager and persist");
        var duplicates = (JsonObject)edit.DeepClone(); ((JsonArray)duplicates["qsys"]!["hardLinks"]!).Add(duplicates["qsys"]!["hardLinks"]![0]!.DeepClone());
        Reject(() => target.Validate(duplicates), "Duplicate hard links rejected");
        FakeLinks.Fail = true; edit["qsys"]!["hardLinks"]![0]!["Name"] = "Changed";
        Reject(() => target.Apply(edit), "Native swallowed save error is detected by persisted readback"); FakeLinks.Fail = false;
        target.Restore(initial); // Restore the last attempt's actual native snapshot.
        Check(FakeLinks.Links[0].Name == "Test", "Native rollback retains the prior links");
        var latest = target.Capture();
        var concurrent = (JsonObject)latest.DeepClone(); concurrent["qsys"]!["general"]!["Theme"] = "Light";
        concurrent["mods"]!["example-mod"]!["Gap"] = 66;
        FakeGeneral.OnSave = () => new SettingsStore(Preferences.PathName).Save(new Dictionary<string, object> { ["outside-mod"] = new { Unique = 99 } });
        Reject(() => target.Apply(concurrent), "Concurrent legacy-store edit rejects the stale commit");
        target.Restore(latest);
        Check(FakeGeneral.Saved.Theme == Choice.Dark && settings.Value.Gap == 33, "Stale commit restores only the state this attempt touched");
        Check(new SettingsStore(Preferences.PathName).Contains("outside-mod"), "Rollback preserves a different process's newer section");
        latest = target.Capture();
        var added = (JsonObject)latest.DeepClone(); added["mods"]!["new-absent-mod"] = new JsonObject { ["Value"] = 1 };
        target.Apply(added); target.Restore(latest);
        Check(!new SettingsStore(Preferences.PathName).Contains("new-absent-mod"), "Rollback removes newly added sections from its own successful write");
        var schema = target.Schema();
        Check(schema["properties"]!["qsys"]!["properties"]!["general"]!["properties"]!["Theme"]!["enum"] is JsonArray, "Native enum choices appear in editor schema");
        Check(schema["properties"]!["mods"]!["properties"]!["example-mod"] is JsonObject, "Registered mod type contributes editor schema");
        void VerifyRefs(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                if (obj["$ref"] is JsonValue reference)
                {
                    JsonNode? found = schema;
                    foreach (string part in reference.GetValue<string>()[2..].Split('/')) found = found?[part.Replace("~1", "/").Replace("~0", "~")];
                    Check(found is not null, "Embedded schema references resolve at the shared root");
                }
                foreach (var pair in obj) VerifyRefs(pair.Value);
            }
            else if (node is JsonArray array) foreach (var item in array) VerifyRefs(item);
        }
        VerifyRefs(schema);
        Console.WriteLine($"Passed {checks} settings bridge checks using fake native persistence. No Designer process was started.");
    }
    public sealed record Example(int Gap, string Label);
    public sealed record Optional(int Gap = 20, bool Enabled = true);
}
