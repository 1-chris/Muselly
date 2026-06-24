using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;

namespace Muselly.Core.Services.Web;

/// <summary>The raw result of a web lyrics lookup, before parsing/caching.</summary>
public sealed class LyricsFetchResult
{
    /// <summary>Synchronised lyrics in LRC format, if the provider has them.</summary>
    public string? SyncedLrc { get; init; }

    /// <summary>Plain (untimed) lyrics text, if that's all the provider has.</summary>
    public string? PlainText { get; init; }

    public bool HasSynced => !string.IsNullOrWhiteSpace(SyncedLrc);
    public bool HasAny => HasSynced || !string.IsNullOrWhiteSpace(PlainText);
}

/// <summary>A web source of lyrics. Implementations must use only key-free, public APIs.</summary>
public interface ILyricsProvider
{
    /// <summary>Display name used to credit the source in the UI.</summary>
    string Name { get; }

    /// <summary>Looks up lyrics for the track; null/empty result means "not found here".</summary>
    Task<LyricsFetchResult?> FetchAsync(Track track, CancellationToken cancellationToken = default);
}

/// <summary>lrclib.net — community LRC database with good synced coverage. No API key.</summary>
public sealed class LrcLibLyricsProvider : ILyricsProvider
{
    public string Name => "lrclib.net";

    public async Task<LyricsFetchResult?> FetchAsync(Track track, CancellationToken ct = default)
    {
        var title = track.Title;
        if (string.IsNullOrWhiteSpace(title)) return null;

        var url = "https://lrclib.net/api/get?artist_name=" + WebClient.Encode(track.DisplayArtist) +
                  "&track_name=" + WebClient.Encode(title);
        if (!string.IsNullOrWhiteSpace(track.Album) && track.Album != "Unknown Album")
            url += "&album_name=" + WebClient.Encode(track.Album!);
        if (track.Duration.TotalSeconds >= 1)
            url += "&duration=" + (int)track.Duration.TotalSeconds;

        var result = await WebClient.GetJsonAsync<LrcLibResult>(url, ct).ConfigureAwait(false);
        if (result is null) return null;
        if (!result.HasContent) return null;

        return new LyricsFetchResult { SyncedLrc = result.SyncedLyrics, PlainText = result.PlainLyrics };
    }

    private sealed class LrcLibResult
    {
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
        public bool Instrumental { get; set; }
        public bool HasContent => !string.IsNullOrWhiteSpace(PlainLyrics) || !string.IsNullOrWhiteSpace(SyncedLyrics);
    }
}

/// <summary>
/// NetEase Cloud Music (music.163.com). Its public web API returns synchronised LRC and has excellent
/// coverage of East-Asian music (where lrclib often comes up empty). No API key; a Referer header is sent
/// as the endpoint expects requests to originate from the site.
/// </summary>
public sealed class NetEaseLyricsProvider : ILyricsProvider
{
    // NetEase returns a different (worse) result set unless the request looks like it comes from a browser
    // on its own site, so send a browser User-Agent and Referer.
    private static readonly IReadOnlyList<KeyValuePair<string, string>> Headers = new[]
    {
        new KeyValuePair<string, string>("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36"),
        new KeyValuePair<string, string>("Referer", "https://music.163.com/"),
        new KeyValuePair<string, string>("Cookie", "NMTID=1")
    };

    private readonly ILogger<NetEaseLyricsProvider> _logger;

    public NetEaseLyricsProvider(ILogger<NetEaseLyricsProvider> logger) => _logger = logger;

    public string Name => "NetEase";

