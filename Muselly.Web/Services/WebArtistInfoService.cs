using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Web;

namespace Muselly.Web.Services;

/// <summary>
/// Browser <see cref="IArtistInfoService"/> that fetches artist biographies from the host (which sources them
/// from Wikipedia and caches the result), rather than calling Wikipedia from the browser. Artist images come
/// from the album artwork the host already serves, so this only deals with the biography text. Results are
/// kept in memory for the session.
/// </summary>
public sealed class WebArtistInfoService : IArtistInfoService
{
    private readonly WebHostClient _client;
    private readonly ConcurrentDictionary<string, ArtistInfo> _cache = new();
    private readonly ConcurrentDictionary<string, Task<ArtistInfo?>> _inFlight = new();

    public WebArtistInfoService(WebHostClient client) => _client = client;

    public event EventHandler<ArtistInfo>? ArtistInfoUpdated;

    public Task LoadAsync() => Task.CompletedTask;

    public ArtistInfo? Get(string artistKey) => _cache.TryGetValue(artistKey, out var info) ? info : null;

    public Task<ArtistInfo?> GetWhenLoadedAsync(string artistKey) => Task.FromResult<ArtistInfo?>(Get(artistKey));

    public Task<ArtistInfo?> EnsureAsync(string artistKey, string artistName, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(artistKey, out var existing) && (existing.HasBiography || existing.NotFound))
            return Task.FromResult<ArtistInfo?>(existing);
        return _inFlight.GetOrAdd(artistKey, key => FetchAsync(key, cancellationToken));
    }

    public Task<ArtistInfo?> RefreshAsync(string artistKey, string artistName, CancellationToken cancellationToken = default)
        => FetchAsync(artistKey, cancellationToken);

    private async Task<ArtistInfo?> FetchAsync(string artistKey, CancellationToken cancellationToken)
    {
        try
        {
            var bio = await _client.GetTextAsync("/api/artist-bio?key=" + Uri.EscapeDataString(artistKey), cancellationToken)
                .ConfigureAwait(false);
            var hasBio = !string.IsNullOrWhiteSpace(bio);
            var info = new ArtistInfo
            {
                ArtistKey = artistKey,
                Biography = hasBio ? bio : null,
                BiographySource = hasBio ? "server" : null,
                NotFound = !hasBio,
                FetchedAt = DateTimeOffset.Now
            };
            _cache[artistKey] = info;
            if (hasBio) ArtistInfoUpdated?.Invoke(this, info);
            return info;
        }
        finally
        {
            _inFlight.TryRemove(artistKey, out _);
        }
    }
}
