using System.Collections.Concurrent;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Server.Auth;

/// <summary>
/// Per-user favourites + listening history for the server. The built-in local user's data is delegated to
/// the host's own <see cref="IFavoritesService"/> / <see cref="IListeningHistoryService"/> so it stays unified
/// with the desktop UI; every other account is persisted to its own file (<c>userdata/&lt;hash&gt;.json</c>).
/// </summary>
public sealed class ServerUserDataStore : IUserDataStore
{
    private const int MaxHistory = 500;

    private readonly IFavoritesService _hostFavorites;
    private readonly IListeningHistoryService _hostHistory;
    private readonly IUserService _users;
    private readonly ConcurrentDictionary<string, UserData> _byUser = new(StringComparer.OrdinalIgnoreCase);

    public ServerUserDataStore(IFavoritesService hostFavorites, IListeningHistoryService hostHistory, IUserService users)
    {
        _hostFavorites = hostFavorites;
        _hostHistory = hostHistory;
        _users = users;
    }

    private bool IsBuiltIn(string username) =>
        string.Equals(username, _users.Current.Username, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<Favorite> GetFavorites(string username) =>
        IsBuiltIn(username) ? _hostFavorites.Items : Load(username).Favorites.ToList();

    public bool ToggleFavorite(string username, FavoriteKind kind, string key)
    {
        if (IsBuiltIn(username)) return _hostFavorites.Toggle(kind, key);

        var data = Load(username);
        lock (data.Gate)
        {
            var existing = data.Favorites.FindIndex(f => f.Kind == kind && f.Key == key);
            if (existing >= 0) { data.Favorites.RemoveAt(existing); Save(username, data); return false; }
            data.Favorites.Insert(0, new Favorite { Kind = kind, Key = key, AddedAt = DateTimeOffset.Now });
            Save(username, data);
            return true;
        }
    }

    public void SetFavorite(string username, FavoriteKind kind, string key, bool favorite)
    {
        if (IsBuiltIn(username))
        {
            if (favorite) _hostFavorites.Add(kind, key); else _hostFavorites.Remove(kind, key);
            return;
        }
        var data = Load(username);
        lock (data.Gate)
        {
            var existing = data.Favorites.FindIndex(f => f.Kind == kind && f.Key == key);
            if (favorite && existing < 0) data.Favorites.Insert(0, new Favorite { Kind = kind, Key = key });
            else if (!favorite && existing >= 0) data.Favorites.RemoveAt(existing);
            else return;
            Save(username, data);
        }
    }

    public IReadOnlyList<ListeningHistoryEntry> GetHistory(string username) =>
        IsBuiltIn(username) ? _hostHistory.Recent : Load(username).History.ToList();

    public void RecordPlay(string username, string trackId)
    {
        if (string.IsNullOrEmpty(trackId)) return;
        if (IsBuiltIn(username)) return; // the host records its own plays through the coordinator

        var data = Load(username);
        lock (data.Gate)
        {
            if (data.History.Count > 0 && data.History[0].TrackId == trackId &&
                DateTimeOffset.Now - data.History[0].PlayedAt < TimeSpan.FromSeconds(20))
                return;
            data.History.Insert(0, new ListeningHistoryEntry { TrackId = trackId, PlayedAt = DateTimeOffset.Now });
            if (data.History.Count > MaxHistory) data.History.RemoveRange(MaxHistory, data.History.Count - MaxHistory);
            Save(username, data);
        }
    }

    private UserData Load(string username) => _byUser.GetOrAdd(username, u =>
    {
        var file = JsonStore.Load(StoragePaths.UserDataFile(u), () => new UserDataFile());
        return new UserData { Favorites = file.Favorites ?? new(), History = file.History ?? new() };
    });

    private static void Save(string username, UserData data)
    {
        var file = new UserDataFile { Favorites = data.Favorites, History = data.History };
        try { JsonStore.Save(StoragePaths.UserDataFile(username), file); }
        catch { /* best-effort */ }
    }

    private sealed class UserData
    {
        public readonly object Gate = new();
        public List<Favorite> Favorites { get; init; } = new();
        public List<ListeningHistoryEntry> History { get; init; } = new();
    }

    private sealed class UserDataFile
    {
        public int Version { get; set; } = 1;
        public List<Favorite> Favorites { get; set; } = new();
        public List<ListeningHistoryEntry> History { get; set; } = new();
    }
}
