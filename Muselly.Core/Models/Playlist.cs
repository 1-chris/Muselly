namespace Muselly.Core.Models;

/// <summary>
/// A user-created, persisted playlist. Stores an ordered list of track ids plus a remembered sort
/// preference. The actual <see cref="Track"/> objects are resolved against the live library at load time,
/// so a playlist survives library rescans (missing tracks are simply skipped).
/// </summary>
public sealed class Playlist
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Ordered track ids. This IS the custom order when <see cref="SortField"/> is Custom.</summary>
    public List<string> TrackIds { get; init; } = new();

    public TrackSortField SortField { get; set; } = TrackSortField.Custom;

    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;

    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    public int TrackCount => TrackIds.Count;
}
