using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Tracks the user's favourited songs, albums and artists (each with the time it was added). Persisted to
/// <c>favorites.json</c>. On the desktop these belong to the built-in local user.
/// </summary>
public interface IFavoritesService
{
    /// <summary>All favourites, newest first.</summary>
    IReadOnlyList<Favorite> Items { get; }

    event EventHandler? Changed;

    bool IsFavorite(FavoriteKind kind, string key);

    void Add(FavoriteKind kind, string key);

    void Remove(FavoriteKind kind, string key);

    /// <summary>Adds the item if missing, removes it if present. Returns the new favourited state.</summary>
    bool Toggle(FavoriteKind kind, string key);

    /// <summary>The keys of all favourites of a given kind (for filtering library views).</summary>
    IReadOnlyCollection<string> KeysOf(FavoriteKind kind);

    Task LoadAsync();
}
