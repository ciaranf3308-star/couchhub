using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CouchHub.Services;

/// <summary>Outcome of a desktop-start attempt, for friendly user-facing errors.</summary>
public sealed record DesktopStartResult(bool Success, string? ErrorMessage);

/// <summary>
/// The Windows desktop (Explorer) as an on-demand session, for when
/// RedSilk itself is the Windows shell (Custom User Interface policy).
/// Winlogon launches RedSilk instead of Explorer at sign-in, so there
/// is no taskbar or desktop until RedSilk starts one.
///
/// This service owns exactly one thing: an Explorer desktop RedSilk
/// itself started. It never touches an Explorer the user started
/// independently — if RedSilk loses track of its own session (e.g. it
/// restarted), it leaves Explorer alone rather than guessing. And
/// exiting RedSilk deliberately never ends the desktop: if the shell
/// goes away while the desktop is up, the user still has a working
/// taskbar, which is the safe failure mode.
/// </summary>
public sealed class DesktopSession
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    private Process? _explorerProcess;

    /// <summary>True when RedSilk started a desktop this run and is still tracking it.</summary>
    public bool HasStartedDesktop => _explorerProcess is not null;

    /// <summary>
    /// True when the Explorer taskbar window exists — i.e. a desktop is
    /// up, whoever started it. The taskbar window class is
    /// "Shell_TrayWnd"; a stray explorer.exe running only as a file
    /// manager has no taskbar, so this is the reliable signal, not the
    /// process list.
    /// </summary>
    public static bool IsDesktopRunning()
    {
        try { return FindWindow("Shell_TrayWnd", null) != IntPtr.Zero; }
        catch { return false; }
    }

    /// <summary>
    /// Launch explorer.exe as the desktop. Call only when
    /// IsDesktopRunning() is false: launching Explorer while it is
    /// already the shell just opens a File Explorer window instead.
    /// On success the process is tracked so return-home can end the
    /// session. Never throws: failures come back as a result with a
    /// plain message.
    /// </summary>
    public DesktopStartResult StartDesktop()
    {
        if (HasStartedDesktop)
            return new DesktopStartResult(false, "A Windows desktop started by RedSilk is already running.");
        if (IsDesktopRunning())
            return new DesktopStartResult(false, "The Windows desktop is already running.");

        try
        {
            // No arguments, no working directory: explorer.exe is
            // resolved via PATH, and as the only Explorer instance it
            // becomes the desktop (taskbar + desktop icons).
            Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            });
            if (process is null)
            {
                // Explorer may have forwarded to an existing instance
                // (which has no taskbar, or we would not be here) and
                // produced no desktop to track.
                Logger.Warn("explorer.exe launch returned no process; no desktop tracked.");
                return new DesktopStartResult(false,
                    "Windows did not start (no Explorer process to track).");
            }
            _explorerProcess = process;
            Logger.Info($"Explorer desktop started (pid {process.Id}).");
            return new DesktopStartResult(true, null);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start Explorer desktop", ex);
            return new DesktopStartResult(false, $"Could not start Windows. {ex.Message}");
        }
    }

    /// <summary>
    /// End a desktop session RedSilk started: terminate ONLY the
    /// tracked Explorer process, so the taskbar/desktop go away. The
    /// tracked process is verified to still be explorer.exe before
    /// anything is killed, and a process that already exited is left
    /// alone. An Explorer RedSilk did not start is never touched.
    /// Best effort; never throws.
    /// </summary>
    public void EndDesktopSession()
    {
        Process? process = _explorerProcess;
        _explorerProcess = null;
        if (process is null) return;
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                Logger.Info("Tracked Explorer desktop already exited; nothing to end.");
                return;
            }
            // Never kill anything that is not explorer.exe, however we got here.
            if (!process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn($"Tracked desktop process is '{process.ProcessName}', not explorer — leaving it alone.");
                return;
            }
            int pid = process.Id;
            process.Kill();
            // Give the taskbar a moment to go away before RedSilk reappears.
            process.WaitForExit(2000);
            Logger.Info($"Explorer desktop ended (pid {pid}).");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Ending Explorer desktop failed (ignored): {ex.GetType().Name}");
        }
        finally
        {
            try { process.Dispose(); } catch { /* best effort */ }
        }
    }
}
