using Muselly.Core.Models;

namespace Muselly.Core.Util;

/// <summary>
/// Post-scan clean-up passes applied to the flat track list before the album/artist projections are built:
///   * <see cref="MergeCompilations"/> collapses multi-artist compilation albums into one "Various Artists"
///     album so they stop fragmenting into one-track albums (and bogus one-track artists).
///   * <see cref="Deduplicate"/> hides duplicate copies of the same song, keeping the highest-quality one.
/// Both are pure transforms over the list and never touch files on disk; de-dup is a display projection so
/// nothing is deleted from the persisted library.
/// </summary>
public static class TrackNormalizer
{
    /// <summary>Two songs are treated as the same recording when their durations differ by no more than this.</summary>
    public static readonly TimeSpan DefaultDuplicateTolerance = TimeSpan.FromSeconds(5);

    private const string UnknownAlbum = "Unknown Album";

    /// <summary>
    /// Reassigns the tracks of any detected compilation album to a single "Various Artists" album. A group of
    /// tracks sharing an album title within one folder is a compilation when any track carries the file's
    /// compilation flag, or when the group spans two or more distinct artists. Album titles of "Unknown
    /// Album" are never merged (those are typically loose, untagged files). Track order is preserved and each
    /// track's own artist tag is left intact — only the album grouping changes.
    /// </summary>
    public static List<Track> MergeCompilations(IReadOnlyList<Track> tracks, bool enabled)
    {
        if (!enabled || tracks.Count == 0) return ToList(tracks);

        // Group by (folder, normalised album title) to decide, per album-in-a-folder, whether it's a comp.
        var groups = new Dictionary<(string Dir, string Album), List<Track>>();
        foreach (var t in tracks)
        {
            var key = (t.Directory ?? string.Empty, Identifiers.NormalizeName(t.DisplayAlbum));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Track>();
            list.Add(t);
        }

        // Map of track id -> the merged ("Various Artists") album key it should adopt.
        var remap = new Dictionary<string, string>();
        var unknownAlbumNorm = Identifiers.NormalizeName(UnknownAlbum);
        foreach (var (key, list) in groups)
        {
            if (string.Equals(key.Album, unknownAlbumNorm, StringComparison.Ordinal)) continue;

            var flagged = false;
            var firstArtist = (string?)null;
            var multipleArtists = false;
            foreach (var t in list)
            {
                if (t.IsCompilation) flagged = true;
                var artist = Identifiers.NormalizeName(t.DisplayArtist);
                if (firstArtist is null) firstArtist = artist;
                else if (!multipleArtists && !string.Equals(artist, firstArtist, StringComparison.Ordinal))
                    multipleArtists = true;
            }

            if (!flagged && !multipleArtists) continue;

            var compKey = Identifiers.AlbumKey(Identifiers.VariousArtists, list[0].DisplayAlbum);
            foreach (var t in list) remap[t.Id] = compKey;
        }

        if (remap.Count == 0) return ToList(tracks);

        // The album moves under "Various Artists", but each track keeps pointing at its own performer (so the
        // artist link on a compilation song goes to the real artist, not to Various Artists). The artist
        // projection still lists the album under Various Artists and the track under its performer.
        var result = new List<Track>(tracks.Count);
        foreach (var t in tracks)
            result.Add(remap.TryGetValue(t.Id, out var albumKey)
                ? t.WithAlbumGrouping(Identifiers.VariousArtists, albumKey, Identifiers.ArtistKey(t.DisplayArtist))
                : t);
        return result;
    }

    /// <summary>
    /// Returns the list with duplicate copies of the same song removed, keeping the highest-quality copy of
    /// each. Songs are considered the same when their normalised title and artist match and their durations
    /// are within <paramref name="tolerance"/>. Order is preserved (the surviving copy keeps its original
    /// position). Non-destructive: this only filters the returned projection.
    /// </summary>
    public static List<Track> Deduplicate(IReadOnlyList<Track> tracks, bool enabled, TimeSpan tolerance)
    {
        if (!enabled || tracks.Count < 2) return ToList(tracks);

        var groups = new Dictionary<(string Title, string Artist), List<Track>>();
        foreach (var t in tracks)
        {
            var key = (Identifiers.NormalizeName(t.Title), Identifiers.NormalizeName(t.DisplayArtist));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Track>();
            list.Add(t);
        }

        var drop = new HashSet<string>();
        foreach (var list in groups.Values)
        {
            if (list.Count < 2) continue;

            // Cluster by duration: same-titled songs whose lengths sit within tolerance of the cluster's
            // shortest member are the same recording (e.g. a lossy + lossless rip); a markedly different
            // length (a live take, a remix) forms its own cluster and is kept.
            var byDuration = new List<Track>(list);
            byDuration.Sort((a, b) => a.Duration.CompareTo(b.Duration));

            var clusterStart = 0;
            for (var i = 1; i <= byDuration.Count; i++)
            {
                var endOfCluster = i == byDuration.Count ||
                                   byDuration[i].Duration - byDuration[clusterStart].Duration > tolerance;
                if (!endOfCluster) continue;

                if (i - clusterStart > 1)
                {
                    var best = byDuration[clusterStart];
                    for (var j = clusterStart + 1; j < i; j++)
                        if (CompareQuality(byDuration[j], best) > 0) best = byDuration[j];
                    for (var j = clusterStart; j < i; j++)
                        if (!ReferenceEquals(byDuration[j], best)) drop.Add(byDuration[j].Id);
                }
                clusterStart = i;
            }
        }

        if (drop.Count == 0) return ToList(tracks);

        var result = new List<Track>(tracks.Count - drop.Count);
        foreach (var t in tracks)
            if (!drop.Contains(t.Id)) result.Add(t);
        return result;
    }

    private static readonly HashSet<string> LosslessFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "alac", "wav", "wave", "aiff", "aif", "ape", "wv", "wavpack", "tak", "tta", "dsd", "dsf", "dff", "pcm"
    };

    private static bool IsLossless(Track t) =>
        (t.Codec is not null && LosslessFormats.Contains(t.Codec)) ||
        (!string.IsNullOrEmpty(t.Extension) && LosslessFormats.Contains(t.Extension));

    /// <summary>Orders two copies of the same song by quality: lossless beats lossy, then higher bitrate,
    /// sample rate, channel count and finally file size win. Returns &gt;0 when <paramref name="a"/> is better.</summary>
    private static int CompareQuality(Track a, Track b)
    {
        var lossless = (IsLossless(a) ? 1 : 0).CompareTo(IsLossless(b) ? 1 : 0);
        if (lossless != 0) return lossless;
        if (a.Bitrate != b.Bitrate) return a.Bitrate.CompareTo(b.Bitrate);
        if (a.SampleRate != b.SampleRate) return a.SampleRate.CompareTo(b.SampleRate);
        if (a.Channels != b.Channels) return a.Channels.CompareTo(b.Channels);
        return a.FileSizeBytes.CompareTo(b.FileSizeBytes);
    }

    private static List<Track> ToList(IReadOnlyList<Track> tracks) =>
        tracks as List<Track> ?? new List<Track>(tracks);
}
