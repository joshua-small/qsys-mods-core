using System.Text.Json.Nodes;
using QsysMods.Core;

int count = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
void Reject(Action action, string label) { try { action(); } catch { count++; return; } throw new Exception(label); }
string directory = Path.Combine(Path.GetTempPath(), "qsys-live-settings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "settings.json");
var target = new Fixture();
using (var sync = new SettingsSynchronizer(path, target))
{
    Check(File.Exists(path) && target.Applications == 0, "Initial projection never applies values");
    Reject(() => new SettingsSynchronizer(path, new Fixture()), "Only one Designer may own the projection");
    Check(File.Exists(Path.Combine(directory, "settings.last-good.json")), "Initial recovery snapshot exists");
    var edited = (JsonObject)target.Value.DeepClone(); edited["qsys"]!["general"]!["Theme"] = "Dark";
    File.WriteAllText(path, SettingsJson.Text(edited)); sync.Poll();
    Check(target.Applications == 0, "A single read does not apply an unfinished save");
    sync.Poll(); Check(target.Value["qsys"]!["general"]!["Theme"]!.GetValue<string>() == "Dark" && sync.Error is null, "Stable valid save applies");
    Check(SettingsJson.Parse(File.ReadAllText(Path.Combine(directory, "settings.last-good.json")))["qsys"]!["general"]!["Theme"]!.GetValue<string>() == "Dark", "Last good advances after success");
    int applied = target.Applications; sync.Poll(); sync.Poll(); Check(target.Applications == applied, "Own projection causes no feedback loop");
    string good = File.ReadAllText(path);
    File.WriteAllText(path, "{unfinished"); sync.Poll(); sync.Poll();
    Check(sync.Error is not null && target.Applications == applied && File.ReadAllText(path) == "{unfinished", "Malformed save preserves both live values and editable text");
    var invalid = (JsonObject)edited.DeepClone(); invalid["mods"]!["example"]!["Gap"] = -1;
    File.WriteAllText(path, SettingsJson.Text(invalid)); sync.Poll(); sync.Poll();
    Check(target.Applications == applied && sync.Error!.Contains("Gap"), "Whole-document validation runs before application");
    File.WriteAllText(path, good); sync.Poll(); sync.Poll();
    // Restoring byte-identical accepted text must clear a stale error.
    Check(target.Applications == applied, "Restoring the accepted file requires no application");
    edited["mods"]!["example"]!["Gap"] = 30; File.WriteAllText(path, SettingsJson.Text(edited)); sync.Poll(); sync.Poll();
    Check(sync.Error is null && target.Value["mods"]!["example"]!["Gap"]!.GetValue<int>() == 30, "Correcting invalid input recovers without restart");
    target.Value["qsys"]!["general"]!["Theme"] = "Light"; sync.Poll();
    Check(SettingsJson.Parse(File.ReadAllText(path))["qsys"]!["general"]!["Theme"]!.GetValue<string>() == "Light", "Native Preferences changes update the file");
    Check(File.Exists(path + ".previous"), "Native projection writes retain previous file");
    edited = (JsonObject)target.Value.DeepClone(); edited["mods"]!["example"]!["Gap"] = 40;
    File.WriteAllText(path, SettingsJson.Text(edited)); target.Value["qsys"]!["general"]!["Theme"] = "Dark";
    sync.Poll(); sync.Poll(); Check(sync.Error!.Contains("both changed") && target.Value["mods"]!["example"]!["Gap"]!.GetValue<int>() == 30, "Concurrent native and file changes never silently win");
    sync.Retry(); sync.Poll(); sync.Poll(); Check(sync.Error is null && target.Value["mods"]!["example"]!["Gap"]!.GetValue<int>() == 40, "Explicit retry acknowledges a simultaneous edit");
    target.FailNext = true; edited["mods"]!["example"]!["Gap"] = 50;
    File.WriteAllText(path, SettingsJson.Text(edited)); sync.Poll(); sync.Poll();
    Check(sync.Error!.Contains("restored") && !sync.Faulted && target.Value["mods"]!["example"]!["Gap"]!.GetValue<int>() == 40, "Partial application is compensated");
    Check(SettingsJson.Parse(File.ReadAllText(Path.Combine(directory, "settings.last-good.json")))["mods"]!["example"]!["Gap"]!.GetValue<int>() == 40, "Failed candidate cannot replace recovery snapshot");
    // Avoid retrying a failed write on every timer tick.
    int afterFailure = target.Applications; sync.Poll(); Check(target.Applications == afterFailure, "Failed candidate waits for a new save or explicit retry");
    File.Delete(path); sync.Poll(); sync.Poll(); Check(sync.Error is not null, "Deletion does not reset native preferences");
    sync.Recreate(); Check(File.Exists(path) && sync.Error is null, "Explicit reopen recreates missing projection");
}
using (var reopened = new SettingsSynchronizer(path, target))
{ reopened.Poll(); reopened.Poll(); Check(reopened.Error is null, "Existing settings survive service restart"); }
Reject(() => SettingsJson.Parse("{\"schemaVersion\":1,\"schemaVersion\":1}"), "Duplicate keys rejected");
Reject(() => SettingsJson.Parse("{\"schemaVersion\":1,\"SchemaVersion\":1}"), "Case duplicate keys rejected");
Reject(() => SettingsJson.Parse("[]"), "Non-object documents rejected");
var wrong = (JsonObject)target.Value.DeepClone(); wrong["schemaVersion"] = 9;
Reject(() => SettingsJson.Parse(SettingsJson.Text(wrong)), "Unknown format cannot overwrite live settings");
wrong["schemaVersion"] = 1; wrong["designerVersion"] = "10.6.0.0";
Reject(() => SettingsJson.Parse(SettingsJson.Text(wrong)), "Different Designer version rejected");
wrong["designerVersion"] = "10.5.0.0"; wrong["typo"] = true;
Reject(() => SettingsJson.Parse(SettingsJson.Text(wrong)), "Unknown top-level field rejected");
Reject(() => SettingsJson.Parse(new string(' ', SettingsJson.MaxBytes + 1)), "Oversized documents rejected");
var jsonc = SettingsJson.Text(target.Value).Replace("{", "{ // comment\n", StringComparison.Ordinal);
Check(SettingsJson.Parse(jsonc) is not null, "JSON comments accepted");
Check(SettingsJson.Parse(SettingsJson.Text(target.Value).TrimEnd().Insert(SettingsJson.Text(target.Value).TrimEnd().Length - 1, ",")) is not null, "Trailing commas accepted");
var partial = SettingsJson.Parse("""{"schemaVersion":1,"designerVersion":"10.5.0.0","qsys":{"general":{"Theme":"Light"}},"mods":{"example":{"Gap":0,"Bindings":{}}}}""");
var merged = SettingsJson.MergeDocument(target.Value, partial);
Check(merged["qsys"]!["hardLinks"] is JsonArray && merged["mods"]!["absent-mod"]!["Retain"]!.GetValue<int>() == 42, "Omitted native fields and absent-mod sections preserved");
Check(((JsonObject)merged["mods"]!["example"]!["Bindings"]!).Count == 0, "An empty mod dictionary really clears assignments");
string original = File.ReadAllText(path); File.WriteAllText(path, original + " ");
Reject(() => SettingsJson.Write(path, "replacement", original), "Stale atomic writer refuses an external edit");
Check(File.ReadAllText(path) == original + " ", "Refused writer preserves external bytes");
var brokenTarget = new Fixture { FailAlways = true };
string badPath = Path.Combine(directory, "broken", "settings.json");
using (var broken = new SettingsSynchronizer(badPath, brokenTarget))
{
    var change = (JsonObject)brokenTarget.Value.DeepClone(); change["mods"]!["example"]!["Gap"] = 88;
    File.WriteAllText(badPath, SettingsJson.Text(change)); broken.Poll(); broken.Poll();
    Check(broken.Faulted && broken.Error!.Contains("recovery failed"), "Rollback failure stops live sync");
    int attempts = brokenTarget.Applications; broken.Poll(); Check(brokenTarget.Applications == attempts, "Stopped sync never repeats a failed mutation");
}
Console.WriteLine($"Passed {count} live settings checks. No Q-SYS preferences were accessed.");

sealed class Fixture : ISettingsTarget
{
    public JsonObject Value = SettingsJson.Parse("""{"$schema":"./settings.schema.json","schemaVersion":1,"designerVersion":"10.5.0.0","qsys":{"general":{"Theme":"Light"},"hardLinks":[]},"mods":{"example":{"Gap":20,"Bindings":{"one":"Ctrl+K"}},"absent-mod":{"Retain":42}}}""");
    public int Applications;
    public bool FailNext, FailAlways;
    public JsonObject Capture() => (JsonObject)Value.DeepClone();
    public void Validate(JsonObject value) { if (value["mods"]!["example"]!["Gap"]!.GetValue<int>() < 0) throw new ArgumentException("Gap must be nonnegative."); }
    public void Apply(JsonObject value)
    {
        Applications++; Value = (JsonObject)value.DeepClone();
        if (FailNext || FailAlways) { FailNext = false; throw new IOException("Injected save failure"); }
    }
}
