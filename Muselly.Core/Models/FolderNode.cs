namespace Muselly.Core.Models;

/// <summary>
/// A node in the browse-by-directory tree. Mirrors the on-disk folder structure of the scanned roots,
/// carrying the tracks contained directly in each folder plus its child folders.
/// </summary>
public sealed class FolderNode
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    /// <summary>True for a configured scan root (shown at the top level of the browser).</summary>
    public bool IsRoot { get; init; }

    public List<FolderNode> Children { get; init; } = new();

    /// <summary>Tracks located directly inside this folder (not in sub-folders).</summary>
    public List<Track> Tracks { get; init; } = new();

    /// <summary>Total tracks in this folder and every descendant.</summary>
    public int TotalTrackCount
    {
        get
        {
            var count = Tracks.Count;
            foreach (var child in Children) count += child.TotalTrackCount;
            return count;
        }
    }
}
