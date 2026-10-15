// Preferences.Install exits before this boundary because Host.Enabled is false.
// Production builds use the actual Harmony assembly; tests do not patch anything.
using System.Reflection;
namespace HarmonyLib;
public sealed class Harmony(string id)
{
    public static PatchInfo? GetPatchInfo(MethodInfo method) => throw new NotSupportedException();
    public void Patch(MethodInfo method, HarmonyMethod prefix) => throw new NotSupportedException(id);
}
public sealed class PatchInfo { public string[] Owners => []; }
public sealed class HarmonyMethod { public HarmonyMethod(Type type, string method) { } }
