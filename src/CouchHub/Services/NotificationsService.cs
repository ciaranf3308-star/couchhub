using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CouchHub.Config;
using CouchHub.Launcher;
using CouchHub.Services;

namespace CouchHub.Services;

/// <summary>One new-episode notification, as written by the TV app.</summary>
public sealed class SeriesNotification
{
    /// <summary>Opaque series id the TV app understands (never displayed as-is in a shell).</summary>
    public required string SeriesId { get; init; }
    public string SeriesTitle { get; init; } = string.Empty;
    /// <summary>Season number, or null when the TV app did not report one.</summary>
    public int? Season { get; init; }
    /// <summary>Episode number, or null when the TV app did not report one.</summary>
    public int? Episode { get; init; }
    public string EpisodeTitle { get; init; } = string.Empty;
    public DateTime? DetectedAtUtc { get; init; }
    public string PosterUrl { get; init; } = string.Empty;
}

/// <summary>
/// A parsed notifications.json. <see cref="IsEmpty"/> covers every
/// failure mode: the file is read defensively and a bad file is
/// indistinguishable from "no notifications" by design — the
/// dashboard never shows an error for a file it does not own.
/// </summary>
public sealed class NotificationsSnapshot
{
    public static readonly NotificationsSnapshot Empty = new()
    {
        UnreadCount = 0,
        Notifications = Array.Empty<SeriesNotification>()
    };

    public int UnreadCount { get; init; }
    public IReadOnlyList<SeriesNotification> Notifications { get; init; } = Array.Empty<SeriesNotification>();
    public DateTime? LastCheckUtc { get; init; }
}

