using System.Collections.Concurrent;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// Default <see cref="IFavoritesService"/>. Keeps favourites in memory keyed by (kind, key) and persists the
/// flat list to <c>favorites.json</c>. Changes raise <see cref="Changed"/> and save asynchronously.
/// </summary>
public sealed class FavoritesService : IFavoritesService
{
    private readonly object _gate = new();
    // (kind,key) -> Favorite. A concurrent dict keeps lookups cheap for "is this favourited?" checks in lists.
    private readonly ConcurrentDictionary<(FavoriteKind, string), Favorite> _byKey = new();

    public IReadOnlyList<Favorite> Items
    {
        get
        {
            var list = new List<Favorite>(_byKey.Values);
            list.Sort((a, b) => b.AddedAt.CompareTo(a.AddedAt));
            return list;
        }
    }

    public event EventHandler? Changed;

    public bool IsFavorite(FavoriteKind kind, string key) =>
        !string.IsNullOrEmpty(key) && _byKey.ContainsKey((kind, key));

    public void Add(FavoriteKind kind, string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_byKey.TryAdd((kind, key), new Favorite { Kind = kind, Key = key, AddedAt = DateTimeOffset.Now })) return;
        OnChanged();
    }

    public void Remove(FavoriteKind kind, string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!_byKey.TryRemove((kind, key), out _)) return;
        OnChanged();
    }

    public bool Toggle(FavoriteKind kind, string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (_byKey.ContainsKey((kind, key)))
        {
            Remove(kind, key);
            return false;
        }
        Add(kind, key);
        return true;
    }

    public IReadOnlyCollection<string> KeysOf(FavoriteKind kind)
    {
        var set = new HashSet<string>();
        foreach (var (k, fav) in _byKey)
            if (k.Item1 == kind) set.Add(fav.Key);
        return set;
    }

    public Task LoadAsync() => Task.Run(() =>
    {
        var snapshot = JsonStore.Load(StoragePaths.FavoritesFile(), () => new FavoritesSnapshot());
        _byKey.Clear();
        foreach (var f in snapshot.Favorites)
            if (!string.IsNullOrEmpty(f.Key))
                _byKey[(f.Kind, f.Key)] = f;
        Changed?.Invoke(this, EventArgs.Empty);
    });

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        var snapshot = new FavoritesSnapshot { Favorites = new List<Favorite>(_byKey.Values) };
        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                try { JsonStore.Save(StoragePaths.FavoritesFile(), snapshot); }
                catch { /* best-effort */ }
            }
        });
    }

    private sealed class FavoritesSnapshot
    {
        public int Version { get; set; } = 1;
        public List<Favorite> Favorites { get; set; } = new();
    }
}
