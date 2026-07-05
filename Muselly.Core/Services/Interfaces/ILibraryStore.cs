using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Persistence backend for the local library's track list. The default <c>JsonLibraryStore</c> keeps it in a
/// single JSON file (used by the browser head, which has no real filesystem); desktop/headless/server heads
/// swap in a SQLite-backed store for faster startup, lower memory and incremental updates. The in-memory
/// organisations (albums/artists/folders) are always rebuilt by <see cref="ILibraryService"/> from the tracks
/// this store returns, so the interface only deals in the flat track list.
/// </summary>
public interface ILibraryStore
{
    /// <summary>Prepares the store (opens/creates the database, runs any one-time migration).</summary>
    Task InitializeAsync();

    /// <summary>Returns every locally-scanned track.</summary>
    Task<List<Track>> GetAllTracksAsync();

    /// <summary>Replaces the entire stored library with <paramref name="tracks"/> (used after a scan or an
    /// artwork update — the service computes the full set and hands it here).</summary>
    Task ReplaceAllAsync(IReadOnlyList<Track> tracks);

    /// <summary>Upserts a batch of tracks without removing anything else. Used to persist a long scan
    /// incrementally so progress survives an early quit (a full scan finishes with a canonical
    /// <see cref="ReplaceAllAsync"/>).</summary>
    Task AppendAsync(IReadOnlyList<Track> tracks);

    /// <summary>Sets the artwork path on every stored track of an album (artwork apply), without a full rewrite.</summary>
    Task SetAlbumArtworkAsync(string albumKey, string artworkPath);

    // --- Lazy query surface (so the library doesn't hold every track resident) -----------------------
    // A SQLite store answers these from the database; the JSON fallback filters its in-memory list (which is
    // empty on the browser head, whose tracks come from a server and live in the library service instead).

    /// <summary>Total number of stored tracks.</summary>
    int Count();

    /// <summary>Resolves a single track by id, or null.</summary>
    Track? FindTrack(string id);

    /// <summary>A track's siblings on the same album (ordered disc/track).</summary>
    IReadOnlyList<Track> GetAlbumTracks(string albumKey);

    /// <summary>An artist's tracks (ordered album/disc/track).</summary>
    IReadOnlyList<Track> GetArtistTracks(string artistKey);

    /// <summary>Tracks directly inside a folder (ordered disc/track).</summary>
    IReadOnlyList<Track> GetFolderTracks(string directory);

    /// <summary>A page of tracks, optionally filtered by a search term (title/artist/album), ordered by title.</summary>
    IReadOnlyList<Track> Search(string? query, int offset, int limit);
}
