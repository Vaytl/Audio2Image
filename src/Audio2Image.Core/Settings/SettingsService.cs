using System.Text.Json;

namespace Audio2Image.Core.Settings;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Get the default settings file path (next to the executable).
    /// </summary>
    public static string GetDefaultPath()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(exeDir, "settings.json");
    }

    /// <summary>
    /// Get the default library path (a "library" folder next to the executable).
    /// </summary>
    public static string GetDefaultLibraryPath()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(exeDir, "library");
    }

    /// <summary>
    /// Load settings from a JSON file. Returns defaults if file doesn't exist.
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= GetDefaultPath();

        if (!File.Exists(path))
        {
            var defaults = CreateDefaults();
            Save(defaults, path);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? CreateDefaults();
        }
        catch
        {
            return CreateDefaults();
        }
    }

    /// <summary>
    /// Save settings to a JSON file.
    /// </summary>
    public static void Save(AppSettings settings, string? path = null)
    {
        path ??= GetDefaultPath();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static AppSettings CreateDefaults()
    {
        return new AppSettings
        {
            LibraryPath = GetDefaultLibraryPath(),
            DatabasePath = Path.Combine(GetDefaultLibraryPath(), "audio2image.db")
        };
    }
}
