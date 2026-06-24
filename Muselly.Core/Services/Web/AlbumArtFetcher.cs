using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Web;

/// <summary>Fetches album cover art from public, key-free web sources.</summary>
public interface IAlbumArtFetcher
{
    /// <summary>
    /// Tries to find cover art for the album and download it into the artwork cache. Returns the local
    /// file path on success, or null if nothing suitable was found.
    /// </summary>
    Task<string?> FetchAsync(string albumArtist, string album, string albumKey, CancellationToken ct = default);
}

/// <summary>
/// Album-art fetcher backed by the iTunes Search API (primary) and the MusicBrainz + Cover Art Archive
/// pair (fallback). Neither requires an API key. The downloaded image is cached under the album key so it
/// is picked up by the existing artwork pipeline.
/// </summary>
public sealed class AlbumArtFetcher : IAlbumArtFetcher
{
    private readonly ILogger<AlbumArtFetcher> _logger;

    public AlbumArtFetcher(ILogger<AlbumArtFetcher> logger) => _logger = logger;

    public async Task<string?> FetchAsync(string albumArtist, string album, string albumKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(album)) return null;

        var dest = Path.Combine(StoragePaths.ArtworkCacheDirectory(), albumKey + ".jpg");

        var imageUrl = await FromItunesAsync(albumArtist, album, ct).ConfigureAwait(false)
                       ?? await FromCoverArtArchiveAsync(albumArtist, album, ct).ConfigureAwait(false);

        if (imageUrl is null) return null;

        var saved = await WebClient.DownloadFileAsync(imageUrl, dest, ct).ConfigureAwait(false);
        if (saved is not null)
            _logger.LogDebug("Fetched album art for {Artist} - {Album}", albumArtist, album);
        return saved;
    }

    private async Task<string?> FromItunesAsync(string artist, string album, CancellationToken ct)
    {
        var term = WebClient.Encode($"{artist} {album}".Trim());
        var url = $"https://itunes.apple.com/search?term={term}&entity=album&limit=8";
        var result = await WebClient.GetJsonAsync<ItunesResponse>(url, ct).ConfigureAwait(false);
        if (result?.Results is not { Count: > 0 }) return null;

        // Prefer a result whose album title matches; otherwise just take the first.
        ItunesAlbum? best = null;
        foreach (var r in result.Results)
        {
            if (string.IsNullOrEmpty(r.ArtworkUrl100)) continue;
            if (Matches(r.CollectionName, album))
            {
                best = r;
                break;
            }
            best ??= r;
        }

        var art = best?.ArtworkUrl100;
        if (string.IsNullOrEmpty(art)) return null;

        // iTunes returns 100x100; bump to a usable resolution.
        return art.Replace("100x100bb", "600x600bb");
    }

    private async Task<string?> FromCoverArtArchiveAsync(string artist, string album, CancellationToken ct)
    {
        var query = WebClient.Encode($"releasegroup:\"{album}\" AND artist:\"{artist}\"");
        var url = $"https://musicbrainz.org/ws/2/release-group/?query={query}&fmt=json&limit=5";
        var result = await WebClient.GetJsonAsync<MbReleaseGroupResponse>(url, ct).ConfigureAwait(false);
        if (result?.ReleaseGroups is not { Count: > 0 }) return null;

        foreach (var rg in result.ReleaseGroups)
        {
            if (string.IsNullOrEmpty(rg.Id)) continue;
            // Cover Art Archive redirects /front to the actual image; HttpClient follows it.
            return $"https://coverartarchive.org/release-group/{rg.Id}/front-500";
        }
        return null;
    }

    private static bool Matches(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class ItunesResponse
    {
        public List<ItunesAlbum>? Results { get; set; }
    }

    private sealed class ItunesAlbum
    {
        public string? CollectionName { get; set; }
        public string? ArtistName { get; set; }
        [JsonPropertyName("artworkUrl100")] public string? ArtworkUrl100 { get; set; }
    }

    private sealed class MbReleaseGroupResponse
    {
        [JsonPropertyName("release-groups")] public List<MbReleaseGroup>? ReleaseGroups { get; set; }
    }

    private sealed class MbReleaseGroup
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
    }
}
