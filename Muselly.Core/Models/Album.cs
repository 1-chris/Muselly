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

    /// <summary>Cover artwork (taken from the first track that carries embedded art).</summary>
    public string? ArtworkPath { get; init; }

    public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();

    public int TrackCount => Tracks.Count;

    public int DiscCount
    {
        get
        {
            var max = 0u;
            foreach (var t in Tracks)
                if (t.DiscNumber > max) max = t.DiscNumber;
            return (int)Math.Max(1, max);
        }
    }

    public TimeSpan Duration
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var t in Tracks) total += t.Duration;
            return total;
        }
    }
}