    public async Task<LyricsFetchResult?> FetchAsync(Track track, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track.Title)) return null;

        try
        {
            var query = WebClient.Encode($"{track.Title} {track.DisplayArtist}".Trim());
            // Note: the /web and /pc variants return encrypted/gated payloads; this one returns plain JSON.
            var searchUrl = $"https://music.163.com/api/search/get?s={query}&type=1&limit=10";
            var search = await WebClient.GetJsonAsync<SearchResponse>(searchUrl, Headers, ct).ConfigureAwait(false);

            var song = PickBest(search?.Result?.Songs, track);
            if (song is null) return null;

            var lyricUrl = $"https://music.163.com/api/song/lyric?id={song.Id}&lv=1&kv=1&tv=-1";
            var lyric = await WebClient.GetJsonAsync<LyricResponse>(lyricUrl, Headers, ct).ConfigureAwait(false);

            var lrc = lyric?.Lrc?.Lyric;
            if (string.IsNullOrWhiteSpace(lrc)) return null;

            // NetEase returns LRC for synced lyrics; some entries are plain text only.
            return LrcParser.LooksSynced(lrc)
                ? new LyricsFetchResult { SyncedLrc = lrc }
                : new LyricsFetchResult { PlainText = lrc };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NetEase lyrics lookup failed for {Title}", track.Title);
            return null;
        }
    }

    private static Song? PickBest(List<Song>? songs, Track track)
    {
        if (songs is not { Count: > 0 }) return null;

        Song? best = null;
        var bestScore = 0;
        var targetMs = track.Duration.TotalMilliseconds;

        foreach (var s in songs)
        {
            if (s.Id <= 0) continue;
            var score = 0;

            if (TitleMatches(s.Name, track.Title)) score += 3;
            if (ArtistMatches(s.Artists, track.DisplayArtist)) score += 3;

            if (targetMs > 0 && s.Duration > 0)
            {
                var diff = Math.Abs(s.Duration - targetMs);
                if (diff <= 4000) score += 2;
                else if (diff <= 15000) score += 1;
            }

            if (score > bestScore) { bestScore = score; best = s; }
        }

        // Only accept a result we're reasonably confident about (avoid returning unrelated lyrics).
        return bestScore >= 3 ? best : null;
    }

    private static bool TitleMatches(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        (a.Trim().Contains(b.Trim(), StringComparison.OrdinalIgnoreCase) ||
         b.Trim().Contains(a.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool ArtistMatches(List<Artist>? artists, string target)
    {
        if (artists is null || string.IsNullOrEmpty(target) || target == "Unknown Artist") return false;
        foreach (var a in artists)
            if (!string.IsNullOrEmpty(a.Name) &&
                (target.Contains(a.Name!, StringComparison.OrdinalIgnoreCase) ||
                 a.Name!.Contains(target, StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    private sealed class SearchResponse
    {
        public SearchResult? Result { get; set; }
    }

    private sealed class SearchResult
    {
        public List<Song>? Songs { get; set; }
    }

    private sealed class Song
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public long Duration { get; set; }
        public List<Artist>? Artists { get; set; }
    }

    private sealed class Artist
    {
        public string? Name { get; set; }
    }

    private sealed class LyricResponse
    {
        public LrcBlock? Lrc { get; set; }
    }

    private sealed class LrcBlock
    {
        public string? Lyric { get; set; }
    }
}

/// <summary>lyrics.ovh — a simple, key-free API that returns plain (untimed) lyrics. Used as a last resort.</summary>
public sealed class LyricsOvhProvider : ILyricsProvider
{
    public string Name => "lyrics.ovh";

    public async Task<LyricsFetchResult?> FetchAsync(Track track, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || track.DisplayArtist == "Unknown Artist") return null;

        var url = $"https://api.lyrics.ovh/v1/{WebClient.Encode(track.DisplayArtist)}/{WebClient.Encode(track.Title)}";
        var result = await WebClient.GetJsonAsync<OvhResult>(url, ct).ConfigureAwait(false);
        if (result is null || string.IsNullOrWhiteSpace(result.Lyrics)) return null;

        return new LyricsFetchResult { PlainText = result.Lyrics };
    }

    private sealed class OvhResult
    {
        public string? Lyrics { get; set; }
    }
}
