using QsysMods.Core;
using System.Text.Json;

string dir = Path.Combine(Path.GetTempPath(), "qsys-mods-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
string path = Path.Combine(dir,"preferences.json");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { try { action(); } catch (Exception) { Console.WriteLine("PASS " + message); return; } throw new Exception(message); }
var first = new SettingsStore(path);
Check(first.Read("better-packing", () => new Example(20)).Gap == 20 && !File.Exists(path), "Reading defaults leaves disk unchanged");
first.Save(new Dictionary<string,object> { ["better-packing"] = new Example(37), ["other-mod"] = new Example(99) });
var stale = new SettingsStore(path);
first.Save(new Dictionary<string,object> { ["better-packing"] = new Example(12) });
Check(new SettingsStore(path).Read("other-mod", () => new Example(0)).Gap == 99, "Saving one page preserves other and absent mod settings");
Reject(() => stale.Save(new Dictionary<string,object> { ["better-packing"] = new Example(22) }), "Stale dialogs cannot overwrite newer preferences");
Check(new SettingsStore(path).Read("better-packing", () => new Example(0)).Gap == 12, "Rejected stale save leaves latest value intact");
Check(new SettingsStore(path+".previous").Read("better-packing", () => new Example(0)).Gap == 37, "Atomic save retains previous document");
File.WriteAllText(path, "{broken}"); Reject(() => new SettingsStore(path), "Malformed preferences are rejected instead of reset"); Check(File.ReadAllText(path) == "{broken}", "Malformed original preserved");
Reject(() => SettingsStore.ValidateId("../escape"), "Unsafe setting identifiers rejected");
Console.WriteLine("Settings checks complete: " + dir);
record Example(int Gap);
