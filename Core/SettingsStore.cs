using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QsysMods.Core;

/// <summary>One atomic settings document; unknown mod sections survive partial installations.</summary>
public sealed class SettingsStore
{
    public sealed record Document(int Schema, Dictionary<string, JsonElement> Mods);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string path;
    private Dictionary<string, JsonElement> values;
    private string? original;
    public SettingsStore(string path)
    {
        this.path = path;
        original = File.Exists(path) ? File.ReadAllText(path) : null;
        var data = original is null ? new Document(1, new()) : JsonSerializer.Deserialize<Document>(original, Json)
            ?? throw new InvalidDataException("Mod preferences are empty.");
        if (data.Schema != 1 || data.Mods is null) throw new InvalidDataException("Unsupported mod preferences format. The original file was preserved.");
        values = data.Mods;
    }
    public static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, "^[a-z][a-z0-9-]{1,63}$")) throw new ArgumentException("Invalid mod identifier.");
    }
    public T Read<T>(string id, Func<T> defaults)
    {
        ValidateId(id);
        return values.TryGetValue(id, out var saved) ? saved.Deserialize<T>(Json) ?? throw new InvalidDataException("Empty preferences for " + id) : defaults();
    }
    public void Save(IReadOnlyDictionary<string, object> changes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var ownership = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Refuse to overwrite settings changed by another Designer process/dialog.
        if ((File.Exists(path) ? File.ReadAllText(path) : null) != original)
            throw new IOException("Mod preferences changed in another Designer window. Close and reopen Preferences before saving.");
        var next = new Dictionary<string, JsonElement>(values);
        foreach (var pair in changes) { ValidateId(pair.Key); next[pair.Key] = JsonSerializer.SerializeToElement(pair.Value, pair.Value.GetType(), Json); }
        var text = JsonSerializer.Serialize(new Document(1, next), Json);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            { using var writer = new StreamWriter(output, leaveOpen: true); writer.Write(text); writer.Flush(); output.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous", true);
            else File.Move(temp, path);
            values = next; original = text;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
