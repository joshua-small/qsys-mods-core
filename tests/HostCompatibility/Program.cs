using System.Security.Cryptography;
using System.Text.Json.Nodes;
using QsysMods.Core;

const string testedHost = "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60";
const string testedCommon = "a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee";
if (args is ["--manager-state", var managed])
{
    if (!HostCompatibility.Allows(managed, testedHost, testedCommon)) throw new Exception("Manager-written opt-in was not accepted by Core");
    Console.WriteLine("Core accepts the actual Manager-written opt-in."); return;
}
string output = Path.GetFullPath(args.Single());
if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
string root = Path.Combine(output, "designer"); Directory.CreateDirectory(root);
string host = Path.Combine(root, "Q-Sys Designer.dll"), common = Path.Combine(root, "ModuleCommon.dll");
File.WriteAllBytes(host, [1,2,3]); File.WriteAllBytes(common, [4,5,6]);
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
bool Allows() => HostCompatibility.Allows(root, testedHost, testedCommon);
Check(!Allows(), "Untested build defaults to blocked");
Check(HostCompatibility.Allows(root, Hash(host), Hash(common)), "Tested build requires no manager state");
string directory = Path.Combine(root, ".qds-mod-manager"); Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "state.json");
JsonObject Valid() => new()
{
    ["schema"] = 2, ["suspended"] = false,
    ["mods"] = new JsonObject { ["mods-core"] = new JsonObject { ["id"] = "mods-core", ["enabled"] = true } },
    ["untestedRuntime"] = new JsonObject { ["schema"] = 1, ["target"] = root, ["hostSha256"] = Hash(host), ["commonSha256"] = Hash(common) }
};
void Save(JsonObject value) => File.WriteAllText(path, value.ToJsonString());
Save(Valid()); Check(Allows(), "Matching installation and hashes allow runtime startup");
void Reject(Action<JsonObject> mutate, string label) { var state = Valid(); mutate(state); Save(state); Check(!Allows(), label); }
Reject(s => s.Remove("untestedRuntime"), "Missing consent blocks startup");
Reject(s => s["schema"] = 3, "Unknown registry schema blocks startup");
Reject(s => s["untestedRuntime"]!["schema"] = 2, "Unknown consent schema blocks startup");
Reject(s => s["untestedRuntime"]!["target"] = output, "Consent cannot transfer to another installation");
Reject(s => s["untestedRuntime"]!["target"] = ".", "Relative consent target is rejected");
Reject(s => s["untestedRuntime"]!["hostSha256"] = new string('a',64), "Changed Designer hash blocks startup");
Reject(s => s["untestedRuntime"]!["commonSha256"] = new string('a',64), "Changed ModuleCommon hash blocks startup");
Reject(s => s["suspended"] = true, "Suspension blocks untested runtime");
Reject(s => s["mods"]!["mods-core"]!["enabled"] = false, "Disabled Core blocks untested runtime");
Reject(s => s["mods"]!.AsObject().Remove("mods-core"), "Removed Core blocks untested runtime");
Reject(s => s["mods"]!["mods-core"]!["id"] = "another-mod", "Wrong Core identity blocks runtime");
File.WriteAllText(path, "not json"); Check(!Allows(), "Malformed state is rejected");
File.WriteAllText(path, Valid().ToJsonString().Insert(1, "\"schema\":2,")); Check(!Allows(), "Duplicate state properties are rejected");
File.WriteAllText(path, new string(' ',1024*1024+1)); Check(!Allows(), "Oversized state is rejected");
Save(Valid()); File.WriteAllBytes(common, []); Check(!Allows(), "Empty host file is rejected");
File.WriteAllBytes(common, [4,5,6]); Save(Valid()); File.Delete(common); Check(!Allows(), "Missing host file is rejected"); File.WriteAllBytes(common, [4,5,6]);
Save(Valid()); string linked = Path.Combine(output, "linked-state.json"); File.Move(path, linked);
File.CreateSymbolicLink(path, linked); Check(!Allows(), "Linked opt-in state is rejected"); File.Delete(path); File.Move(linked, path);
Check(Allows(), "Restoring valid state restores eligibility");
File.WriteAllText(Path.Combine(output,"checks.json"), System.Text.Json.JsonSerializer.Serialize(checks));
Console.WriteLine($"Passed {checks.Count} host compatibility checks.");
