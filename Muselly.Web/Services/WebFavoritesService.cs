using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Web.Services;

/// <summary>
/// Browser <see cref="IFavoritesService"/> backed by the host's per-user API, so the shared Favourites UI
/// reflects the signed-in account's server-side favourites. Updates are optimistic (applied locally at once,
/// then sent to the host) and the set is (re)loaded on sign-in.
/// </summary>
public sealed class WebFavoritesService : IFavoritesService
{
    private readonly WebHostClient _client;
    private readonly WebSession _session;
    private readonly object _gate = new();
    private List<Favorite> _items = new();
    private readonly HashSet<(FavoriteKind, string)> _set = new();

    public WebFavoritesService(WebHostClient client, WebSession session)
    {
        _client = client;
        _session = session;
        _session.Changed += (_, _) => _ = LoadAsync();
    }

    public IReadOnlyList<Favorite> Items { get { lock (_gate) return _items.ToList(); } }

    public event EventHandler? Changed;

    public bool IsFavorite(FavoriteKind kind, string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        lock (_gate) return _set.Contains((kind, key));
    }

    public void Add(FavoriteKind kind, string key) => _ = SetAsync(kind, key, true);
    public void Remove(FavoriteKind kind, string key) => _ = SetAsync(kind, key, false);

    public bool Toggle(FavoriteKind kind, string key)
    {
        var target = !IsFavorite(kind, key);
        _ = SetAsync(kind, key, target);
        return target;
    }

    public IReadOnlyCollection<string> KeysOf(FavoriteKind kind)
    {
        lock (_gate) return _items.Where(f => f.Kind == kind).Select(f => f.Key).ToList();
    }

    public Task LoadAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        if (!_session.IsAuthenticated || _session.Role == UserRole.Guest)
        {
            lock (_gate) { _items.Clear(); _set.Clear(); }
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var dto = await _client.GetFavoritesAsync(_session.Username).ConfigureAwait(false);
        lock (_gate)
        {
            _items = dto?.Favorites.Select(f => new Favorite { Kind = f.Kind, Key = f.Key }).ToList() ?? new();
            _set.Clear();
            foreach (var f in _items) _set.Add((f.Kind, f.Key));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SetAsync(FavoriteKind kind, string key, bool on)
    {
        if (string.IsNullOrEmpty(key)) return;

        bool changed;
        lock (_gate)
        {
            if (on) { changed = _set.Add((kind, key)); if (changed) _items.Insert(0, new Favorite { Kind = kind, Key = key }); }
            else { changed = _set.Remove((kind, key)); if (changed) _items.RemoveAll(f => f.Kind == kind && f.Key == key); }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);

        await _client.SetFavoriteAsync(kind, key, on).ConfigureAwait(false);
    }
}
