using System.Reflection;
using System.Windows;

namespace QsysMods.Core;

// Only host initialization/preferences integration is substituted. Registry,
// planner, binding engine, replacement router, review UI and storage are production source.
public static class Host
{
    public const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static bool Enabled => true;
    public static Type Type(string name) => typeof(FloatingFixture);
    public static void Error(string mod, Exception ex, bool notify = false) { }
    public static void Theme(Window window) { }
}
public sealed class FloatingFixture : Window { }
public static class Mods
{
    public static string? InitializingId { get; set; }
    public static readonly Dictionary<string, string> States = new();
    public static bool IsReady(string id) => Status(id) == "Ready";
    public static string Status(string id) => States.GetValueOrDefault(id, "Not loaded");
}
public static class Preferences
{
    public static string PathName { get; set; } = "";
    public static void RegisterControlsPage(string id, string name, Func<ControlDraft, FrameworkElement> create) { }
    public static void RegisterControlsPage(string id, string name, Func<ControlDraft, FrameworkElement> create, Func<bool> visible) { }
    public static void Open(string? id = null) { }
}
