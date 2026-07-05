namespace Muselly.Core.Models;

/// <summary>
/// A node in the browse-by-directory tree. Mirrors the on-disk folder structure of the scanned roots. The
/// tracks directly inside each folder are loaded on demand via <see cref="TracksProvider"/> (keyed by
/// <see cref="Path"/>) rather than held resident; counts are pre-computed.
/// </summary>
public sealed class FolderNode
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    /// <summary>True for a configured scan root (shown at the top level of the browser).</summary>
    public bool IsRoot { get; init; }

    public List<FolderNode> Children { get; init; } = new();

    /// <summary>Number of tracks directly in this folder (not descendants); pre-computed at build time.</summary>
    public int DirectTrackCount { get; set; }

    /// <summary>Loads the tracks directly inside this folder on demand (set by the library service).</summary>
    public Func<FolderNode, IReadOnlyList<Track>>? TracksProvider { get; set; }

    /// <summary>Tracks located directly inside this folder (not in sub-folders). Loaded lazily from the store.</summary>
    public IReadOnlyList<Track> Tracks => TracksProvider?.Invoke(this) ?? System.Array.Empty<Track>();

    /// <summary>Total tracks in this folder and every descendant (from pre-computed counts).</summary>
    public int TotalTrackCount
    {
        get
        {
            var count = DirectTrackCount;
            foreach (var child in Children) count += child.TotalTrackCount;
            return count;
        }
    }
}
