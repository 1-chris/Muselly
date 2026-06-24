using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Web;

/// <summary>Provides and caches web-sourced artist metadata (biography + image).</summary>
public interface IArtistInfoService
{
    /// <summary>Loads the persisted cache from disk. Call once at startup.</summary>
    Task LoadAsync();

    /// <summary>Returns cached info for an artist, or null if nothing has been fetched yet.</summary>
    ArtistInfo? Get(string artistKey);

    /// <summary>Like <see cref="Get"/>, but waits for the persisted cache to finish loading first.</summary>
    Task<ArtistInfo?> GetWhenLoadedAsync(string artistKey);

    /// <summary>
    /// Ensures info is present for the artist, fetching from the web if it is missing (or refreshing a
    /// previous "not found"). Returns the info (possibly empty). Safe to call repeatedly; in-flight and
    /// completed fetches are de-duplicated.
    /// </summary>
    Task<ArtistInfo?> EnsureAsync(string artistKey, string artistName, CancellationToken cancellationToken = default);

    /// <summary>Forces a fresh fetch, ignoring any cached result.</summary>
    Task<ArtistInfo?> RefreshAsync(string artistKey, string artistName, CancellationToken cancellationToken = default);

    /// <summary>Raised (possibly off the UI thread) when an artist's info is fetched/updated.</summary>
    event EventHandler<ArtistInfo>? ArtistInfoUpdated;
}

/// <summary>
/// Artist-info service backed by Wikipedia (lead-section extract + page image) via the key-free MediaWiki
/// API. Results are cached in memory and persisted to <c>artist-info.json</c>, and images are downloaded
/// into the artist image cache. A negative ("not found") result is remembered for a while so opening an
/// obscure artist repeatedly does not re-hit the network every time.
/// </summary>
public sealed class ArtistInfoService : IArtistInfoService
{
    private static readonly TimeSpan NegativeRetryAfter = TimeSpan.FromDays(7);

    private readonly ILogger<ArtistInfoService> _logger;
    private readonly ConcurrentDictionary<string, ArtistInfo> _cache = new();
    private readonly ConcurrentDictionary<string, Task<ArtistInfo?>> _inFlight = new();
    private readonly object _saveGate = new();

    /// <summary>The load task, so fetches never persist (and overwrite the file) before the cache is read.</summary>
    private Task _loadTask = Task.CompletedTask;

    public ArtistInfoService(ILogger<ArtistInfoService> logger) => _logger = logger;

    public event EventHandler<ArtistInfo>? ArtistInfoUpdated;

    public Task LoadAsync()
    {
        _loadTask = Task.Run(() =>
        {
            var store = JsonStore.Load(StoragePaths.ArtistInfoFile(), () => new ArtistInfoStore());
            foreach (var info in store.Artists)
                if (!string.IsNullOrEmpty(info.ArtistKey))
                    _cache[info.ArtistKey] = info;
        });
        return _loadTask;
    }

    public ArtistInfo? Get(string artistKey) => _cache.GetValueOrDefault(artistKey);

    public async Task<ArtistInfo?> GetWhenLoadedAsync(string artistKey)
    {
        await _loadTask.ConfigureAwait(false);
        return _cache.GetValueOrDefault(artistKey);
    }

