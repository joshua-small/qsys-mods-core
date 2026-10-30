using QsysMods.Core;
using System.Text.Json;

string dir = Path.Combine(Path.GetTempPath(), "qsys-mods-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
string path = Path.Combine(dir,"preferences.json");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { try { action(); } catch (Exception) { Console.WriteLine("PASS " + message); return; } throw new Exception(message); }
void RejectArgument(Action action, string message) { try { action(); } catch (ArgumentException) { Console.WriteLine("PASS " + message); return; } throw new Exception(message); }
var first = new SettingsStore(path);
Check(first.Read("better-packing", () => new Example(20)).Gap == 20 && !File.Exists(path), "Reading defaults leaves disk unchanged");
first.Save(new Dictionary<string,object> { ["better-packing"] = new Example(37), ["other-mod"] = new Example(99) });
var stale = new SettingsStore(path);
first.Save(new Dictionary<string,object> { ["better-packing"] = new Example(12) });
Check(new SettingsStore(path).Read("other-mod", () => new Example(0)).Gap == 99, "Saving one page preserves other and absent mod settings");
Reject(() => stale.Save(new Dictionary<string,object> { ["better-packing"] = new Example(22) }), "Stale dialogs cannot overwrite newer preferences");
Check(new SettingsStore(path).Read("better-packing", () => new Example(0)).Gap == 12, "Rejected stale save leaves latest value intact");
Check(new SettingsStore(path+".previous").Read("better-packing", () => new Example(0)).Gap == 37, "Atomic save retains previous document");
foreach (var id in new[] { "ab", "mod-2", "a" + new string('b', 63) })
{
    SettingsStore.ValidateId(id);
    Check(true, "Valid identifier accepted: " + JsonSerializer.Serialize(id));
}
foreach (var id in new[] { "", "a", "a" + new string('b', 64), "1a", "Ab", "a b", "ab/c", "ab\n", "ab\r", "ab\r\n", "éa" })
    RejectArgument(() => SettingsStore.ValidateId(id), "Invalid identifier rejected: " + JsonSerializer.Serialize(id));

var untouched = new SettingsStore(Path.Combine(dir, "rejected", "preferences.json"));
bool defaultsCalled = false;
RejectArgument(() => untouched.Read("example\n", () => { defaultsCalled = true; return new Example(0); }), "Invalid read identifier rejected");
Check(!defaultsCalled, "Rejected read does not invoke defaults");
RejectArgument(() => untouched.Save(new Dictionary<string, object> { ["valid-mod"] = new Example(10), ["example\n"] = new Example(20) }), "Invalid identifier rejects the whole new save");
Check(!Directory.Exists(Path.Combine(dir, "rejected")) && untouched.Snapshot().Count == 0, "Rejected new save creates no files and keeps memory unchanged");

byte[] currentBefore = File.ReadAllBytes(path), previousBefore = File.ReadAllBytes(path + ".previous");
RejectArgument(() => first.Save(new Dictionary<string, object> { ["better-packing"] = new Example(88), ["example\n"] = new Example(20) }), "Invalid identifier rejects the whole existing save");
Check(File.ReadAllBytes(path).SequenceEqual(currentBefore)
    && File.ReadAllBytes(path + ".previous").SequenceEqual(previousBefore)
    && first.Read("better-packing", () => new Example(0)).Gap == 12
    && !first.Contains("example\n")
    && Directory.GetFiles(dir, "*.tmp").Length == 0, "Rejected save preserves current, backup and in-memory values without temporary files");
File.WriteAllText(path, "{broken}"); Reject(() => new SettingsStore(path), "Malformed preferences are rejected instead of reset"); Check(File.ReadAllText(path) == "{broken}", "Malformed original preserved");
Reject(() => SettingsStore.ValidateId("../escape"), "Unsafe setting identifiers rejected");
Console.WriteLine("Settings checks complete: " + dir);
record Example(int Gap);
