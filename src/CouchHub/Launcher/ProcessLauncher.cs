using System.Diagnostics;
using System.IO;
using CouchHub.Config;
using CouchHub.Services;

namespace CouchHub.Launcher;

/// <summary>Outcome of a launch attempt, for friendly user-facing errors.</summary>
public sealed record LaunchResult(bool Success, string? ErrorMessage, Process? Process);

/// <summary>
/// Generic launcher: validates a LaunchTarget, avoids duplicate
/// instances where practical, launches, and hands the window back.
/// Deliberately NOT a process supervisor: a short-lived launcher
/// (Steam, a .lnk, a store app) exiting does not mean the real app
/// exited, so CouchHub never auto-restores on process exit. Return
/// home is always explicit (hotkey, tray, or View+Menu hold).
/// </summary>
public sealed class ProcessLauncher
{
    /// <summary>
    /// True for targets Windows resolves itself rather than a file on
    /// disk: a URI (ms-windows-store://, https://, …) or a Store-app
    /// activation string such as
    /// "shell:AppsFolder\&lt;PackageFamilyName&gt;!App".
    /// This is how a Microsoft Store app (HotPlayer) is launched — it
    /// has no .exe path to point at, so File.Exists must never be
    /// used to validate it. The PackageFamilyName / App id are
    /// install-specific and live only in config.json, never in source.
    /// </summary>
    public static bool IsShellTarget(string path)
    {
        string trimmed = path.Trim();
        if (trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return true;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme is "http" or "https" or "ms-windows-store" or "ms-settings" ||
             trimmed.Contains("://", StringComparison.Ordinal));
    }

    /// <summary>
    /// Resolve a target to something launchable, or explain why not.
    /// Accepts: an existing .exe/.bat/.cmd/.lnk, or a shell target
    /// (a URI, or a shell:AppsFolder Store-app target). Nothing else.
    /// </summary>
    public static bool TryResolve(LaunchTarget target, out string error)
    {
        error = string.Empty;
        if (!target.IsConfigured)
        {
            error = $"{target.DisplayName} is not set up yet. Open Settings to choose an application.";
            return false;
        }
        string path = target.ExePath.Trim();
        if (IsShellTarget(path))
            return true; // shell/URI/Store target — Windows resolves it at launch
        if (File.Exists(path)) return true;
        error = $"Could not find {target.DisplayName}: \"{path}\". Check the path in Settings.";
        return false;
    }

    /// <summary>
    /// If an app with the same exe name is already running, bring its
    /// main window forward instead of starting a second copy.
    /// Returns true when an existing instance was focused.
    /// </summary>
    public static bool TryFocusExisting(LaunchTarget target)
    {
        // Store/URI targets have no file to match a process name against,
        // and need none: activating a Store app that is already running
        // brings its existing window forward natively, it does not
        // start a second copy. So there is nothing to focus here.
        if (IsShellTarget(target.ExePath)) return false;
        try
        {
            string path = target.ExePath.Trim();
            if (!File.Exists(path)) return false;
            string name = Path.GetFileNameWithoutExtension(path);
            foreach (Process p in Process.GetProcessesByName(name))
            {
                IntPtr hWnd = WindowHelper.MainWindowHandle(p);
                if (hWnd != IntPtr.Zero && WindowHelper.BringToFront(hWnd))
                {
                    Logger.Info($"Focused already-running {target.DisplayName} (pid {p.Id}).");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Existing-instance check failed: {ex.GetType().Name}");
        }
        return false;
    }

    public LaunchResult Launch(LaunchTarget target)
    {
        if (!TryResolve(target, out string error))
            return new LaunchResult(false, error, null);

        if (TryFocusExisting(target))
            return new LaunchResult(true, null, null); // already running; nothing new to track

        try
        {
            // Shell/URI/Store targets are not files: no working directory
            // (Path.GetFullPath on one is meaningless) and no arguments
            // (they are not passed through shell activation).
            bool isShellTarget = IsShellTarget(target.ExePath);
            var start = new ProcessStartInfo
            {
                FileName = target.ExePath.Trim(),
                UseShellExecute = true // needed for .lnk / URI / Store-app targets
            };
            if (!isShellTarget)
            {
                // Arguments are never logged verbatim (they could contain tokens).
                if (!string.IsNullOrWhiteSpace(target.Arguments))
                    start.Arguments = target.Arguments;

                string workingDir = string.IsNullOrWhiteSpace(target.WorkingDirectory)
                    ? Path.GetDirectoryName(Path.GetFullPath(target.ExePath.Trim()))
                    : target.WorkingDirectory;
                if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
                    start.WorkingDirectory = workingDir;
            }

            // Process.Start can return null for shell/URI targets even on success.
            Process? process = Process.Start(start);
            Logger.Info($"{target.DisplayName} launched (args present: " +
                        $"{!string.IsNullOrWhiteSpace(target.Arguments)}).");

            // Best-effort fullscreen: give the app a moment to create its
            // window, then ask Windows to maximise it. Fire-and-forget on
            // a background task; never block the dashboard.
            // Shell/Store targets return no Process (process is null),
            // so there is nothing to maximise here — a Store app such as
            // HotPlayer owns its own window state and must be maximised
            // in the app itself. Do not invent window-hunting for it.
            if (target.FullscreenPreferred && process is not null)
            {
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++) // up to ~5 s
                    {
                        await Task.Delay(250).ConfigureAwait(false);
                        IntPtr hWnd = WindowHelper.MainWindowHandle(process);
                        if (hWnd != IntPtr.Zero)
                        {
                            WindowHelper.MaximiseIfPossible(hWnd);
                            WindowHelper.BringToFront(hWnd);
                            break;
                        }
                        try
                        {
                            if (process.HasExited) break;
                        }
                        catch { break; }
                    }
                });
            }
            return new LaunchResult(true, null, process);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to launch {target.DisplayName}", ex);
            return new LaunchResult(false,
                $"Could not start {target.DisplayName}. {ex.Message}", null);
        }
    }
}