    public async Task<ArtistInfo?> EnsureAsync(string artistKey, string artistName, CancellationToken cancellationToken = default)
    {
        // Wait for the persisted cache to load first, so we use a saved result instead of refetching it
        // (and so a fetch can't overwrite the file with a partially-loaded cache).
        await _loadTask.ConfigureAwait(false);

        if (_cache.TryGetValue(artistKey, out var existing) && !ShouldRefetch(existing))
            return existing;

        return await _inFlight.GetOrAdd(artistKey, _ => FetchAndCacheAsync(artistKey, artistName, cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<ArtistInfo?> RefreshAsync(string artistKey, string artistName, CancellationToken cancellationToken = default)
    {
        await _loadTask.ConfigureAwait(false);
        _inFlight.TryRemove(artistKey, out _);
        return await _inFlight.GetOrAdd(artistKey, _ => FetchAndCacheAsync(artistKey, artistName, cancellationToken))
            .ConfigureAwait(false);
    }

    private static bool ShouldRefetch(ArtistInfo info) =>
        info.NotFound && DateTimeOffset.Now - info.FetchedAt > NegativeRetryAfter;

    private async Task<ArtistInfo?> FetchAndCacheAsync(string artistKey, string artistName, CancellationToken ct)
    {
        try
        {
            // Guarantee the saved cache is loaded before we persist, so we never clobber the file.
            await _loadTask.ConfigureAwait(false);

            var info = await FetchFromWikipediaAsync(artistKey, artistName, ct).ConfigureAwait(false)
                       ?? new ArtistInfo { ArtistKey = artistKey, NotFound = true };

            info.ArtistKey = artistKey;
            info.FetchedAt = DateTimeOffset.Now;

            _cache[artistKey] = info;
            Persist();
            ArtistInfoUpdated?.Invoke(this, info);
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Artist info fetch failed for {Artist}", artistName);
            return null;
        }
        finally
        {
            _inFlight.TryRemove(artistKey, out _);
        }
    }

    private async Task<ArtistInfo?> FetchFromWikipediaAsync(string artistKey, string artistName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(artistName) || artistName == "Unknown Artist") return null;

        // One call: search for the best-matching page and pull its lead extract + original page image.
        var search = WebClient.Encode(artistName);
        var url = "https://en.wikipedia.org/w/api.php?action=query&format=json&redirects=1" +
                  "&generator=search&gsrlimit=1&gsrsearch=" + search +
                  "&prop=extracts|pageimages|info&inprop=url&exintro=1&explaintext=1&piprop=original";

        var response = await WebClient.GetJsonAsync<WikiResponse>(url, ct).ConfigureAwait(false);
        var page = response?.Query?.Pages?.Values.FirstOrDefault();
        if (page is null || string.IsNullOrWhiteSpace(page.Extract)) return null;

        var info = new ArtistInfo
        {
            ArtistKey = artistKey,
            Biography = page.Extract!.Trim(),
            BiographySource = "Wikipedia",
            BiographyUrl = page.FullUrl,
            NotFound = false
        };

        var imageUrl = page.Original?.Source;
        if (!string.IsNullOrEmpty(imageUrl))
        {
            var ext = GuessExtension(imageUrl);
            var dest = Path.Combine(StoragePaths.ArtistImageCacheDirectory(), artistKey + ext);
            info.ImagePath = await WebClient.DownloadFileAsync(imageUrl, dest, ct).ConfigureAwait(false);
        }

        return info;
    }

    private static string GuessExtension(string url)
    {
        var clean = url.Split('?', '#')[0];
        var ext = Path.GetExtension(clean).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" ? ext : ".jpg";
    }

    private void Persist()
    {
        try
        {
            lock (_saveGate)
            {
                var store = new ArtistInfoStore { Artists = new List<ArtistInfo>(_cache.Values) };
                JsonStore.Save(StoragePaths.ArtistInfoFile(), store);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist artist info cache");
        }
    }

    private sealed class ArtistInfoStore
    {
        public int Version { get; set; } = 1;
        public List<ArtistInfo> Artists { get; set; } = new();
    }

    private sealed class WikiResponse
    {
        public WikiQuery? Query { get; set; }
    }

    private sealed class WikiQuery
    {
        public Dictionary<string, WikiPage>? Pages { get; set; }
    }

    private sealed class WikiPage
    {
        public string? Title { get; set; }
        public string? Extract { get; set; }
        [JsonPropertyName("fullurl")] public string? FullUrl { get; set; }
        [JsonPropertyName("original")] public WikiImage? Original { get; set; }
    }

    private sealed class WikiImage
    {
        public string? Source { get; set; }
    }
}
