using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace QsysMods.Core;

/// <summary>Reads the installation-specific opt-in written by Q-SYS Mod Manager.</summary>
internal static class HostCompatibility
{
    private const int StateLimit = 1024 * 1024;
    internal static bool Allows(string directory, string testedHost, string testedCommon)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string host = Hash(Path.Combine(root, "Q-Sys Designer.dll"));
            string common = Hash(Path.Combine(root, "ModuleCommon.dll"));
            if (host == testedHost && common == testedCommon) return true;
            string path = Path.Combine(root, ".qds-mod-manager", "state.json"); Guard(path);
            using var stream = File.OpenRead(path);
            if (stream.Length is <= 0 or > StateLimit) return false;
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            var state = document.RootElement;
            if (!Unique(state) || state.GetProperty("schema").GetInt32() != 2 || state.GetProperty("suspended").GetBoolean()) return false;
            var mods = state.GetProperty("mods");
            if (!Unique(mods) || !mods.TryGetProperty("mods-core", out var core) || !Unique(core)
                || core.GetProperty("id").GetString() != "mods-core" || !core.GetProperty("enabled").GetBoolean()) return false;
            var consent = state.GetProperty("untestedRuntime");
            if (!Unique(consent) || consent.GetProperty("schema").GetInt32() != 1) return false;
            string? target = consent.GetProperty("target").GetString();
            return target is not null && Path.IsPathFullyQualified(target)
                && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)), root, StringComparison.OrdinalIgnoreCase)
                && consent.GetProperty("hostSha256").GetString() == host
                && consent.GetProperty("commonSha256").GetString() == common;
        }
        catch { return false; } // Missing, unreadable or malformed consent never enables an untested runtime.
    }
    private static bool Unique(JsonElement element) => element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == element.EnumerateObject().Count();
    private static string Hash(string path)
    {
        Guard(path); using var stream = File.OpenRead(path);
        if (stream.Length == 0) throw new InvalidDataException("Empty Designer file.");
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static void Guard(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked compatibility paths are not supported.");
    }
}
