using System.IO;
using System.Text.Json;
using CouchHub.Services;

namespace CouchHub.Config;

/// <summary>
/// Loads / saves %LOCALAPPDATA%\CouchHub\config.json.
/// Rules: absent file -> create defaults; malformed file -> back it up,
/// return defaults, never crash, and let the UI tell the user.
/// </summary>
public sealed class ConfigService
{
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CouchHub");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public AppConfig Load()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(ConfigPath))
            {
                var fresh = new AppConfig();
                Save(fresh);
                return fresh;
            }

            string json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null)
                throw new JsonException("Config deserialised to null.");

            // Normalise: a hand-edited file may leave a target null.
            config.Gaming ??= new LaunchTarget { DisplayName = "Gaming" };
            config.Tv ??= new LaunchTarget { DisplayName = "TV" };
            config.YouTube ??= new LaunchTarget { DisplayName = "YouTube" };
            config.LastFocusedTile = Math.Clamp(config.LastFocusedTile, 0, 3);
            if (config.Theme != "Hotel" && config.Theme != "Bedroom")
                config.Theme = "Bedroom";
            return config;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Warn($"Config unreadable, recovering with defaults: {ex.GetType().Name}");
            TryPreserveCorruptFile();
            var defaults = new AppConfig { RecoveredFromMalformedFile = true };
            try { Save(defaults); } catch { /* defaults in memory are enough */ }
            return defaults;
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(config, JsonOptions);

        // Write atomically-ish: temp file then replace, so a crash
        // mid-save cannot leave a half-written config.json.
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ConfigPath, overwrite: true);
    }

    private static void TryPreserveCorruptFile()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            string backup = ConfigPath + ".corrupt-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".bak";
            File.Move(ConfigPath, backup);
            Logger.Info($"Preserved malformed config as {Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not preserve malformed config: {ex.GetType().Name}");
        }
    }
}
