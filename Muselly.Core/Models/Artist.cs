namespace Muselly.Core.Models;

/// <summary>
/// An artist summary: identity, image and the artist's album summaries, plus pre-computed counts. Tracks are
/// loaded on demand via <see cref="TracksProvider"/> rather than held resident.
/// </summary>
public sealed class Artist
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? ArtworkPath { get; set; }

    public IReadOnlyList<Album> Albums { get; init; } = Array.Empty<Album>();

    public int AlbumCount => Albums.Count;

    /// <summary>Pre-computed track total (so display doesn't load the tracks).</summary>
    public int TrackCount { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    /// <summary>Loads this artist's tracks on demand (set by the library service).</summary>
    public Func<Artist, IReadOnlyList<Track>>? TracksProvider { get; set; }

    /// <summary>The artist's tracks. Loaded lazily from the store.</summary>
    public IReadOnlyList<Track> Tracks => TracksProvider?.Invoke(this) ?? Array.Empty<Track>();
}
