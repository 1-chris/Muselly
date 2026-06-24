namespace Muselly.Core.Models;

/// <summary>
/// An artist: all albums and tracks attributed to a single album-artist, assembled by the library.
/// </summary>
public sealed class Artist
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public string? ArtworkPath { get; init; }

    public IReadOnlyList<Album> Albums { get; init; } = Array.Empty<Album>();

    public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();

    public int AlbumCount => Albums.Count;

    public int TrackCount => Tracks.Count;

    public IReadOnlyList<string> Genres
    {
        get
        {
            var set = new List<string>();
            foreach (var t in Tracks)
                foreach (var g in t.Genres)
                    if (!set.Contains(g)) set.Add(g);
            return set;
        }
    }
}
