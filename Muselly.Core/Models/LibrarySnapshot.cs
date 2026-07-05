namespace Muselly.Core.Models;

/// <summary>
/// The persisted form of the local library: the flat track list plus when it was last scanned. Album/artist/
/// folder organisations are not persisted — they're rebuilt in memory from the tracks on load.
/// </summary>
public sealed class LibrarySnapshot
{
    public int Version { get; set; } = 1;
    public System.DateTimeOffset ScannedAt { get; set; }
    public List<Track> Tracks { get; set; } = new();
}
