using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace QsysMods.Core;

/// <summary>Allowlisted adapter for the hash-guarded Designer 10.5 Preferences model.</summary>
internal sealed class NativePreferences
{
    internal static readonly string[] GeneralNames = ["Mode", "PeriodicHoldoffMinutes", "Theme", "AudioApi", "AudioBuffer", "AudioQuality",
        "ScriptEditor", "CollectPerformanceData", "ProxyServerEnabled", "ProxyAddress", "AssetDirectory", "DisableNotifications",
        "DefaultScriptAccess", "TraceWireStyle", "HighlightComponents", "TraceColorAudio", "TraceColorVideo", "TraceColorControl",
        "TraceColorMediacast", "EnableNewSchematicElements"];
    private readonly Type generalType = Host.Type("QSC.QSys.windows.Dialogs.GeneralSettings");
    private readonly Type linksType = Host.Type("QSC.QSys.Discovery.HardLinkManager");
    private readonly Type linkType = Host.Type("QSC.QSys.Discovery.HardLink");
    private object? previousGeneral;
    private object[] previousLinks = [];
    private bool generalAttempted, linksAttempted;
    private object General() => Activator.CreateInstance(generalType)!;
    private static object Model(object general) => Host.Property(general, "Model");
    private IList Links => (IList)linksType.GetProperty("Links", Host.Static)!.GetValue(null)!;
    public JsonObject Capture()
    {
        var model = Model(General());
        var general = new JsonObject();
        foreach (string name in GeneralNames)
        {
            var value = Host.Property(model, name);
            general[name] = value is Color color ? JsonValue.Create(color.ToString(CultureInfo.InvariantCulture))
                : value is Enum ? JsonValue.Create(value.ToString()) : JsonSerializer.SerializeToNode(value);
        }
        var links = new JsonArray();
        foreach (var link in Links)
            links.Add(new JsonObject { ["Name"] = (string)Host.Property(link!, "Name"), ["IpAddress"] = (string)Host.Property(link!, "IpAddress"), ["IsEnabled"] = (bool)Host.Property(link!, "IsEnabled") });
        return new() { ["general"] = general, ["hardLinks"] = links };
    }
    private object PrepareGeneral(JsonObject values)
    {
        var general = General(); var model = Model(general);
        foreach (var pair in values)
        {
            if (!GeneralNames.Contains(pair.Key)) throw new ArgumentException("Unknown Q-SYS preference: " + pair.Key);
            var property = model.GetType().GetProperty(pair.Key)!;
            var node = pair.Value ?? throw new ArgumentException(pair.Key + " cannot be null.");
            object value;
            if (property.PropertyType.IsEnum)
            {
                string name = node.GetValue<string>();
                if (!Enum.GetNames(property.PropertyType).Contains(name)) throw new ArgumentException("Invalid " + pair.Key + " choice.");
                value = Enum.Parse(property.PropertyType, name);
            }
            else if (property.PropertyType == typeof(Color))
            {
                string color = node.GetValue<string>();
                if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-F]{8}$")) throw new ArgumentException(pair.Key + " must use uppercase #AARRGGBB.");
                value = ColorConverter.ConvertFromString(color);
            }
            else value = node.Deserialize(property.PropertyType) ?? throw new ArgumentException("Empty " + pair.Key);
            if (pair.Key == "PeriodicHoldoffMinutes" && ((int)value < 0 || (int)value > 1999999999)) throw new ArgumentException("Save timeout must be 0–1999999999 minutes.");
            if (pair.Key == "AudioBuffer" && (int)value < 0) throw new ArgumentException("AudioBuffer cannot be negative.");
            if (pair.Key == "TraceWireStyle")
            {
                var converter = TypeDescriptor.GetProperties(model)[pair.Key]!.Converter;
                if (!converter.GetStandardValues()!.Cast<object>().Contains(value)) throw new ArgumentException("Unknown trace wire style.");
            }
            property.SetValue(model, value);
        }
        if (model is IDataErrorInfo errors && !string.IsNullOrEmpty(errors["ProxyAddress"])) throw new ArgumentException("Invalid proxy server address.");
        return general;
    }
    private object[] PrepareLinks(JsonArray values)
    {
        if (values.Count > 10000) throw new ArgumentException("Too many hard links.");
        var ips = new HashSet<string>(StringComparer.Ordinal);
        var reserved = Host.Type("QSC.QSys.Discovery.HardLinkValidation").GetMethod("IsReservedIp", Host.Static)!;
        return values.Select(node =>
        {
            if (node is not JsonObject value || value.Count != 3 || value.Any(p => p.Key is not ("Name" or "IpAddress" or "IsEnabled")))
                throw new ArgumentException("Each hard link requires Name, IpAddress and IsEnabled.");
            string ip = value["IpAddress"]!.GetValue<string>();
            if (ip != ip.Trim() || (bool)reserved.Invoke(null, [ip])! || !ips.Add(ip)) throw new ArgumentException("Invalid, reserved or duplicate hard-link address.");
            var link = Activator.CreateInstance(linkType)!;
            linkType.GetProperty("Name")!.SetValue(link, value["Name"]!.GetValue<string>());
            linkType.GetProperty("IpAddress")!.SetValue(link, ip);
            linkType.GetProperty("IsEnabled")!.SetValue(link, value["IsEnabled"]!.GetValue<bool>());
            var existing = Links.Cast<object>().FirstOrDefault(l => Equals(Host.Property(l, "IpAddress"), ip));
            if (existing is not null && linkType.GetProperty("ExtensionData") is { } extra)
                extra.SetValue(link, extra.GetValue(existing)); // retain unexposed per-link vendor fields
            return link;
        }).ToArray();
    }
    public void Validate(JsonObject value)
    {
        if (value.Count != 2 || value["general"] is not JsonObject general || value["hardLinks"] is not JsonArray links)
            throw new ArgumentException("qsys requires general and hardLinks.");
        _ = PrepareGeneral(general); _ = PrepareLinks(links);
    }
    public void Apply(JsonObject value)
    {
        generalAttempted = linksAttempted = false;
        var before = Capture();
        var general = PrepareGeneral((JsonObject)value["general"]!);
        var links = PrepareLinks((JsonArray)value["hardLinks"]!);
        previousGeneral = General(); previousLinks = Links.Cast<object>().ToArray();
        if (!SettingsJson.Same(before["general"], value["general"])) { generalAttempted = true; Host.Call(general, "Save"); VerifyGeneral(); }
        if (!SettingsJson.Same(before["hardLinks"], value["hardLinks"]))
        {
            // No Preferences window is open while syncing, so its collection
            // watcher cannot persist an intermediate Clear/Add sequence.
            linksAttempted = true;
            var collection = Links; collection.Clear(); foreach (var link in links) collection.Add(link);
            linksType.GetMethod("Save", Host.Static)!.Invoke(null, null);
            // The native save swallows IO errors. Verify its persisted values.
            string path = (string)linksType.GetProperty("JsonFileName", Host.Static)!.GetValue(null)!;
            var saved = JsonNode.Parse(File.ReadAllText(path))?["Links"] as JsonArray ?? throw new IOException("Hard Links did not save.");
            var projection = new JsonArray(saved.Select(n => (JsonNode)new JsonObject { ["Name"] = n!["Name"]?.DeepClone(), ["IpAddress"] = n["IpAddress"]?.DeepClone(), ["IsEnabled"] = n["IsEnabled"]?.DeepClone() }).ToArray());
            if (!SettingsJson.Same(projection, value["hardLinks"])) throw new IOException("Hard Links did not retain the requested values.");
            VerifyLinkNames(links);
        }
    }
    public void Restore()
    {
        var errors = new List<Exception>();
        if (generalAttempted) try { Host.Call(previousGeneral!, "Save"); VerifyGeneral(); } catch (Exception ex) { errors.Add(ex); }
        if (linksAttempted) try
        {
            var collection = Links; collection.Clear(); foreach (var link in previousLinks) collection.Add(link);
            linksType.GetMethod("Save", Host.Static)!.Invoke(null, null);
            string path = (string)linksType.GetProperty("JsonFileName", Host.Static)!.GetValue(null)!;
            var saved = JsonNode.Parse(File.ReadAllText(path))?["Links"] as JsonArray;
            var expected = JsonSerializer.SerializeToNode(previousLinks);
            if (!SettingsJson.Same(saved, expected)) throw new IOException("Hard Links recovery could not be verified.");
            VerifyLinkNames(previousLinks);
        }
        catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("Native preferences recovery failed.", errors);
    }
    private static void VerifyGeneral()
    {
        // SettingsManager catches write failures. Compare its serialized values
        // with the file without reloading or changing the live native model.
        var manager = Host.Type("QSC.Common.SettingsManager");
        var data = manager.GetField("data", Host.Static)!.GetValue(null)!;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new XmlSerializer(data.GetType()).Serialize(writer, data);
        var expected = XDocument.Parse(writer.ToString()).Root!.Element("data")!;
        var path = (string)manager.GetProperty("FileName", Host.Static)!.GetValue(null)!;
        var saved = XDocument.Load(path).Root?.Element("data") ?? throw new IOException("Native preferences did not save.");
        string[] keys = ["RecoveryMode", "RecoveryPeriodicHoldoff", "ColorScheme2", "AudioMonitorApi", "AudioMonitorBuffer", "AudioMonitorQuality",
            "ScriptEditor", "CollectPerformanceData", "ProxyServerEnabled", "ProxyAddress", "AssetDirectory", "DisableNotifications",
            "DefaultScriptAccess", "TraceWireStyle", "HighlightComponents", "TraceColorAudio", "TraceColorVideo", "TraceColorControl", "TraceColorMediacast", "EnableNewSchematicElements"];
        foreach (var key in keys)
        {
            var entry = expected.Elements().SingleOrDefault(e => (string?)e.Element("key") == key);
            var actual = saved.Elements().SingleOrDefault(e => (string?)e.Element("key") == key);
            if (entry is null || !XNode.DeepEquals(entry, actual)) throw new IOException("Native preferences did not retain " + key + ".");
        }
    }
    private void VerifyLinkNames(object[] links)
    {
        var expected = new JsonObject();
        foreach (var link in links)
            if (Host.Property(link, "Name") is string { Length: > 0 } name)
                expected[((string)Host.Property(link, "IpAddress")).Trim()] = name;
        string path = (string)linksType.GetProperty("NamesFileName", Host.Static)!.GetValue(null)!;
        if (!SettingsJson.Same(JsonNode.Parse(File.ReadAllText(path))?["Names"], expected))
            throw new IOException("Hard Link names did not retain the requested values.");
    }
    public JsonObject Schema()
    {
        var model = Model(General()); var properties = new JsonObject();
        foreach (string name in GeneralNames)
        {
            var property = model.GetType().GetProperty(name)!;
            var descriptor = TypeDescriptor.GetProperties(model)[name]!;
            var item = new JsonObject { ["description"] = descriptor.DisplayName + ". " + descriptor.Description };
            if (property.PropertyType.IsEnum) { item["type"] = "string"; item["enum"] = new JsonArray(Enum.GetNames(property.PropertyType).Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()); }
            else if (property.PropertyType == typeof(bool)) item["type"] = "boolean";
            else if (property.PropertyType == typeof(int)) { item["type"] = "integer"; item["minimum"] = 0; }
            else { item["type"] = "string"; if (property.PropertyType == typeof(Color)) item["pattern"] = "^#[0-9A-F]{8}$"; }
            if (name == "PeriodicHoldoffMinutes") item["maximum"] = 1999999999;
            properties[name] = item;
        }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = new JsonObject
        {
            ["general"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties },
            ["hardLinks"] = new JsonObject { ["type"] = "array", ["maxItems"] = 10000, ["items"] = new JsonObject
            { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("Name", "IpAddress", "IsEnabled"), ["properties"] = new JsonObject
                { ["Name"] = new JsonObject { ["type"] = "string" }, ["IpAddress"] = new JsonObject { ["type"] = "string", ["format"] = "ipv4" }, ["IsEnabled"] = new JsonObject { ["type"] = "boolean" } } } }
        } };
    }
}
