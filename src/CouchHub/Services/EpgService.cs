using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CouchHub.Config;

namespace CouchHub.Services;

/// <summary>One programme in the live guide (times are UTC).</summary>
public sealed class EpgProgramme
{
    public required string Title { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
}

/// <summary>Now / next / later for one channel. Null programmes mean
/// the guide had no data for that slot — the UI hides those rows
/// instead of inventing anything.</summary>
public sealed class EpgGuide
{
    public required string ChannelName { get; init; }
    public EpgProgramme? Now { get; init; }
    public EpgProgramme? Next { get; init; }
    public EpgProgramme? Later { get; init; }
    public required string Source { get; init; }
    public DateTime FetchedAtUtc { get; init; }
}

/// <summary>One channel from a guide file (for the Settings picker).</summary>
public sealed record EpgChannel(string Id, string Name);

/// <summary>
/// Live TV guide (v0.5). DEFAULT source: a free public XMLTV file
/// (open-epg Ireland / UK) — no account, no credentials. CouchHub
/// downloads the .gz, gunzips it, parses it with XDocument and shows
/// real now / next / later for the configured channel. The file is
/// cached locally so a failed download still shows the last good
/// guide while it is fresh, and the dashboard never blocks on it.
/// OPTIONAL override: if the user's own Xtream Codes provider is
/// configured in Settings, the provider's get_short_epg is tried
/// first (fallback: the provider's xmltv.php), and the free file is
/// the fallback if the provider fails.
/// Honesty rule: every failure returns null — the tile shows the LIVE
/// badge and channel name with the programme rows hidden. Nothing
/// here ever fabricates a title or a time.
/// Privacy: the Xtream password is only ever sent to the user's own
/// provider server, and nothing in this file logs a URL, a username
/// or a password — log lines carry only the host and the error type.
/// </summary>
public sealed class EpgService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static EpgService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("CouchHub/0.5.0 (Windows)");
    }

    // Verified live 2026-11-09. These are the only fallback URLs —
    // never guess a mirror: an unlisted host is not a source.
    private const string EpgTalkUk = "https://epgtalk.com/UK_guide.xml.gz";
    private const string EpgShareUk = "https://epgshare01.online/epgshare01/epg_ripper_UK1.xml.gz";
    private const string EpgPwGb = "https://epg.pw/xmltv/epg_GB.xml.gz";
    private const string Dp247Freeview = "https://raw.githubusercontent.com/dp247/Freeview-EPG/master/epg.xml";

    /// <summary>Preset channels for the Settings picker when the guide
    /// file cannot be downloaded (ids verified against open-epg).</summary>
    public static readonly IReadOnlyList<EpgChannel> PresetChannels = new[]
    {
        new EpgChannel("RTÉOne.ie", "RTÉ One"),
        new EpgChannel("RTÉ2.ie", "RTÉ2"),
        new EpgChannel("RTÉNews.ie", "RTÉ News"),
        new EpgChannel("TG4.ie", "TG4"),
        new EpgChannel("VirginMediaOne.ie", "Virgin Media One"),
        new EpgChannel("BBCOneLondon.uk", "BBC One London"),
        new EpgChannel("SkySportsMainEvent.uk", "Sky Sports Main Event")
    };

    public async Task<EpgGuide?> GetGuideAsync(EpgSettings settings, CancellationToken ct = default)
    {
        if (settings.XtreamConfigured)
        {
            var fromProvider = await TryXtreamAsync(settings, ct);
            if (fromProvider is not null) return fromProvider;
            Logger.Warn("Live guide: provider failed, falling back to the free guide.");
        }

        string feedUrl = ResolveFeedUrl(settings);
        string? xml = await LoadXmlTvAsync(feedUrl, settings.ChannelId, ct);
        if (xml is null) return null;
        return ParseXmlTv(xml, settings.ChannelId, settings.ChannelName);
    }

    /// <summary>Channel list for the Settings picker, from the given
    /// guide file (downloaded, or the local cache). Empty on failure —
    /// the picker falls back to <see cref="PresetChannels"/>.</summary>
    public async Task<IReadOnlyList<EpgChannel>> GetChannelsAsync(string feedUrl, CancellationToken ct = default)
    {
        string? xml = await LoadXmlTvAsync(feedUrl, channelId: string.Empty, ct);
        if (xml is null) return Array.Empty<EpgChannel>();
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return Array.Empty<EpgChannel>();
            return root.Elements()
                .Where(e => e.Name.LocalName == "channel")
                .Select(ch => new EpgChannel(
                    (string?)ch.Attribute("id") ?? string.Empty,
                    ch.Elements().Where(e => e.Name.LocalName == "display-name")
                        .Select(e => e.Value.Trim())
                        .FirstOrDefault(n => n.Length > 0) ?? string.Empty))
                .Where(c => c.Id.Length > 0 && c.Name.Length > 0)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            Logger.Warn($"Live guide: could not parse channel list: {ex.GetType().Name}");
            return Array.Empty<EpgChannel>();
        }
    }

    // ----- Feed URL ----------------------------------------------------------

    /// <summary>
    /// A channel id ending .uk needs the UK file and one ending .ie
    /// needs the Ireland file. If the stored URL is one of the two
    /// known defaults and the channel belongs to the other file,
    /// correct it — a custom URL is always respected as-is.
    /// </summary>
    private static string ResolveFeedUrl(EpgSettings settings)
    {
        string url = settings.FeedUrl.Trim();
        if (string.Equals(url, EpgSettings.IrelandFeedUrl, StringComparison.OrdinalIgnoreCase) &&
            settings.ChannelId.EndsWith(".uk", StringComparison.OrdinalIgnoreCase))
            return EpgSettings.UkFeedUrl;
        if (string.Equals(url, EpgSettings.UkFeedUrl, StringComparison.OrdinalIgnoreCase) &&
            settings.ChannelId.EndsWith(".ie", StringComparison.OrdinalIgnoreCase))
            return EpgSettings.IrelandFeedUrl;
        return url;
    }

    private static IEnumerable<string> CandidateUrls(string feedUrl, string channelId)
    {
        yield return feedUrl;
        bool isUk = channelId.EndsWith(".uk", StringComparison.OrdinalIgnoreCase) ||
                    feedUrl.Contains("unitedkingdom", StringComparison.OrdinalIgnoreCase) ||
                    feedUrl.Contains("epgtalk.com", StringComparison.OrdinalIgnoreCase) ||
                    feedUrl.Contains("epgshare01", StringComparison.OrdinalIgnoreCase) ||
                    feedUrl.Contains("epg_GB", StringComparison.OrdinalIgnoreCase);
        bool isIe = channelId.EndsWith(".ie", StringComparison.OrdinalIgnoreCase) ||
                    feedUrl.Contains("ireland", StringComparison.OrdinalIgnoreCase);
        if (isUk)
        {
            foreach (string mirror in new[] { EpgSettings.UkFeedUrl, EpgTalkUk, EpgShareUk, EpgPwGb, Dp247Freeview })
            {
                if (!string.Equals(mirror, feedUrl, StringComparison.OrdinalIgnoreCase))
                    yield return mirror;
            }
        }
        else if (isIe)
        {
            if (!string.Equals(EpgSettings.IrelandFeedUrl, feedUrl, StringComparison.OrdinalIgnoreCase))
                yield return EpgSettings.IrelandFeedUrl;
        }
    }

    // ----- Download + cache ----------------------------------------------------

    private static string CachePathFor(string feedUrl)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(feedUrl.Trim().ToLowerInvariant()));
        string hex = Convert.ToHexString(hash).ToLowerInvariant()[..12];
        return Path.Combine(ConfigService.ConfigDirectory, $"epg-cache-{hex}.xml.gz");
    }

    /// <summary>Download the guide (or reuse a fresh cache). Returns
    /// the decompressed XML, or null when nothing usable is available.
    /// Never throws.</summary>
    private static async Task<string?> LoadXmlTvAsync(string feedUrl, string channelId, CancellationToken ct)
    {
        string cachePath = CachePathFor(feedUrl);
        if (File.Exists(cachePath) &&
            File.GetLastWriteTimeUtc(cachePath) > DateTime.UtcNow.AddMinutes(-30))
        {
            try { return Decompress(await File.ReadAllBytesAsync(cachePath, ct)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Logger.Warn($"Live guide: fresh cache unreadable: {ex.GetType().Name}");
            }
        }

        foreach (string url in CandidateUrls(feedUrl, channelId))
        {
            try
            {
                byte[] gz = await Http.GetByteArrayAsync(url, ct);
                if (gz.Length == 0 || gz.Length > 64 * 1024 * 1024) continue;
                string xml = Decompress(gz);
                if (!xml.Contains("<tv", StringComparison.OrdinalIgnoreCase)) continue;
                try { await File.WriteAllBytesAsync(cachePath, gz, ct); } catch { /* cache is best-effort */ }
                Logger.Info($"Live guide: downloaded guide from {HostOf(url)}.");
                return xml;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
            {
                Logger.Warn($"Live guide: download failed from {HostOf(url)}: {ex.GetType().Name}");
            }
        }

        // Last resort: any cache, however stale. The now/next selection
        // below yields nothing for stale data, which degrades honestly.
        if (File.Exists(cachePath))
        {
            try { return Decompress(await File.ReadAllBytesAsync(cachePath, ct)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Logger.Warn($"Live guide: stale cache unreadable: {ex.GetType().Name}");
            }
        }
        return null;
    }

    private static string Decompress(byte[] bytes)
    {
        // .gz magic bytes 1F 8B; some mirrors serve plain XML instead.
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        return Encoding.UTF8.GetString(bytes);
    }

    // ----- XMLTV parsing ---------------------------------------------------------

    private static EpgGuide? ParseXmlTv(string xml, string channelId, string channelName)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (Exception ex) when (ex is System.Xml.XmlException)
        {
            Logger.Warn($"Live guide: guide XML would not parse: {ex.GetType().Name}");
            return null;
        }
        var root = doc.Root;
        if (root is null) return null;

        string? resolvedId = null;
        string resolvedName = channelName;
        foreach (var ch in root.Elements().Where(e => e.Name.LocalName == "channel"))
        {
            string? id = (string?)ch.Attribute("id");
            string? name = ch.Elements().Where(e => e.Name.LocalName == "display-name")
                .Select(e => e.Value.Trim()).FirstOrDefault(n => n.Length > 0);
            if (id is not null && string.Equals(id, channelId, StringComparison.OrdinalIgnoreCase))
            {
                resolvedId = id;
                if (name is not null) resolvedName = name;
                break;
            }
            if (resolvedId is null && name is not null &&
                string.Equals(name, channelName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedId = id;
                resolvedName = name;
            }
        }
        if (resolvedId is null) return null;

        var programmes = new List<EpgProgramme>();
        foreach (var p in root.Elements().Where(e => e.Name.LocalName == "programme"))
        {
            if (!string.Equals((string?)p.Attribute("channel"), resolvedId, StringComparison.OrdinalIgnoreCase))
                continue;
            string? title = p.Elements().Where(e => e.Name.LocalName == "title")
                .Select(e => e.Value.Trim()).FirstOrDefault(t => t.Length > 0);
            if (title is null) continue;
            if (!TryParseXmlTvTime((string?)p.Attribute("start"), out DateTimeOffset start)) continue;
            if (!TryParseXmlTvTime((string?)p.Attribute("stop"), out DateTimeOffset end)) continue;
            programmes.Add(new EpgProgramme { Title = title, StartUtc = start.UtcDateTime, EndUtc = end.UtcDateTime });
        }
        return SelectNowNextLater(programmes, resolvedName, "Free guide");
    }

    private static EpgGuide? SelectNowNextLater(List<EpgProgramme> programmes, string channelName, string source)
    {
        if (programmes.Count == 0) return null;
        programmes.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        DateTime now = DateTime.UtcNow;
        EpgProgramme? current = programmes.FirstOrDefault(p => p.StartUtc <= now && now < p.EndUtc);
        var upcoming = programmes.Where(p => p.StartUtc > now).OrderBy(p => p.StartUtc).ToList();
        EpgProgramme? next = upcoming.FirstOrDefault();
        EpgProgramme? later = upcoming.Skip(1).FirstOrDefault();
        if (current is null && next is null) return null;
        return new EpgGuide
        {
            ChannelName = channelName,
            Now = current,
            Next = next,
            Later = later,
            Source = source,
            FetchedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>XMLTV times look like "20261109210000 +0000". The offset
    /// is honoured when present; a missing offset is read as UTC.</summary>
    private static bool TryParseXmlTvTime(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (!DateTime.TryParseExact(parts[0], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime local))
            return false;
        TimeSpan offset = TimeSpan.Zero;
        if (parts.Length >= 2)
        {
            string o = parts[1];
            if (o.Length == 5 && (o[0] == '+' || o[0] == '-') &&
                int.TryParse(o.AsSpan(1, 2), out int oh) && int.TryParse(o.AsSpan(3, 2), out int om))
            {
                offset = new TimeSpan(oh, om, 0);
                if (o[0] == '-') offset = -offset;
            }
        }
        result = new DateTimeOffset(local, offset);
        return true;
    }

    // ----- Xtream Codes override --------------------------------------------------

    private static async Task<EpgGuide?> TryXtreamAsync(EpgSettings settings, CancellationToken ct)
    {
        string baseUrl = settings.XtreamServerUrl.Trim().TrimEnd('/');
        string user = Uri.EscapeDataString(settings.XtreamUsername.Trim());
        string pass = Uri.EscapeDataString(settings.XtreamPassword);
        try
        {
            string streamsUrl = $"{baseUrl}/player_api.php?username={user}&password={pass}&action=get_live_streams";
            string streamsJson = await Http.GetStringAsync(streamsUrl, ct);
            string? streamId = FindXtreamStreamId(streamsJson, settings.XtreamChannelName);
            if (streamId is null)
            {
                Logger.Warn("Live guide (provider): channel not found in the provider's channel list.");
                return null;
            }

            string epgUrl = $"{baseUrl}/player_api.php?username={user}&password={pass}&action=get_short_epg&stream_id={Uri.EscapeDataString(streamId)}";
            string epgJson = await Http.GetStringAsync(epgUrl, ct);
            var guide = ParseShortEpg(epgJson, settings.XtreamChannelName);
            if (guide is not null) return guide;

            // Fallback: the provider's own XMLTV file.
            string xmltvUrl = $"{baseUrl}/xmltv.php?username={user}&password={pass}";
            string xml = await Http.GetStringAsync(xmltvUrl, ct);
            var xmlGuide = ParseXmlTv(xml, channelId: string.Empty, settings.XtreamChannelName);
            if (xmlGuide is not null)
                return new EpgGuide
                {
                    ChannelName = xmlGuide.ChannelName,
                    Now = xmlGuide.Now,
                    Next = xmlGuide.Next,
                    Later = xmlGuide.Later,
                    Source = "Provider guide",
                    FetchedAtUtc = xmlGuide.FetchedAtUtc
                };
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Logger.Warn($"Live guide (provider): {ex.GetType().Name} from {HostOf(baseUrl)}.");
            return null;
        }
    }

    private static string? FindXtreamStreamId(string json, string channelName)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        string? containsMatch = null;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("name", out var nameEl)) continue;
            string? name = nameEl.GetString();
            if (name is null) continue;
            string? id = el.TryGetProperty("stream_id", out var idEl)
                ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString()
                : null;
            if (id is null) continue;
            if (string.Equals(name.Trim(), channelName.Trim(), StringComparison.OrdinalIgnoreCase))
                return id;
            containsMatch ??= (name.Contains(channelName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                               channelName.Trim().Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))
                ? id : null;
        }
        return containsMatch;
    }

    private static EpgGuide? ParseShortEpg(string json, string channelName)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("epg_listings", out var listings) ||
            listings.ValueKind != JsonValueKind.Array)
            return null;
        var programmes = new List<EpgProgramme>();
        foreach (var el in listings.EnumerateArray())
        {
            string? title = el.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;
            string? startRaw = FirstString(el, "start", "start_timestamp");
            string? endRaw = FirstString(el, "end", "stop_timestamp", "stop");
            if (!TryParseUnixOrDate(startRaw, out DateTime start)) continue;
            if (!TryParseUnixOrDate(endRaw, out DateTime end)) continue;
            programmes.Add(new EpgProgramme { Title = title.Trim(), StartUtc = start, EndUtc = end });
        }
        return SelectNowNextLater(programmes, channelName, "Provider guide");
    }

    private static string? FirstString(JsonElement el, params string[] names)
    {
        foreach (string name in names)
        {
            if (!el.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
        }
        return null;
    }

    private static bool TryParseUnixOrDate(string? raw, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string value = raw.Trim();
        if (long.TryParse(value, out long unix))
        {
            utc = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            return true;
        }
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt))
        {
            utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            return true;
        }
        return false;
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; }
        catch { return "provider"; }
    }
}
