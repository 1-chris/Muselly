namespace Muselly.Core.Models;

/// <summary>
/// An album: a group of tracks sharing the same album-artist and album title, assembled by the library
/// from scanned tracks. Tracks are ordered by disc then track number.
/// </summary>
public sealed class Album
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string AlbumArtist { get; init; }

    public uint Year { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    public string? Label { get; init; }

    /// <summary>Cover artwork (settable so an artwork update can patch it without a full rebuild).</summary>
    public string? ArtworkPath { get; set; }

    /// <summary>Pre-computed at build time so display never needs to load the tracks.</summary>
    public int TrackCount { get; init; }

    public int DiscCount { get; init; } = 1;

    public TimeSpan Duration { get; init; }

    /// <summary>Loads this album's tracks on demand (set by the library service). Not cached here, so the
    /// tracks are released once the caller is done with them.</summary>
    public Func<Album, IReadOnlyList<Track>>? TracksProvider { get; set; }

    /// <summary>The album's tracks, ordered by disc then track number. Loaded lazily from the store.</summary>
    public IReadOnlyList<Track> Tracks => TracksProvider?.Invoke(this) ?? Array.Empty<Track>();
}
