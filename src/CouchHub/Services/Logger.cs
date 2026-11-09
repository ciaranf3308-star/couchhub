using System.IO;

namespace CouchHub.Services;

/// <summary>
/// Tiny file logger. One file per day under
/// %LOCALAPPDATA%\CouchHub\logs\couchhub-YYYY-MM-DD.log.
/// Never log launch arguments, tokens or credentials — callers must
/// not pass them in; launch logging records only "args present: yes/no".
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string? _logDirectory;

    public static string LogDirectory
    {
        get
        {
            _logDirectory ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CouchHub", "logs");
            return _logDirectory;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                string path = Path.Combine(LogDirectory,
                    $"couchhub-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the launcher.
        }
    }

    /// <summary>Delete log files older than 14 days. Best effort, called at startup.</summary>
    public static void PruneOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            foreach (string file in Directory.GetFiles(LogDirectory, "couchhub-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                    File.Delete(file);
            }
        }
        catch { /* best effort */ }
    }
}
