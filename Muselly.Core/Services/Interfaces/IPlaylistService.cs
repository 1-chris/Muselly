using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Manages user playlists: create, rename, delete, add/remove/reorder tracks, randomise ordering and
/// sort by any field. Playlists persist to <c>playlists.json</c> as ordered track-id lists and resolve to
/// live tracks against the <see cref="ILibraryService"/>.
/// </summary>
public interface IPlaylistService
{
    IReadOnlyList<Playlist> Playlists { get; }

    event EventHandler? Changed;

    Task LoadAsync();

    Playlist Create(string name);

    void Rename(string id, string name);

    void Delete(string id);

    void AddTracks(string id, IReadOnlyList<string> trackIds);

    void RemoveTrackAt(string id, int index);

    void Move(string id, int fromIndex, int toIndex);

    /// <summary>Shuffles the stored order and switches the playlist to custom ordering.</summary>
    void Randomize(string id);

    /// <summary>Reorders the stored ids by the given field/direction and remembers the preference.</summary>
    void SetSort(string id, TrackSortField field, SortDirection direction);

    /// <summary>Resolves a playlist's tracks to live library tracks in stored order.</summary>
    IReadOnlyList<Track> ResolveTracks(string id);

    void Save();
}
