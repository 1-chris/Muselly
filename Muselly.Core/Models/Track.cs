namespace Muselly.Core.Models;

/// <summary>
/// A single playable item in the library, carrying the full set of metadata extracted from the file's
/// tags during a library scan. Tracks are immutable value snapshots produced by the scanner; the library,
/// queue and playlists reference them by <see cref="Id"/>.
/// </summary>
public sealed class Track
{
    /// <summary>Stable identity for the track. Derived from the absolute file path (hashed) so a rescan
    /// of the same file produces the same id and references survive.</summary>
    public required string Id { get; init; }

    /// <summary>Absolute path (or URI) to the audio source on disk.</summary>
    public required string Source { get; init; }

    public required string Title { get; init; }

    public string? Artist { get; init; }

    /// <summary>The album-level artist used for grouping (falls back to <see cref="Artist"/>).</summary>
    public string? AlbumArtist { get; init; }

    public string? Album { get; init; }

    public string? Composer { get; init; }

    /// <summary>Genres declared on the file (a track may carry several).</summary>
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    /// <summary>Record label / publisher, when present in the tags.</summary>
    public string? Label { get; init; }

    public uint Year { get; init; }

    public uint TrackNumber { get; init; }

    public uint TrackCount { get; init; }

    public uint DiscNumber { get; init; }

    public uint DiscCount { get; init; }

    public TimeSpan Duration { get; init; }

    public int Bitrate { get; init; }

    public int SampleRate { get; init; }

    public int Channels { get; init; }

    /// <summary>The audio codec/format, e.g. <c>FLAC</c>, <c>MP3</c>, <c>Opus</c>.</summary>
    public string? Codec { get; init; }

    public string Extension { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    /// <summary>Absolute path to the cached cover image extracted from the file, if any.</summary>
    public string? ArtworkPath { get; init; }

    /// <summary>True when the file declares itself part of a compilation (iTunes <c>cpil</c> / ID3 <c>TCMP</c>
    /// / Vorbis <c>COMPILATION</c>). Used to group such tracks under a single "Various Artists" album.</summary>
    public bool IsCompilation { get; init; }

    /// <summary>Stable key identifying the album this track belongs to (album-artist + album name).</summary>
    public string AlbumKey { get; init; } = string.Empty;

    /// <summary>Stable key identifying the (primary) artist this track belongs to.</summary>
    public string ArtistKey { get; init; } = string.Empty;

    /// <summary>Absolute path to the directory the file lives in (for browse-by-folder).</summary>
    public string Directory { get; init; } = string.Empty;

    public DateTimeOffset DateAdded { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset FileModified { get; init; }

    /// <summary>Best display artist: explicit artist, else album artist, else "Unknown Artist".</summary>
    public string DisplayArtist => !string.IsNullOrWhiteSpace(Artist) ? Artist!
        : !string.IsNullOrWhiteSpace(AlbumArtist) ? AlbumArtist!
        : "Unknown Artist";

    public string DisplayAlbum => string.IsNullOrWhiteSpace(Album) ? "Unknown Album" : Album!;

    /// <summary>Returns a copy of this track with a different cached artwork path (tracks are immutable).</summary>
    public Track WithArtwork(string? artworkPath) => Clone(artworkPath);

    /// <summary>Returns a copy of this track reassigned to a different album grouping. Used to collapse a
    /// compilation's per-artist albums into one "Various Artists" album without touching the track's identity
    /// or its own artist tag.</summary>
    public Track WithAlbumGrouping(string albumArtist, string albumKey, string artistKey) =>
        Clone(ArtworkPath, albumArtist, albumKey, artistKey);

    private Track Clone(string? artworkPath, string? albumArtist = null, string? albumKey = null,
        string? artistKey = null) => new()
    {
        Id = Id,
        Source = Source,
        Title = Title,
        Artist = Artist,
        AlbumArtist = albumArtist ?? AlbumArtist,
        Album = Album,
        Composer = Composer,
        Genres = Genres,
        Label = Label,
        Year = Year,
        TrackNumber = TrackNumber,
        TrackCount = TrackCount,
        DiscNumber = DiscNumber,
        DiscCount = DiscCount,
        Duration = Duration,
        Bitrate = Bitrate,
        SampleRate = SampleRate,
        Channels = Channels,
        Codec = Codec,
        Extension = Extension,
        FileSizeBytes = FileSizeBytes,
        ArtworkPath = artworkPath,
        IsCompilation = IsCompilation,
        AlbumKey = albumKey ?? AlbumKey,
        ArtistKey = artistKey ?? ArtistKey,
        Directory = Directory,
        DateAdded = DateAdded,
        FileModified = FileModified
    };
}
