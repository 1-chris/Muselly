using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>A single play event: which track, and when it started.</summary>
public sealed class ListeningHistoryEntry
{
    public required string TrackId { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Records the local user's listening history (most-recent first, capped). Persisted to
/// <c>listening-history.json</c>. Visibility to others is governed by the user's privacy settings.
/// </summary>
public interface IListeningHistoryService
{
    IReadOnlyList<ListeningHistoryEntry> Recent { get; }

    event EventHandler? Changed;

    void Record(Track track);

    Task LoadAsync();
}
