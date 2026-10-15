using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;

// Test fixture only. Never package with Mods Core. DOTNET_STARTUP_HOOKS is scoped
// to the disposable child process by the lab launcher, before native static init.
public static class StartupHook
{
    private static string root = "";
    public static void Initialize()
    {
        // Chromium child processes use another runtime and must not inherit a
        // Designer-only startup hook. The current runtime has consumed these.
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);
        Environment.SetEnvironmentVariable("DOTNET_ReadyToRun", null);
        Environment.SetEnvironmentVariable("DOTNET_TieredCompilation", null);
        Environment.SetEnvironmentVariable("COMPlus_JitNoInline", null);
        var marker = Path.Combine(AppContext.BaseDirectory, ".settings-live-lab.json");
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(marker))!;
        if (!Path.GetFullPath(config["ApplicationDirectory"]).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Not the exact disposable application directory.");
        root = Path.GetFullPath(config["ProfileDirectory"]);
        if (!root.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Profile must be inside the lab.");
        Directory.CreateDirectory(root);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "0Harmony.dll"));
        Redirect();
        Verify();
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Verify()
    {
        var paths = new Dictionary<string, string>();
        foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.Personal, Environment.SpecialFolder.UserProfile })
        {
            var path = Environment.GetFolderPath(folder);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Profile redirection failed: " + folder);
            Directory.CreateDirectory(path); paths[folder.ToString()] = path;
        }
        File.WriteAllText(Path.Combine(root, "isolation.json"), JsonSerializer.Serialize(paths));
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Redirect()
    {
        var harmony = new Harmony("qsys.settings.disposable-profile");
        foreach (var method in typeof(Environment).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name is "GetFolderPath" or "GetFolderPathCore"))
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(StartupHook), nameof(Folder)));
    }
    public static bool Folder(Environment.SpecialFolder folder, ref string __result)
    {
        string? name = folder switch {
            Environment.SpecialFolder.ApplicationData => "Roaming",
            Environment.SpecialFolder.LocalApplicationData => "Local",
            Environment.SpecialFolder.Personal => "Documents",
            Environment.SpecialFolder.UserProfile => "User",
            _ => null
        };
        if (name is null) return true;
        __result = Path.Combine(root, name); return false;
    }
}
