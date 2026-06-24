using Muselly.Core.Models;

namespace Muselly.Core.Util;

/// <summary>
/// Sorts a sequence of tracks by any <see cref="TrackSortField"/> and direction. Used by library views
/// and playlists. The <see cref="TrackSortField.Custom"/> field preserves the incoming order.
/// </summary>
public static class TrackSorter
{
    public static List<Track> Sort(IEnumerable<Track> tracks, TrackSortField field, SortDirection direction)
    {
        var list = new List<Track>(tracks);
        if (field == TrackSortField.Custom) return list;

        Comparison<Track> cmp = field switch
        {
            TrackSortField.Title => (a, b) => Str(a.Title, b.Title),
            TrackSortField.Artist => (a, b) => Str(a.DisplayArtist, b.DisplayArtist),
            TrackSortField.Album => (a, b) => Str(a.DisplayAlbum, b.DisplayAlbum),
            TrackSortField.AlbumArtist => (a, b) => Str(a.AlbumArtist, b.AlbumArtist),
            TrackSortField.Genre => (a, b) => Str(First(a.Genres), First(b.Genres)),
            TrackSortField.Year => (a, b) => a.Year.CompareTo(b.Year),
            TrackSortField.Duration => (a, b) => a.Duration.CompareTo(b.Duration),
            TrackSortField.DateAdded => (a, b) => a.DateAdded.CompareTo(b.DateAdded),
            TrackSortField.TrackNumber => CompareByDiscTrack,
            _ => (a, b) => 0
        };

        list.Sort(cmp);
        if (direction == SortDirection.Descending) list.Reverse();
        return list;
    }

    private static int CompareByDiscTrack(Track a, Track b)
    {
        var disc = a.DiscNumber.CompareTo(b.DiscNumber);
        if (disc != 0) return disc;
        return a.TrackNumber.CompareTo(b.TrackNumber);
    }

    private static int Str(string? a, string? b) =>
        string.Compare(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string First(IReadOnlyList<string> values) => values.Count > 0 ? values[0] : string.Empty;
}
