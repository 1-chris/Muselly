using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Web;

/// <summary>Progress of a "scan for missing album art" run.</summary>
public sealed class ArtworkScanProgress
{
    public bool IsRunning { get; init; }
    public int Processed { get; init; }
    public int Total { get; init; }
    public int Found { get; init; }
    public string? CurrentItem { get; init; }
    public double Fraction => Total <= 0 ? 0 : Math.Clamp((double)Processed / Total, 0, 1);
}

/// <summary>Scans the library for albums missing cover art and fills it in from the web.</summary>
public interface IArtworkScanService
{
    bool IsRunning { get; }
    event EventHandler<ArtworkScanProgress>? ProgressChanged;

    /// <summary>Finds albums with no usable artwork and fetches it. Returns the number of covers added.</summary>
    Task<int> ScanMissingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IArtworkScanService"/>. Walks the library's albums, skips those that already have a
/// readable cover, and for the rest asks <see cref="IAlbumArtFetcher"/> for art from public sources,
/// applying any hit back to the library (which persists it). Requests are spaced out slightly to be a
/// polite client of the free APIs.
/// </summary>
public sealed class ArtworkScanService : IArtworkScanService
{
    private readonly ILibraryService _library;
    private readonly IAlbumArtFetcher _fetcher;
    private readonly ILogger<ArtworkScanService> _logger;

    public ArtworkScanService(ILibraryService library, IAlbumArtFetcher fetcher, ILogger<ArtworkScanService> logger)
    {
        _library = library;
        _fetcher = fetcher;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }

    public event EventHandler<ArtworkScanProgress>? ProgressChanged;

    public async Task<int> ScanMissingAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return 0;
        IsRunning = true;
        var found = 0;
        try
        {
            var missing = new List<(string Key, string Artist, string Title)>();
            foreach (var album in _library.Albums)
            {
                // Never fetch art for albums merged in from a connected server — those are the host's to manage,
                // and their artwork is streamed remotely rather than stored locally.
                if (!IsLocal(album)) continue;
                if (HasArtwork(album.ArtworkPath)) continue;
                missing.Add((album.Key, album.AlbumArtist, album.Title));
            }

            var total = missing.Count;
            Report(true, 0, total, found, null);

            for (var i = 0; i < missing.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (key, artist, title) = missing[i];
                Report(true, i, total, found, title);

                try
                {
                    var path = await _fetcher.FetchAsync(artist, title, key, cancellationToken).ConfigureAwait(false);
                    if (path is not null)
                    {
                        await _library.ApplyAlbumArtworkAsync(key, path).ConfigureAwait(false);
                        found++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Artwork fetch failed for {Artist} - {Album}", artist, title);
                }

                // Be polite to the public APIs.
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            Report(false, total, total, found, null);
            _logger.LogInformation("Album art scan complete: {Found}/{Total} covers added", found, total);
        }
        catch (OperationCanceledException)
        {
            Report(false, 0, 0, found, null);
        }
        finally
        {
            IsRunning = false;
        }
        return found;
    }

    private static bool HasArtwork(string? path) => !string.IsNullOrEmpty(path) && File.Exists(path);

    /// <summary>True if the album has at least one track sourced from the local disk (not a remote server).</summary>
    private static bool IsLocal(Album album)
    {
        foreach (var t in album.Tracks)
            if (!RemoteSource.IsRemote(t.Source))
                return true;
        return false;
    }

    private void Report(bool running, int processed, int total, int found, string? item) =>
        ProgressChanged?.Invoke(this, new ArtworkScanProgress
        {
            IsRunning = running,
            Processed = processed,
            Total = total,
            Found = found,
            CurrentItem = item
        });
}
