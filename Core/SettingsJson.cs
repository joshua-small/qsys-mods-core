using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QsysMods.Core;

/// <summary>UI-thread settings projection. Validation may normalize a candidate, never live state.</summary>
public interface ISettingsTarget
{
    JsonObject Capture();
    void Validate(JsonObject document);
    void Apply(JsonObject document);
    void Restore(JsonObject document) => Apply(document);
}

public static class SettingsJson
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly JsonSerializerOptions Format = new() { WriteIndented = true };
    public static JsonObject Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException("settings.json exceeds 2 MiB.");
        using var parsed = JsonDocument.Parse(text, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
        CheckDuplicates(parsed.RootElement);
        var root = JsonNode.Parse(parsed.RootElement.GetRawText(), documentOptions: new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 }) as JsonObject ?? throw new InvalidDataException("Expected a settings object.");
        foreach (var key in root.Select(p => p.Key))
            if (key is not ("$schema" or "schemaVersion" or "designerVersion" or "qsys" or "mods"))
                throw new InvalidDataException("Unknown settings field: " + key);
        if (root["schemaVersion"]?.GetValue<int>() != 1 || root["designerVersion"]?.GetValue<string>() != "10.5.0.0")
            throw new InvalidDataException("Unsupported settings format or Designer version.");
        if (root["qsys"] is not JsonObject || root["mods"] is not JsonObject) throw new InvalidDataException("qsys and mods must be objects.");
        root["$schema"] = "./settings.schema.json";
        return root;
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in element.EnumerateObject())
            { if (!keys.Add(p.Name)) throw new InvalidDataException("Duplicate settings key: " + p.Name); CheckDuplicates(p.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) CheckDuplicates(value);
    }
    // Omitted properties retain their current values. Arrays replace as a unit;
    // an explicit empty array clears a list. Null is validated by the owner.
    public static JsonObject Merge(JsonObject current, JsonObject patch)
    {
        var result = (JsonObject)current.DeepClone();
        foreach (var pair in patch)
            result[pair.Key] = pair.Value is JsonObject value && result[pair.Key] is JsonObject old
                ? Merge(old, value) : pair.Value?.DeepClone();
        return result;
    }
    public static JsonObject MergeDocument(JsonObject current, JsonObject patch)
    {
        var result = Merge(current, patch);
        // A supplied mod section is a complete typed value. Replacing it permits
        // clearing shortcut dictionaries and resetting empty collections.
        foreach (var pair in (JsonObject)patch["mods"]!)
            ((JsonObject)result["mods"]!)[pair.Key] = pair.Value?.DeepClone();
        return result;
    }
    public static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);
    public static string Text(JsonObject value) => value.ToJsonString(Format) + Environment.NewLine;
    public static string? Read(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new InvalidDataException("settings.json exceeds 2 MiB.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static void Write(string path, string text, string? expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var locked = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Read(path) != expected) throw new IOException("Settings changed while being saved; retry after the editor finishes saving.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            { using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true); writer.Write(text); writer.Flush(); stream.Flush(true); }
            // Recheck after writing the temporary file, before replacing the editor's file.
            if (Read(path) != expected) throw new IOException("Settings changed during save; the editor's file was preserved.");
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous", true); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

/// <summary>Call Poll on the host UI thread. Two identical reads debounce editor saves.</summary>
public sealed class SettingsSynchronizer : IDisposable
{
    private readonly string path;
    private readonly ISettingsTarget target;
    private readonly FileStream lease;
    private JsonObject baseline;
    private string? acceptedText, candidate;
    private bool pending, rejected;
    private string? rejectedText;
    public string? Error { get; private set; }
    public bool Faulted { get; private set; }
    public event Action? Changed;
    public SettingsSynchronizer(string path, ISettingsTarget target)
    {
        this.path = path; this.target = target;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lease = new FileStream(path + ".session.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            baseline = target.Capture();
            if (!File.Exists(path))
            {
                acceptedText = SettingsJson.Text(baseline); SettingsJson.Write(path, acceptedText, null);
                SaveGood(baseline);
            }
            // An existing file is applied only after stable reads and validation.
        }
        catch { lease.Dispose(); throw; }
    }
    public void Poll()
    {
        if (Faulted) return;
        string? text = null;
        try
        {
            text = SettingsJson.Read(path);
            if (rejected && text == rejectedText) return;
            rejected = false;
            var live = target.Capture();
            if (text != acceptedText)
            {
                if (!pending || text != candidate) { candidate = text; pending = true; return; }
                if (text is null) throw new InvalidDataException("settings.json was removed. Restore it or use Open Settings JSON to recreate it.");
                var patch = SettingsJson.Parse(text);
                var next = SettingsJson.MergeDocument(live, patch);
                // Never pick a winner between a native dialog and an external editor.
                if (!SettingsJson.Same(live, baseline))
                    throw new IOException("Preferences and settings.json both changed. Keep the desired values in settings.json, then use Retry Settings JSON.");
                target.Validate(next); // Validate every provider before applying any provider.
                if (!SettingsJson.Same(next, live))
                {
                    SaveGood(live); // Recovery snapshot exists before the first side effect.
                    try
                    {
                        target.Apply(next);
                        var applied = target.Capture();
                        if (!SettingsJson.Same(applied, next)) throw new IOException("A settings provider did not retain the requested values.");
                        SaveGood(applied);
                    }
                    catch (Exception failure)
                    {
                        try { target.Restore(live); SaveGood(live); }
                        catch (Exception rollback)
                        { Faulted = true; throw new AggregateException("Settings application and recovery failed. Live sync is stopped; reopen Designer after reviewing settings.last-good.json.", failure, rollback); }
                        throw new InvalidOperationException("Settings were not applied; the previous values were restored. " + failure.GetBaseException().Message, failure);
                    }
                }
                else SaveGood(live); // Also repair recovery for an unchanged startup file.
                baseline = target.Capture(); acceptedText = text; pending = false;
                SetError(null); Changed?.Invoke();
            }
            else if (!SettingsJson.Same(live, baseline))
            {
                target.Validate(live);
                var output = SettingsJson.Text(live);
                SettingsJson.Write(path, output, acceptedText); SaveGood(live);
                acceptedText = output; baseline = live; pending = false; SetError(null); Changed?.Invoke();
            }
            else { pending = false; SetError(null); }
        }
        catch (Exception ex) { rejected = true; rejectedText = text; SetError(ex.Message); }
    }
    // Explicit local action acknowledges a simultaneous native edit. Still validates
    // the complete file before applying; it never silently chooses on timeout.
    public void Retry() { if (Faulted) throw new InvalidOperationException(Error); baseline = target.Capture(); candidate = null; pending = false; rejected = false; SetError(null); }
    public void Recreate()
    {
        if (File.Exists(path)) return;
        if (Faulted) throw new InvalidOperationException(Error);
        var live = target.Capture(); var text = SettingsJson.Text(live);
        SettingsJson.Write(path, text, null); SaveGood(live); baseline = live; acceptedText = text; pending = false; rejected = false; SetError(null);
    }
    private void SaveGood(JsonObject document)
    {
        var good = Path.Combine(Path.GetDirectoryName(path)!, "settings.last-good.json");
        SettingsJson.Write(good, SettingsJson.Text(document), SettingsJson.Read(good));
    }
    private void SetError(string? error) { if (Error != error) { Error = error; Changed?.Invoke(); } }
    public void Dispose() => lease.Dispose();
}
