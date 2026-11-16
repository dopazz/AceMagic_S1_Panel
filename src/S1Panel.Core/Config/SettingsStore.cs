using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace S1Panel.Core.Config;

/// <summary>
/// Loads and saves s1panel.json. Lookup order: an explicit path, then next to the exe, then
/// %APPDATA%\s1panel\. A missing file simply means "all defaults".
/// </summary>
public static class SettingsStore
{
    public const string FileName = "s1panel.json";

    public static string ExeDirPath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static string AppDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "s1panel", FileName);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, // catch typos in key names
    };

    /// <summary>Short names accepted by <see cref="Set"/> in addition to the JSON keys.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["interval"] = "intervalMs",
        ["update-gap"] = "updateGapMs",
        ["redraw-delay"] = "redrawDelayMs",
        ["text-size"] = "textSize",
        ["no-blink"] = "blink",
    };

    /// <summary>The settings file that would be read, or null if none exists.</summary>
    public static string? FindExisting(string? explicitPath = null)
    {
        if (explicitPath is not null)
            return explicitPath;
        if (File.Exists(ExeDirPath))
            return ExeDirPath;
        if (File.Exists(AppDataPath))
            return AppDataPath;
        return null;
    }

    /// <summary>Loads and validates settings. Returns defaults (and a null path) when there is no file.</summary>
    public static (PanelSettings Settings, string? Path) Load(string? explicitPath = null)
    {
        string? path = FindExisting(explicitPath);
        if (path is null)
            return (new PanelSettings(), null);
        if (!File.Exists(path))
            throw new ArgumentException($"Settings file not found: {path}");

        PanelSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<PanelSettings>(File.ReadAllText(path), Json) ?? new PanelSettings();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{path}: {ex.Message}", ex);
        }

        try { settings.Validate(); }
        catch (ArgumentException ex) { throw new ArgumentException($"{path}: {ex.Message}", ex); }
        return (settings, path);
    }

    /// <summary>
    /// Saves settings (validated) to <paramref name="path"/>, or to the existing file, or next to the exe —
    /// falling back to %APPDATA% when the exe's folder isn't writable. Returns the path written.
    /// </summary>
    public static string Save(PanelSettings settings, string? path = null)
    {
        settings.Validate();
        string json = Serialize(settings);
        string target = path ?? FindExisting() ?? ExeDirPath;
        try
        {
            WriteAtomically(target, json);
        }
        catch (UnauthorizedAccessException) when (path is null && target == ExeDirPath)
        {
            target = AppDataPath;
            WriteAtomically(target, json);
        }
        return target;
    }

    public static string Serialize(PanelSettings settings) => JsonSerializer.Serialize(settings, Json);

    /// <summary>
    /// Returns a copy of <paramref name="settings"/> with one key changed, validated.
    /// Keys are the JSON names (case-insensitive, kebab-case accepted), "led.mode"-style for nested values.
    /// "null"/"none" clears an optional value; setting any "led.*" key turns LED control on.
    /// </summary>
    public static PanelSettings Set(PanelSettings settings, string key, string value)
    {
        var root = JsonNode.Parse(Serialize(settings))!.AsObject();
        string[] parts = key.Split('.');
        JsonObject obj = root;

        for (int i = 0; i < parts.Length; i++)
        {
            string name = ResolveKey(obj, parts[i], parts.Length == 1 ? null : string.Join('.', parts[..i]));
            bool last = i == parts.Length - 1;
            if (!last)
            {
                if (obj[name] is not JsonObject child)
                {
                    // e.g. "led.mode" while led is null: start from the defaults
                    child = name == "led"
                        ? JsonNode.Parse(JsonSerializer.Serialize(new LedSettings(), Json))!.AsObject()
                        : throw new ArgumentException($"'{key}' is not a valid setting.");
                    obj[name] = child;
                }
                obj = child;
                continue;
            }

            JsonNode? current = obj[name];
            bool invert = parts[i].Equals("no-blink", StringComparison.OrdinalIgnoreCase);
            obj[name] = Convert(name, current, value, invert);
        }

        PanelSettings result;
        try
        {
            result = root.Deserialize<PanelSettings>(Json)!;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{key}: {ex.Message}", ex);
        }
        result.Validate();
        return result;
    }

    private static string ResolveKey(JsonObject obj, string raw, string? parent)
    {
        string name = Aliases.TryGetValue(raw, out var alias) ? alias : KebabToCamel(raw);
        string? match = obj.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            string where = parent is null ? "" : $" in '{parent}'";
            throw new ArgumentException($"Unknown setting '{raw}'{where}. Valid keys: {string.Join(", ", obj.Select(kv => kv.Key))}.");
        }
        return match;
    }

    private static JsonNode? Convert(string name, JsonNode? current, string value, bool invert)
    {
        string v = value.Trim();
        if (v.Equals("null", StringComparison.OrdinalIgnoreCase) || v.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        switch (current?.GetValueKind())
        {
            case JsonValueKind.True or JsonValueKind.False:
                bool? b = v.ToLowerInvariant() switch
                {
                    "true" or "on" or "yes" or "1" => true,
                    "false" or "off" or "no" or "0" => false,
                    _ => null,
                };
                if (b is null) throw new ArgumentException($"{name} must be true or false (got '{value}').");
                return JsonValue.Create(invert ? !b.Value : b.Value);
            case JsonValueKind.Number:
                if (!int.TryParse(v, out int n)) throw new ArgumentException($"{name} must be a whole number (got '{value}').");
                return JsonValue.Create(n);
            case JsonValueKind.Object:
                throw new ArgumentException($"'{name}' is a group; set one of its keys (e.g. {name}.mode) or 'none' to clear it.");
            default:
                return JsonValue.Create(v);
        }
    }

    private static string KebabToCamel(string s)
    {
        var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? s : parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static void WriteAtomically(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents + Environment.NewLine);
        File.Move(tmp, path, overwrite: true);
    }
}
