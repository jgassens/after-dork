using System.Text.Json;

namespace AfterDork;

// Shared settings store for all After Dork modules, the Windows twin of
// Shared/Settings.swift: one JSON file holding a dictionary per module,
// { "OrbitalBox": { "spin": 1.4, ... }, ... }. Savers read it straight from
// disk each time they start so a fresh activation always sees the latest
// values. Checks are stored as bools, integer sliders as integers, others
// as doubles, exactly like the Mac plist.

public static class Settings
{
    /// <summary>Environment variable that redirects the file (used by tests).</summary>
    public const string PathVariable = "AFTERDORK_SETTINGS_PATH";

    public static string FilePath =>
        Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } p
            ? p
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AfterDork", "settings.json");

    /// <summary>In-memory values that win over the file (the render harness's --set).</summary>
    public static readonly Dictionary<string, Dictionary<string, object>> Overrides = new();

    public static Dictionary<string, Dictionary<string, object>> AllSettings()
    {
        var result = new Dictionary<string, Dictionary<string, object>>();
        try
        {
            if (!File.Exists(FilePath)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(FilePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var module in doc.RootElement.EnumerateObject())
            {
                if (module.Value.ValueKind != JsonValueKind.Object) continue;
                var d = new Dictionary<string, object>();
                foreach (var kv in module.Value.EnumerateObject())
                {
                    switch (kv.Value.ValueKind)
                    {
                        case JsonValueKind.True: d[kv.Name] = true; break;
                        case JsonValueKind.False: d[kv.Name] = false; break;
                        case JsonValueKind.Number:
                            d[kv.Name] = kv.Value.TryGetInt64(out var i) ? (object)i : kv.Value.GetDouble();
                            break;
                    }
                }
                result[module.Name] = d;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A missing or half-written file means "use defaults", as on the Mac.
        }
        return result;
    }

    static object? Raw(string module, string key)
    {
        if (Overrides.TryGetValue(module, out var o) && o.TryGetValue(key, out var ov)) return ov;
        return AllSettings().TryGetValue(module, out var d) && d.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>AfterDork.value(module, key, def): Double or Int values; anything else → default.</summary>
    public static double Value(string module, string key, double def) => Raw(module, key) switch
    {
        double d => d,
        long l => l,
        int i => i,
        bool b => b ? 1 : 0,  // NSNumber bridging lets a Bool read as a number
        _ => def,
    };

    /// <summary>AfterDork.flag(module, key, def): Bool values (0/1 numbers bridge, as NSNumber does).</summary>
    public static bool Flag(string module, string key, bool def) => Raw(module, key) switch
    {
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        double d => d != 0,
        _ => def,
    };

    /// <summary>Writes the whole settings dictionary atomically.</summary>
    public static void Write(Dictionary<string, Dictionary<string, object>> settings)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true });
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
