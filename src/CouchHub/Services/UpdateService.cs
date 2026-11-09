using Velopack;

namespace CouchHub.Services;

/// <summary>
/// Velopack auto-updates. The update feed is a static file host
/// (GitHub Pages): https://ciaranf3308-star.github.io/couchhub-updates/couchhub/
/// The UpdateManager is given the feed *directory* — Velopack appends
/// releases.win.json itself, so do not pass the file URL here.
/// CouchHub checks on startup and stages any update silently;
/// VelopackApp.Run() applies the staged update the next time the app
/// launches, so a running session is never interrupted or restarted.
/// Nothing here ever throws: no network, a portable (non-installed)
/// run, or any other failure is logged and the launcher carries on.
/// </summary>
public static class UpdateService
{
    // Feed directory, NOT the releases.win.json file: Velopack appends
    // that filename itself, and a full file URL would break the lookup.
    public const string FeedUrl = "https://ciaranf3308-star.github.io/couchhub-updates/couchhub/";

    public enum Outcome { NotInstalled, UpToDate, Downloaded, Failed }

    public record Result(Outcome Outcome, string Message);

    public static async Task<Result> CheckForUpdatesAsync()
    {
        try
        {
            using var manager = new UpdateManager(FeedUrl);
            if (!manager.IsInstalled)
            {
                Logger.Info("Update check skipped: RedSilk is running portable, not installed via Setup.exe.");
                return new Result(Outcome.NotInstalled,
                    "Not installed via Setup.exe — updates only flow for the installed copy.");
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Logger.Info("Update check: up to date.");
                return new Result(Outcome.UpToDate, "You're up to date.");
            }

            string version = update.TargetFullRelease.Version.ToString();
            Logger.Info($"Update check: v{version} available, downloading.");
            await manager.DownloadUpdatesAsync(update);
            Logger.Info($"Update v{version} downloaded — applies on next launch.");
            return new Result(Outcome.Downloaded,
                $"Update v{version} downloaded — applies on next launch.");
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            return new Result(Outcome.Failed,
                "Update check failed — see the log in %LOCALAPPDATA%\\CouchHub\\logs.");
        }
    }
}
