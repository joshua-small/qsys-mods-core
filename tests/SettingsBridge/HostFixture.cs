using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Serialization;

namespace QsysMods.Core;

// Production adapters and preference registration are exercised against a fake
// native persistence boundary. No Q-SYS process or real preference file is used.
public static class Host
{
    public const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    public static bool Enabled => false;
    public static bool IsLab => false;
    public static string SettingsDirectory { get; set; } = "";
    public static Type Type(string name) => name switch
    {
        "QSC.QSys.windows.Dialogs.GeneralSettings" => typeof(FakeGeneral),
        "QSC.QSys.Discovery.HardLinkManager" => typeof(FakeLinks),
        "QSC.QSys.Discovery.HardLink" => typeof(FakeLink),
        "QSC.QSys.Discovery.HardLinkValidation" => typeof(FakeValidation),
        "QSC.Common.SettingsManager" => typeof(FakeSettingsManager),
        _ => typeof(Window)
    };
    public static object Property(object obj, string name) => obj.GetType().GetProperty(name)!.GetValue(obj)!;
    public static void Call(object obj, string name) => obj.GetType().GetMethod(name)!.Invoke(obj, null);
    public static void Error(string mod, Exception ex, bool notify = false) { }
    public static void Theme(Window window) { }
    public static string Receipt(string mod, string kind, object value) => "";
}
public static class Mods
{
    public static string? InitializingId => null;
    public static bool IsReady(string id) => true;
    public static string Status(string id) => "Ready";
}
public enum Choice { Light, Dark }
public sealed class FakeModel : IDataErrorInfo
{
    public Choice Mode { get; set; }
    public int PeriodicHoldoffMinutes { get; set; } = 5;
    public Choice Theme { get; set; }
    public Choice AudioApi { get; set; }
    public int AudioBuffer { get; set; } = 100;
    public Choice AudioQuality { get; set; }
    public Choice ScriptEditor { get; set; }
    public bool CollectPerformanceData { get; set; }
    public bool ProxyServerEnabled { get; set; }
    public string ProxyAddress { get; set; } = "";
    public string AssetDirectory { get; set; } = "C:\\Assets";
    public bool DisableNotifications { get; set; }
    public Choice DefaultScriptAccess { get; set; }
    [TypeConverter(typeof(StyleChoices))] public string TraceWireStyle { get; set; } = "Dashed";
    public bool HighlightComponents { get; set; } = true;
    public Color TraceColorAudio { get; set; } = Colors.Blue;
    public Color TraceColorVideo { get; set; } = Colors.Red;
    public Color TraceColorControl { get; set; } = Colors.Green;
    public Color TraceColorMediacast { get; set; } = Colors.White;
    public bool EnableNewSchematicElements { get; set; } = true;
    public string ProxyPassword { get; set; } = "fixture-secret";
    public string ProxyUserName { get; set; } = "fixture-user";
    public string Error => "";
    public string this[string name] => name == "ProxyAddress" && ProxyAddress == "invalid" ? "Invalid" : "";
    public FakeModel Copy() => (FakeModel)MemberwiseClone();
}
public sealed class StyleChoices : TypeConverter
{
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(new[] { "Dashed", "Solid" });
}
public sealed class FakeGeneral
{
    public static FakeModel Saved = new();
    public static int Saves;
    public static Action? OnSave;
    public FakeModel Model { get; } = Saved.Copy();
    public static bool Fail;
    public void Save() { Saves++; Saved = Model.Copy(); FakeSettingsManager.Update(Saved, Fail); var callback = OnSave; OnSave = null; callback?.Invoke(); }
}
[XmlRoot("Data")] public sealed class FakeSettingsData { [XmlArray("data"), XmlArrayItem("Entry")] public FakeEntry[] Entries { get; set; } = []; }
public sealed class FakeEntry { [XmlElement("key")] public string Key { get; set; } = ""; [XmlElement("data")] public string Value { get; set; } = ""; }
public static class FakeSettingsManager
{
    public static FakeSettingsData data = new();
    public static string FileName => Path.Combine(Host.SettingsDirectory, "fixture-prefs.xml");
    public static void Update(FakeModel model, bool fail)
    {
        var aliases = new Dictionary<string, string> { ["Mode"] = "RecoveryMode", ["PeriodicHoldoffMinutes"] = "RecoveryPeriodicHoldoff", ["Theme"] = "ColorScheme2",
            ["AudioApi"] = "AudioMonitorApi", ["AudioBuffer"] = "AudioMonitorBuffer", ["AudioQuality"] = "AudioMonitorQuality" };
        data.Entries = NativePreferences.GeneralNames.Select(n => new FakeEntry { Key = aliases.GetValueOrDefault(n, n), Value = Host.Property(model, n).ToString()! }).ToArray();
        if (fail) return;
        using var writer = File.CreateText(FileName); new XmlSerializer(typeof(FakeSettingsData)).Serialize(writer, data);
    }
}
public sealed class FakeLink
{
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public bool IsEnabled { get; set; }
}
public static class FakeLinks
{
    public static ObservableCollection<FakeLink> Links { get; } = new();
    public static string JsonFileName => Path.Combine(Host.SettingsDirectory, "fixture-hardlinks.json");
    public static string NamesFileName => Path.Combine(Host.SettingsDirectory, "fixture-hardlink-names.json");
    public static bool Fail;
    public static bool FailNames;
    public static void Save() { if (!Fail) File.WriteAllText(JsonFileName, JsonSerializer.Serialize(new { Links }));
        if (!FailNames) File.WriteAllText(NamesFileName, JsonSerializer.Serialize(new { Names = Links.Where(l => l.Name.Length > 0).ToDictionary(l => l.IpAddress, l => l.Name) })); }
}
public static class FakeValidation
{
    public static bool IsReservedIp(string value) => !System.Net.IPAddress.TryParse(value, out var ip) || ip.GetAddressBytes()[0] is 0 or 127;
}
