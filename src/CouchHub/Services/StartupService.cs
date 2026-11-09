using System.IO;
using CouchHub.Services;

namespace CouchHub.Services;

/// <summary>
/// Startup at Windows sign-in, user-level only: a shortcut in the
/// current user's Startup folder. No registry Run key, no admin,
/// no shell replacement. Fully reversible by deleting the shortcut.
/// </summary>
public sealed class StartupService
{
    private const string ShortcutName = "CouchHub.lnk";

    public static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static string ShortcutPath => Path.Combine(StartupFolder, ShortcutName);

    public bool IsEnabled => File.Exists(ShortcutPath);

    /// <summary>Enable or disable autostart. Returns false with a logged reason on failure.</summary>
    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                Directory.CreateDirectory(StartupFolder);
                string exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return false;
                CreateShortcut(ShortcutPath, exePath);
                Logger.Info("Startup shortcut created.");
            }
            else
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                Logger.Info("Startup shortcut removed.");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to change startup setting", ex);
            return false;
        }
    }

    /// <summary>
    /// Creates a .lnk via WScript.Shell late binding (no COM reference
    /// needed, no NuGet). Target is always this executable.
    /// </summary>
    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            throw new InvalidOperationException("WScript.Shell is unavailable on this system.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
                shortcut.Description = "CouchHub living-room launcher";
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Save();
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
