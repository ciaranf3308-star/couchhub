using System.IO;
using System.Text.Json;
using CouchHub.Services;

namespace CouchHub.Config;

/// <summary>
/// Loads / saves %AppData%\CouchHub\config.json (Roaming, since v0.5.1:
/// Velopack replaces the whole install folder on reinstall, so a config
/// in LocalAppData would not survive — Roaming does). On first run after
/// the move, a config still sitting in %LOCALAPPDATA%\CouchHub is copied
/// over once (migration) and the old file is left in place.
/// Rules: absent file -> create defaults; malformed file -> back it up,
/// return defaults, never crash, and let the UI tell the user.
/// Logs stay in %LOCALAPPDATA%\CouchHub\logs — only the config moves.
/// </summary>
public sealed class ConfigService
{
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CouchHub");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    // Pre-v0.5.1 location, for the one-time migration in Load().
    private static string LegacyConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CouchHub", "config.json");

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
            MigrateLegacyConfigOnce();
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
            config.Epg ??= new EpgSettings();
            if (string.IsNullOrWhiteSpace(config.Epg.FeedUrl))
                config.Epg.FeedUrl = EpgSettings.IrelandFeedUrl;
            if (string.IsNullOrWhiteSpace(config.Epg.ChannelId))
                config.Epg.ChannelId = "RTÉOne.ie";
            if (string.IsNullOrWhiteSpace(config.Epg.ChannelName))
                config.Epg.ChannelName = "RTÉ One";
            config.LastFocusedTile = Math.Clamp(config.LastFocusedTile, 0, 3);
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

    /// <summary>One-time move from the pre-v0.5.1 LocalAppData location:
    /// if the Roaming config does not exist yet but the old one does,
    /// copy it over. The old file is left in place (harmless, and a
    /// rollback to 0.5.0 would still find it). Never throws.</summary>
    private static void MigrateLegacyConfigOnce()
    {
        try
        {
            if (File.Exists(ConfigPath) || !File.Exists(LegacyConfigPath)) return;
            File.Copy(LegacyConfigPath, ConfigPath);
            Logger.Info("Config migrated from %LOCALAPPDATA%\\CouchHub to %AppData%\\CouchHub.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Config migration skipped: {ex.GetType().Name}");
        }
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
