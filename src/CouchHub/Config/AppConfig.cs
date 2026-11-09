using System.Text.Json.Serialization;

namespace CouchHub.Config;

/// <summary>
/// One configurable launch target. The Windows tile is built in and
/// has no target — it simply hides CouchHub to expose the desktop.
/// </summary>
public sealed class LaunchTarget
{
    /// <summary>Display name shown on the tile.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Executable path, .lnk, or a shell target such as a URI.
    /// Empty means "not configured yet" — the tile shows Setup required.
    /// NEVER hardcode a real path here; Codex fills this in on the PC.
    /// </summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>Optional command-line arguments. Never put passwords/tokens here.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Optional working directory. Empty = the exe's own folder.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Best-effort request to maximise the launched app's main window.
    /// External apps own their own fullscreen behaviour; CouchHub cannot
    /// force it, it can only ask Windows to maximise the window.
    /// </summary>
    public bool FullscreenPreferred { get; set; } = true;

    /// <summary>Hide CouchHub while the external app runs (default true).</summary>
    public bool HideOnLaunch { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ExePath);
}

/// <summary>Whole-file configuration, stored as JSON (see ConfigService).</summary>
public sealed class AppConfig
{
    public LaunchTarget Gaming { get; set; } = new()
    {
        DisplayName = "Gaming",
        // Intentionally blank: ES-DE's real path is set by Codex on the PC.
        ExePath = string.Empty,
        FullscreenPreferred = true,
        HideOnLaunch = true
    };

    public LaunchTarget Tv { get; set; } = new()
    {
        DisplayName = "TV",
        // Intentionally blank: HotPlayer is a Microsoft Store app with
        // no .exe path. Codex sets this on the PC to its shell target,
        // e.g. "shell:AppsFolder\<PackageFamilyName>!App" (see
        // CODEX_HANDOFF.md for how to find the real value), or to a
        // .lnk shortcut to HotPlayer. Never hardcode it here.
        ExePath = string.Empty,
        FullscreenPreferred = true,
        HideOnLaunch = true
    };

    public LaunchTarget YouTube { get; set; } = new()
    {
        DisplayName = "YouTube",
        // Intentionally blank: FreeTube's real path is set by Codex on the PC.
        ExePath = string.Empty,
        FullscreenPreferred = true,
        HideOnLaunch = true
    };

    /// <summary>Start CouchHub fullscreen (borderless, maximised).</summary>
    public bool FullscreenDashboard { get; set; } = true;

    /// <summary>
    /// Dashboard theme: "Bedroom" (deep wine burgundy, beige bright —
    /// the bedroom) or "Hotel" (brass and warm charcoal). Applied at
    /// startup; unknown values fall back to Bedroom.
    /// </summary>
    public string Theme { get; set; } = "Bedroom";

    /// <summary>
    /// App-level scaling on top of Windows DPI scaling.
    /// "System" = no extra scaling; "125" / "150" / "200" = that percent.
    /// </summary>
    public string ScalingMode { get; set; } = "System";

    /// <summary>Launch CouchHub automatically at Windows sign-in (Startup folder).</summary>
    public bool StartAtSignIn { get; set; } = false;

    /// <summary>
    /// Return-home keyboard shortcut, stored as a string so it stays
    /// configurable without recompiling. Format: "Ctrl+Alt+Home".
    /// Parsed by WindowHelper; unknown values fall back to Ctrl+Alt+Home.
    /// </summary>
    public string ReturnHomeShortcut { get; set; } = "Ctrl+Alt+Home";

    /// <summary>Index of the tile focused on startup / return home (0-3).</summary>
    public int LastFocusedTile { get; set; } = 0;

    /// <summary>Set by ConfigService when the file was malformed and recovered.</summary>
    [JsonIgnore]
    public bool RecoveredFromMalformedFile { get; set; }
}
