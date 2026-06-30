using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// The music library: owns the scanned tracks and the album / artist / folder organisations derived from
/// them. It loads a cached snapshot at startup (fast) and can rescan the configured folders on demand,
/// reporting progress. Derived collections are rebuilt in memory from the flat track list, so persistence
/// is just the track list.
/// </summary>
public interface ILibraryService
{
    IReadOnlyList<Track> Tracks { get; }

    IReadOnlyList<Album> Albums { get; }

    IReadOnlyList<Artist> Artists { get; }

    /// <summary>The roots of the browse-by-directory tree (one per configured scan folder).</summary>
    IReadOnlyList<FolderNode> Folders { get; }

    IReadOnlyList<string> Genres { get; }

    bool IsScanning { get; }

    /// <summary>True while the cached library is being loaded from disk at startup (before the first
    /// <see cref="LibraryChanged"/>). Lets the UI show a "loading" state rather than "empty".</summary>
    bool IsLoading { get; }

    /// <summary>Raised (possibly off the UI thread) whenever the library content changes.</summary>
    event EventHandler? LibraryChanged;

    /// <summary>Raised (possibly off the UI thread) as a scan progresses.</summary>
    event EventHandler<ScanProgress>? ScanProgressChanged;

    Track? FindTrack(string id);

    Album? FindAlbum(string key);

    Artist? FindArtist(string key);

    /// <summary>Resolves track ids to live tracks, skipping any that no longer exist (preserving order).</summary>
    IReadOnlyList<Track> ResolveTracks(IEnumerable<string> ids);

    /// <summary>Loads the persisted snapshot from disk and rebuilds the organisations. Fast; no file I/O on audio.</summary>
    Task LoadAsync();

    /// <summary>Re-applies the compilation-merge and de-duplication passes and rebuilds the organisations from
    /// the current tracks, without re-reading files. Used when a related setting changes. Raises
    /// <see cref="LibraryChanged"/>.</summary>
    void RefreshOrganization();

    /// <summary>Full rescan: re-reads every file in every configured folder, so new files are added, edits
    /// are picked up and tracks whose files are gone are removed. Previously-fetched album art is preserved.
    /// Rebuilds the organisations and persists.</summary>
    Task ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>Incremental scan: reads only files not already in the library and appends them, leaving
    /// existing tracks (and nothing removed) untouched. Fast for adding newly-added songs.</summary>
    Task ScanNewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans a single folder, merging the result into the existing local library: tracks already known under
    /// <paramref name="folder"/> are replaced with the fresh scan, while tracks under other folders are left
    /// untouched. Used when adding a folder or rescanning just one, so the whole library isn't re-read.
    /// </summary>
    Task ScanFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the cached artwork path on every track of the given album, rebuilds the organisations and
    /// persists the change. Used by the "scan for missing album art" feature. Raises <see cref="LibraryChanged"/>.
    /// </summary>
    Task ApplyAlbumArtworkAsync(string albumKey, string artworkPath);

    /// <summary>
    /// Merges tracks streamed from a connected remote server into the in-memory library (keyed by
    /// <paramref name="serverId"/>). Remote tracks are not persisted to <c>library.json</c>; only the local
    /// scan is. Replaces any previously added set for the same server. Raises <see cref="LibraryChanged"/>.
    /// </summary>
    void AddRemoteTracks(string serverId, IReadOnlyList<Track> tracks);

    /// <summary>Removes all tracks contributed by a remote server (on disconnect). Raises <see cref="LibraryChanged"/>.</summary>
    void RemoveRemoteSource(string serverId);
}
