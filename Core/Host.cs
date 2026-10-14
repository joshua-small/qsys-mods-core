using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace QsysMods.Core;

public static class Host
{
    public const string DesignerHash = "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60";
    public const string CommonHash = "a6d1addda06bdcb1ad32b57c7480c947210c3d3ad287f5818c2ea64068bf5aee";
    public const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static bool Enabled { get; } = Check();
    public static bool IsLab => File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"));
    public static string SettingsDirectory => IsLab ? Path.Combine(AppContext.BaseDirectory, "mods-lab-settings")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QsysMods", "Designer-10.5");
    public static bool DeveloperMode => Environment.GetCommandLineArgs().Any(a => a.Equals("/mods-dev", StringComparison.OrdinalIgnoreCase))
        || (Type("QSC.QSys.App").GetField("AppInfo", Static)?.GetValue(null) is { } app && Property(app, "IsDevelopmentMode") is true);
    private static bool Check()
    {
        try { return Hash("Q-Sys Designer.dll") == DesignerHash && Hash("ModuleCommon.dll") == CommonHash; }
        catch { return false; }
    }
    private static string Hash(string name) { using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, name)); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    public static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t is not null)
        ?? throw new MissingMemberException("Host type unavailable: " + name);
    public static object Field(object value, string name) => value.GetType().GetField(name, Instance)?.GetValue(value) ?? throw new MissingFieldException(value.GetType().FullName, name);
    public static object Property(object value, string name) => value.GetType().GetProperty(name, Instance)?.GetValue(value) ?? throw new MissingMemberException(value.GetType().FullName, name);
    public static void Call(object value, string name) => (value.GetType().GetMethod(name, Instance, null, System.Type.EmptyTypes, null) ?? throw new MissingMethodException(value.GetType().FullName, name)).Invoke(value, null);
    public static string Receipt(string mod, string kind, object detail)
    {
        try
        {
            SettingsStore.ValidateId(mod); var dir = Path.Combine(SettingsDirectory, "diagnostics", mod); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { kind, detail }, new JsonSerializerOptions { WriteIndented = true })); return path;
        }
        catch { return ""; }
    }
    public static void Error(string mod, Exception error, bool notify = false)
    {
        Receipt(mod, "error", new { error = error.ToString() });
        if (notify) MessageBox.Show(Application.Current.MainWindow, error.GetBaseException().Message, "Q-SYS Mods");
    }
    public static void Theme(Window window) => window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
}
