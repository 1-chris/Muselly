using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Per-user favourites and listening history on a server, keyed by username. The built-in local user's data
/// is the host's own (shared with the desktop's <see cref="IFavoritesService"/> / <see cref="IListeningHistoryService"/>);
/// every other account gets its own persisted store. Privacy/permission checks live a layer up (the API).
/// </summary>
public interface IUserDataStore
{
    IReadOnlyList<Favorite> GetFavorites(string username);

    /// <summary>Adds/removes a favourite; returns the new favourited state.</summary>
    bool ToggleFavorite(string username, FavoriteKind kind, string key);

    void SetFavorite(string username, FavoriteKind kind, string key, bool favorite);

    IReadOnlyList<ListeningHistoryEntry> GetHistory(string username);

    void RecordPlay(string username, string trackId);
}