/// <summary>
/// Followed-series notifications (v0.7.0), launcher side of the
/// contract with RedSilk TV. The TV app owns
/// %LOCALAPPDATA%\RedSilk\integration\notifications.json and updates
/// it atomically; this service only ever READS it:
///   {"schemaVersion":1,"lastCheck":"&lt;ISO-8601 UTC&gt;","unreadCount":n,
///    "notifications":[{"seriesId":"...","seriesTitle":"...","season":n,
///    "episode":n,"episodeTitle":"...","detectedAt":"&lt;ISO-8601 UTC&gt;",
///    "posterUrl":"..."}]}
/// Defensive by design: absent file, malformed JSON, wrong
/// schemaVersion, missing fields and extra unknown fields are all
/// tolerated — a bad file logs once and reads as zero notifications.
/// Never expects passwords or stream URLs in the file, and never
/// logs a series id or title verbatim (ids are opaque; titles are
/// user content). No polling: a FileSystemWatcher on the integration
/// folder refreshes the snapshot, debounced so one atomic rewrite
/// (which fires several change events) refreshes once.
/// </summary>
public sealed class NotificationsService : IDisposable
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Folder the TV app writes into; the file lives here.</summary>
    public static string IntegrationDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RedSilk", "integration");

    public static string NotificationsPath => Path.Combine(IntegrationDirectory, "notifications.json");

    // The TV app writes a small file; anything far bigger is not
    // our contract and is ignored rather than parsed.
    private const int MaxFileBytes = 1024 * 1024;
    private const int MaxNotifications = 50;
    private const int MaxSeriesIdLength = 128;
    private const int MaxTitleLength = 512;

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounceCts;
    private bool _disposed;

    /// <summary>Raised (on a thread-pool thread) when a re-read produced a new snapshot.</summary>
    public event Action<NotificationsSnapshot>? Changed;

    public NotificationsSnapshot Current { get; private set; } = NotificationsSnapshot.Empty;

    /// <summary>
    /// Read the file once and start watching the folder. Safe to call
    /// when the folder does not exist yet — the watcher is created
    /// anyway (FileSystemWatcher tolerates a missing path only after
    /// it exists, so creation is retried lazily on each refresh).
    /// Never throws.
    /// </summary>
    public void Start()
    {
        Current = ReadSnapshot();
        try
        {
            Directory.CreateDirectory(IntegrationDirectory);
            _watcher = new FileSystemWatcher(IntegrationDirectory, "notifications.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => OnFileChanged();
            _watcher.Created += (_, _) => OnFileChanged();
            _watcher.Renamed += (_, _) => OnFileChanged();
            _watcher.Deleted += (_, _) => OnFileChanged();
            _watcher.Error += (_, e) =>
                Logger.Warn($"Notifications watcher error: {e.GetException()?.GetType().Name ?? "unknown"}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Watching is best-effort: the dashboard still shows the
            // startup read, and ShowDashboard re-reads on return home.
            Logger.Warn($"Notifications watcher could not start: {ex.GetType().Name}");
            _watcher = null;
        }
    }

    /// <summary>Re-read the file now (startup, return home). Never throws.</summary>
    public NotificationsSnapshot Refresh()
    {
        var snapshot = ReadSnapshot();
        Current = snapshot;
        return snapshot;
    }

    private void OnFileChanged()
    {
        // Debounce: an atomic rewrite fires Changed several times in
        // a burst, and the file may still be mid-write on the first
        // event. Wait 500 ms of quiet, then re-read once. A newer
        // burst cancels the pending read.
        CancellationTokenSource? previous;
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed) return;
            previous = _debounceCts;
            _debounceCts = new CancellationTokenSource();
            cts = _debounceCts;
        }
        previous?.Cancel();
        previous?.Dispose();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            if (_disposed) return;
            var snapshot = ReadSnapshot();
            Current = snapshot;
            try { Changed?.Invoke(snapshot); }
            catch (Exception ex)
            {
                Logger.Warn($"Notifications change handler failed: {ex.GetType().Name}");
            }
        });
    }

    /// <summary>
    /// Parse the file defensively. Any failure — absent, unreadable,
    /// too large, malformed, wrong schema — returns
    /// <see cref="NotificationsSnapshot.Empty"/> and logs only the
    /// failure kind, never file content. Never throws.
    /// </summary>
    public static NotificationsSnapshot ReadSnapshot()
    {
        string path = NotificationsPath;
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return NotificationsSnapshot.Empty;
            if (info.Length > MaxFileBytes)
            {
                Logger.Warn("Notifications file too large; ignoring.");
                return NotificationsSnapshot.Empty;
            }
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn($"Notifications file unreadable: {ex.GetType().Name}");
            return NotificationsSnapshot.Empty;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            Logger.Warn("Notifications file is malformed JSON; ignoring.");
            return NotificationsSnapshot.Empty;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Logger.Warn("Notifications file is not a JSON object; ignoring.");
                return NotificationsSnapshot.Empty;
            }
            if (!root.TryGetProperty("schemaVersion", out var schemaEl) ||
                schemaEl.ValueKind != JsonValueKind.Number ||
                !schemaEl.TryGetInt32(out int schemaVersion) ||
                schemaVersion != CurrentSchemaVersion)
            {
                Logger.Warn("Notifications file has an unsupported schemaVersion; ignoring.");
                return NotificationsSnapshot.Empty;
            }

            int unread = 0;
            if (root.TryGetProperty("unreadCount", out var unreadEl) &&
                unreadEl.ValueKind == JsonValueKind.Number &&
                unreadEl.TryGetInt32(out int parsedUnread))
                unread = Math.Clamp(parsedUnread, 0, MaxNotifications);

            DateTime? lastCheck = null;
            if (root.TryGetProperty("lastCheck", out var lastCheckEl) &&
                lastCheckEl.ValueKind == JsonValueKind.String &&
                TryParseUtc(lastCheckEl.GetString(), out DateTime lastCheckUtc))
                lastCheck = lastCheckUtc;

            var items = new List<SeriesNotification>();
            if (root.TryGetProperty("notifications", out var listEl) &&
                listEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in listEl.EnumerateArray())
                {
                    if (items.Count >= MaxNotifications) break;
                    var item = ParseNotification(el);
                    if (item is not null) items.Add(item);
                }
            }

            // Trust the list over the count when they disagree: the
            // badge must never claim more than the view can show.
            if (items.Count == 0) unread = 0;
            else if (unread == 0) unread = items.Count;
            else unread = Math.Min(unread, items.Count);

            return new NotificationsSnapshot
            {
                UnreadCount = unread,
                Notifications = items,
                LastCheckUtc = lastCheck
            };
        }
    }

    private static SeriesNotification? ParseNotification(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        string? seriesId = GetString(el, "seriesId");
        if (string.IsNullOrWhiteSpace(seriesId)) return null; // unusable without an id
        seriesId = seriesId.Trim();
        if (seriesId.Length > MaxSeriesIdLength) return null;

        int? season = GetInt(el, "season");
        int? episode = GetInt(el, "episode");
        // A season/episode outside any sane range is bad data, not a
        // deep link — drop the numbers, keep the notification.
        if (season is < 0 or > 999) season = null;
        if (episode is < 0 or > 999) episode = null;

        DateTime? detectedAt = null;
        if (TryParseUtc(GetString(el, "detectedAt"), out DateTime detectedUtc))
            detectedAt = detectedUtc;

        return new SeriesNotification
        {
            SeriesId = seriesId,
            SeriesTitle = Truncate(GetString(el, "seriesTitle") ?? string.Empty),
            Season = season,
            Episode = episode,
            EpisodeTitle = Truncate(GetString(el, "episodeTitle") ?? string.Empty),
            DetectedAtUtc = detectedAt,
            PosterUrl = Truncate(GetString(el, "posterUrl") ?? string.Empty)
        };
    }

    private static string Truncate(string value) =>
        value.Length > MaxTitleLength ? value[..MaxTitleLength] : value;

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() : null;

    private static int? GetInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number &&
        prop.TryGetInt32(out int value) ? value : null;

    private static bool TryParseUtc(string? value, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTime.TryParse(value, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTime parsed))
            return false;
        utc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        return true;
    }

    // ----- Deep link ---------------------------------------------------------

    /// <summary>
    /// Build the TV app's deep-link arguments for one notification:
    /// `--open-series &lt;seriesId&gt; [--episode &lt;season&gt;:&lt;episode&gt;]`
    /// (episode omitted when unknown). The series id is allowlisted,
    /// never passed through raw: it must be a single token of letters,
    /// digits and - _ . : with no leading dash (so it can never be
    /// parsed as another flag), no whitespace, no quotes, no shell
    /// metacharacters. Season/episode are numbers only, and the
    /// episode flag needs both. Returns false (with a plain reason)
    /// when the notification cannot be deep-linked safely.
    /// </summary>
    public static bool TryBuildDeepLinkArguments(SeriesNotification notification, out string arguments, out string error)
    {
        arguments = string.Empty;
        error = string.Empty;
        string id = notification.SeriesId.Trim();
        if (id.Length == 0 || id.Length > MaxSeriesIdLength || id[0] == '-' ||
            id.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':')))
        {
            error = "That notification cannot be opened — its series id is not a safe deep-link target.";
            return false;
        }
        arguments = $"--open-series {id}";
        if (notification.Season is not null && notification.Episode is not null)
            arguments += $" --episode {notification.Season.Value}:{notification.Episode.Value}";
        return true;
    }

    /// <summary>
    /// Launch the configured TV app at one notification. Resolves the
    /// TV exe exactly the way the TV tile does (config target), but
    /// deep links need a real exe: a Store-app / shell target
    /// (HotPlayer) cannot take command-line arguments, so that case
    /// gets a plain error instead of a launch. Single-instance is the
    /// TV app's job — it forwards the deep link to the running copy;
    /// this method just launches it. Never throws; failures return a
    /// plain user-facing message. Arguments are never logged.
    /// </summary>
    public static bool TryLaunchDeepLink(LaunchTarget tvTarget, SeriesNotification notification, out string error)
    {
        error = string.Empty;
        if (!tvTarget.IsConfigured)
        {
            error = "TV is not set up yet. Open Settings to choose the TV app.";
            return false;
        }
        if (!TryBuildDeepLinkArguments(notification, out string arguments, out string argError))
        {
            error = argError;
            return false;
        }
        if (ProcessLauncher.IsShellTarget(tvTarget.ExePath))
        {
            error = "The TV tile is set to a Store app, which cannot take a series link. " +
                    "Point the TV tile at the RedSilk TV app in Settings to open episodes directly.";
            return false;
        }
        if (!File.Exists(tvTarget.ExePath.Trim()))
        {
            error = $"Could not find TV: \"{tvTarget.ExePath.Trim()}\". Check the path in Settings.";
            return false;
        }
        try
        {
            string workingDir = string.IsNullOrWhiteSpace(tvTarget.WorkingDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(tvTarget.ExePath.Trim())) ?? string.Empty
                : tvTarget.WorkingDirectory;
            var start = new ProcessStartInfo
            {
                FileName = tvTarget.ExePath.Trim(),
                Arguments = arguments,
                UseShellExecute = true
            };
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
                start.WorkingDirectory = workingDir;
            Process? process = Process.Start(start);
            Logger.Info("TV deep link launched (series id not logged).");
            if (tvTarget.FullscreenPreferred && process is not null)
            {
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(250).ConfigureAwait(false);
                        IntPtr hWnd = WindowHelper.MainWindowHandle(process);
                        if (hWnd != IntPtr.Zero)
                        {
                            WindowHelper.MaximiseIfPossible(hWnd);
                            WindowHelper.BringToFront(hWnd);
                            break;
                        }
                        try { if (process.HasExited) break; } catch { break; }
                    }
                });
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to launch TV deep link", ex);
            error = $"Could not start TV. {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
