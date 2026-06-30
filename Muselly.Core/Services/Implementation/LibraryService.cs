using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Muselly.Core.Audio;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// Default <see cref="ILibraryService"/>. Holds the flat track list and rebuilds the album / artist /
/// folder organisations from it. Only the track list is persisted (to <c>library.json</c>); the
/// organisations are pure projections rebuilt on load and after each scan.
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private readonly ISettingsService _settings;
    private readonly IMetadataReader _metadata;
    private readonly ILogger<LibraryService> _logger;
    private readonly object _gate = new();

    // Local (persisted) tracks and per-server remote (in-memory only) tracks are kept apart so a rescan or
    // an album-art update never clobbers merged remote content, and disconnecting a server removes only its
    // tracks. The public projections are rebuilt from the combined set.
    private List<Track> _localTracks = new();
    private readonly Dictionary<string, IReadOnlyList<Track>> _remoteTracks = new();

    private List<Track> _tracks = new();
    private Dictionary<string, Track> _tracksById = new();
    private List<Album> _albums = new();
    private Dictionary<string, Album> _albumsByKey = new();
    private List<Artist> _artists = new();
    private Dictionary<string, Artist> _artistsByKey = new();
    private List<FolderNode> _folders = new();
    private List<string> _genres = new();

    public LibraryService(ISettingsService settings, IMetadataReader metadata, ILogger<LibraryService> logger)
    {
        _settings = settings;
        _metadata = metadata;
        _logger = logger;
    }

    public IReadOnlyList<Track> Tracks => _tracks;
    public IReadOnlyList<Album> Albums => _albums;
    public IReadOnlyList<Artist> Artists => _artists;
    public IReadOnlyList<FolderNode> Folders => _folders;
    public IReadOnlyList<string> Genres => _genres;

    public bool IsScanning { get; private set; }

    public bool IsLoading { get; private set; } = true;

    public event EventHandler? LibraryChanged;
    public event EventHandler<ScanProgress>? ScanProgressChanged;

    public Track? FindTrack(string id) => _tracksById.GetValueOrDefault(id);
    public Album? FindAlbum(string key) => _albumsByKey.GetValueOrDefault(key);
    public Artist? FindArtist(string key) => _artistsByKey.GetValueOrDefault(key);

    public IReadOnlyList<Track> ResolveTracks(IEnumerable<string> ids)
    {
        var list = new List<Track>();
        foreach (var id in ids)
            if (_tracksById.TryGetValue(id, out var t))
                list.Add(t);
        return list;
    }

    public Task LoadAsync() => Task.Run(() =>
    {
        IsLoading = true;
        try
        {
            var snapshot = JsonStore.Load(StoragePaths.LibraryCacheFile(), () => new LibrarySnapshot());
            _localTracks = snapshot.Tracks ?? new List<Track>();
            RebuildCombined();
        }
        finally
        {
            IsLoading = false;
        }
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    });

    public Task ScanAsync(CancellationToken cancellationToken = default) =>
        ScanInternalAsync(ScanMode.Full, cancellationToken);

    public Task ScanNewAsync(CancellationToken cancellationToken = default) =>
        ScanInternalAsync(ScanMode.NewOnly, cancellationToken);

    private async Task ScanInternalAsync(ScanMode mode, CancellationToken cancellationToken)
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            var folders = new List<string>(_settings.Current.MusicFolders);
            var recurse = _settings.Current.ScanSubdirectories;

            Report(ScanPhase.Discovering, 0, 0, null);

            // Enumerate off the calling thread: walking a large or network folder tree is slow and must never
            // block the UI (this runs synchronously before the first real await otherwise).
            var files = await Task.Run(() =>
            {
                var list = new List<string>();
                foreach (var folder in folders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    list.AddRange(EnumerateAudioFiles(folder, recurse));
                }
                return list;
            }, cancellationToken).ConfigureAwait(false);

            List<Track> finalLocal;
            if (mode == ScanMode.NewOnly)
            {
                // Incremental: only read files we don't already have, and never drop existing tracks. Existing
                // tracks (and their fetched artwork) are kept untouched; new files are read and appended.
                List<Track> existing;
                lock (_gate) existing = _localTracks;

                var existingSources = new HashSet<string>(existing.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var t in existing) existingSources.Add(t.Source);

                var toRead = new List<string>();
                foreach (var f in files)
                    if (!existingSources.Contains(f)) toRead.Add(f);

                var scanned = LinkCachedArtwork(await ReadTracksAsync(toRead, cancellationToken).ConfigureAwait(false));

                var ids = new HashSet<string>(existing.Count);
                foreach (var t in existing) ids.Add(t.Id);
                var merged = new List<Track>(existing.Count + scanned.Count);
                merged.AddRange(existing);
                foreach (var t in scanned)
                    if (ids.Add(t.Id)) merged.Add(t);   // skip anything we already had (path normalisation, etc.)
                finalLocal = merged;
            }
            else
            {
                // Full: re-read everything (so removed files drop out and edits are picked up), then re-link
                // any previously-fetched album art from the artwork cache so it isn't lost.
                finalLocal = LinkCachedArtwork(await ReadTracksAsync(files, cancellationToken).ConfigureAwait(false));
            }

            lock (_gate) _localTracks = finalLocal;
            RebuildCombined();

            JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot
            {
                Tracks = finalLocal,
                ScannedAt = DateTimeOffset.Now
            });

            Report(ScanPhase.Completed, finalLocal.Count, finalLocal.Count, null);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Report(ScanPhase.Idle, 0, 0, null);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task ScanFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (IsScanning || string.IsNullOrWhiteSpace(folder)) return;
        IsScanning = true;
        try
        {
            var recurse = _settings.Current.ScanSubdirectories;
            var full = Path.GetFullPath(folder);

            Report(ScanPhase.Discovering, 0, 0, null);
            // Enumerate off the calling thread so a slow/network folder never blocks the UI.
            var files = await Task.Run(() => EnumerateAudioFiles(full, recurse), cancellationToken).ConfigureAwait(false);

            var scanned = LinkCachedArtwork(await ReadTracksAsync(files, cancellationToken).ConfigureAwait(false));

            // Merge: drop any existing local tracks living under this folder, then add the fresh scan. Tracks
            // under other folders (and all remote tracks) are left untouched.
            List<Track> merged;
            lock (_gate)
            {
                merged = new List<Track>(_localTracks.Count + scanned.Count);
                foreach (var t in _localTracks)
                    if (!IsUnderOrEqual(t.Directory, full))
                        merged.Add(t);
                merged.AddRange(scanned);
                _localTracks = merged;
            }

            RebuildCombined();

            JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot
            {
                Tracks = merged,
                ScannedAt = DateTimeOffset.Now
            });

            Report(ScanPhase.Completed, scanned.Count, scanned.Count, null);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Report(ScanPhase.Idle, 0, 0, null);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private List<string> EnumerateAudioFiles(string folder, bool recurse)
    {
        var files = new List<string>();
        if (!Directory.Exists(folder)) return files;
        try
        {
            var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(folder, "*", option))
                if (AudioFormats.IsSupported(file))
                    files.Add(file);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate {Folder}", folder);
        }
        return files;
    }

    private async Task<List<Track>> ReadTracksAsync(List<string> files, CancellationToken cancellationToken)
    {
        var total = files.Count;
        Report(ScanPhase.Reading, 0, total, null);

        var results = new ConcurrentBag<Track>();
        var processed = 0;
        // Throttle progress events: at most ~one per 1% (or per file for small libraries).
        var step = Math.Max(1, total / 100);

        var parallel = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
        };

        await Task.Run(() => Parallel.ForEach(files, parallel, file =>
        {
            var track = _metadata.Read(file);
            if (track is not null) results.Add(track);

            var done = Interlocked.Increment(ref processed);
            if (done % step == 0 || done == total)
                Report(ScanPhase.Reading, done, total, Path.GetFileName(file));
        }), cancellationToken).ConfigureAwait(false);

        Report(ScanPhase.Organizing, total, total, null);

        var read = results.Count;
        var skipped = total - read;
        if (skipped > 0)
            _logger.LogWarning(
                "Scan: {Skipped} of {Total} files could not be read and were skipped. On a macOS SMB share " +
                "this is usually filenames in NFD (decomposed Unicode) form, which macOS cannot open over " +
                "SMB; converting the names to NFC on the server (e.g. `convmv -f utf8 -t utf8 --nfc -r`) fixes it.",
                skipped, total);

        return new List<Track>(results);
    }

    private static readonly string[] ArtworkExtensions = { "jpg", "jpeg", "png", "webp", "gif", "bmp" };

    /// <summary>
    /// Fills in artwork for freshly-scanned tracks that have none, by re-linking a cover already sitting in
    /// the artwork cache for that album key. Covers are cached as <c>&lt;albumKey&gt;.&lt;ext&gt;</c> by both
    /// the embedded-art extractor and the web art fetcher, so this re-attaches art fetched from the internet
    /// (and any previously-applied cover) that a full rescan would otherwise lose — without overwriting art
    /// the scan just extracted from the file itself.
    /// </summary>
    private static List<Track> LinkCachedArtwork(List<Track> tracks)
    {
        if (tracks.Count == 0) return tracks;

        var dir = StoragePaths.ArtworkCacheDirectory();
        var byKey = new Dictionary<string, string?>();
        var result = new List<Track>(tracks.Count);
        var changed = false;

        foreach (var t in tracks)
        {
            if ((!string.IsNullOrEmpty(t.ArtworkPath) && File.Exists(t.ArtworkPath)) || string.IsNullOrEmpty(t.AlbumKey))
            {
                result.Add(t);
                continue;
            }

            if (!byKey.TryGetValue(t.AlbumKey, out var found))
                byKey[t.AlbumKey] = found = FindCachedArtwork(dir, t.AlbumKey);

            if (found is not null)
            {
                result.Add(t.WithArtwork(found));
                changed = true;
            }
            else
            {
                result.Add(t);
            }
        }

        return changed ? result : tracks;
    }

    private static string? FindCachedArtwork(string dir, string albumKey)
    {
        foreach (var ext in ArtworkExtensions)
        {
            var path = Path.Combine(dir, albumKey + "." + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public Task ApplyAlbumArtworkAsync(string albumKey, string artworkPath) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(albumKey) || string.IsNullOrEmpty(artworkPath)) return;

        // The album key may be a merged ("Various Artists") key that no raw persisted track carries verbatim,
        // so resolve the album's member ids from the current projection and stamp artwork onto those tracks.
        // Fall back to a direct key match for plain (unmerged) albums.
        var album = FindAlbum(albumKey);
        var ids = album is null
            ? new HashSet<string>()
            : album.Tracks.Select(t => t.Id).ToHashSet();

        List<Track> updated;
        lock (_gate)
        {
            var changed = false;
            updated = new List<Track>(_localTracks.Count);
            foreach (var t in _localTracks)
            {
                if ((ids.Contains(t.Id) || t.AlbumKey == albumKey) && t.ArtworkPath != artworkPath)
                {
                    updated.Add(t.WithArtwork(artworkPath));
                    changed = true;
                }
                else
                {
                    updated.Add(t);
                }
            }
            if (!changed) return;
            _localTracks = updated;
        }

        RebuildCombined();
        JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot
        {
            Tracks = updated,
            ScannedAt = DateTimeOffset.Now
        });
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    });

    public void AddRemoteTracks(string serverId, IReadOnlyList<Track> tracks)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        lock (_gate)
        {
            _remoteTracks[serverId] = tracks ?? Array.Empty<Track>();
        }
        RebuildCombined();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRemoteSource(string serverId)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        bool removed;
        lock (_gate)
        {
            removed = _remoteTracks.Remove(serverId);
        }
        if (!removed) return;
        RebuildCombined();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuilds the public projections from the local scan plus every connected server's tracks.</summary>
    private void RebuildCombined()
    {
        List<Track> combined;
        lock (_gate)
        {
            combined = new List<Track>(_localTracks.Count + _remoteTracks.Values.Sum(v => v.Count));
            combined.AddRange(_localTracks);
            foreach (var set in _remoteTracks.Values) combined.AddRange(set);
        }

        // Apply the compilation merge as a projection over the raw (persisted) tracks, so the setting can be
        // toggled without a rescan and the underlying files/keys are never mutated.
        var merged = TrackNormalizer.MergeCompilations(combined, _settings.Current.MergeCompilationAlbums);
        Rebuild(merged);
    }

    /// <summary>Re-applies the compilation/de-duplication passes and rebuilds every projection from the
    /// current tracks. Cheap (no file I/O); used when a related setting changes so it takes effect at once.</summary>
    public void RefreshOrganization()
    {
        RebuildCombined();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Report(ScanPhase phase, int processed, int total, string? item) =>
        ScanProgressChanged?.Invoke(this, new ScanProgress
        {
            Phase = phase,
            Processed = processed,
            Total = total,
            CurrentItem = item
        });

    // --- Organisation building -----------------------------------------------------------------------

    private void Rebuild(List<Track> tracks)
    {
        // Every individual file stays resolvable by id (queue/playlist links, and copies hidden by de-dup).
        var byId = new Dictionary<string, Track>(tracks.Count);
        foreach (var t in tracks) byId[t.Id] = t;

        // De-dup is a display projection: the Songs/Albums/Artists views show one copy per song, while the
        // Folder tree (and the persisted library) still reflects every file on disk.
        var deduped = TrackNormalizer.Deduplicate(tracks, _settings.Current.DeduplicateTracks,
            TrackNormalizer.DefaultDuplicateTolerance);

        var albums = BuildAlbums(deduped, out var albumsByKey);
        var artists = BuildArtists(albums, deduped, out var artistsByKey);
        var folders = BuildFolders(tracks);
        var genres = BuildGenres(tracks);

        lock (_gate)
        {
            _tracks = deduped;
            _tracksById = byId;
            _albums = albums;
            _albumsByKey = albumsByKey;
            _artists = artists;
            _artistsByKey = artistsByKey;
            _folders = folders;
            _genres = genres;
        }
    }

    private static List<Album> BuildAlbums(List<Track> tracks, out Dictionary<string, Album> byKey)
    {
        var groups = new Dictionary<string, List<Track>>();
        foreach (var t in tracks)
        {
            if (!groups.TryGetValue(t.AlbumKey, out var list))
                groups[t.AlbumKey] = list = new List<Track>();
            list.Add(t);
        }

        var albums = new List<Album>(groups.Count);
        byKey = new Dictionary<string, Album>(groups.Count);

        foreach (var (key, list) in groups)
        {
            list.Sort(CompareForAlbum);

            var genres = new List<string>();
            uint year = 0;
            string? label = null;
            string? artwork = null;
            foreach (var t in list)
            {
                if (year == 0 && t.Year > 0) year = t.Year;
                label ??= t.Label;
                artwork ??= t.ArtworkPath;
                foreach (var g in t.Genres)
                    if (!genres.Contains(g)) genres.Add(g);
            }

            var album = new Album
            {
                Key = key,
                Title = list[0].DisplayAlbum,
                AlbumArtist = list[0].AlbumArtist ?? list[0].DisplayArtist,
                Year = year,
                Genres = genres,
                Label = label,
                ArtworkPath = artwork,
                Tracks = list
            };
            albums.Add(album);
            byKey[key] = album;
        }

        albums.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        return albums;
    }

    private static List<Artist> BuildArtists(List<Album> albums, List<Track> tracks, out Dictionary<string, Artist> byKey)
    {
        // Albums group under their album-artist (so a "Various Artists" compilation appears as one VA album).
        var albumGroups = new Dictionary<string, List<Album>>();
        foreach (var a in albums)
        {
            var key = Identifiers.ArtistKey(a.AlbumArtist);
            if (!albumGroups.TryGetValue(key, out var list))
                albumGroups[key] = list = new List<Album>();
            list.Add(a);
        }

        // Tracks group under their own performer (Track.ArtistKey). For ordinary albums this equals the
        // album-artist; for a merged compilation it's the real per-track artist, so each performer still gets
        // an artist entry containing their track even though the album lives under Various Artists.
        var trackGroups = new Dictionary<string, List<Track>>();
        var nameByKey = new Dictionary<string, string>();
        foreach (var t in tracks)
        {
            var key = t.ArtistKey;
            if (string.IsNullOrEmpty(key)) continue;
            if (!trackGroups.TryGetValue(key, out var list))
                trackGroups[key] = list = new List<Track>();
            list.Add(t);
            if (!nameByKey.ContainsKey(key)) nameByKey[key] = t.DisplayArtist;
        }

        // An artist exists if it's an album-artist, a track performer, or both.
        var keys = new HashSet<string>(albumGroups.Keys);
        keys.UnionWith(trackGroups.Keys);

        var artists = new List<Artist>(keys.Count);
        byKey = new Dictionary<string, Artist>(keys.Count);

        foreach (var key in keys)
        {
            var albumList = albumGroups.GetValueOrDefault(key) ?? new List<Album>();
            albumList.Sort((a, b) => b.Year.CompareTo(a.Year));
            var trackList = trackGroups.GetValueOrDefault(key) ?? new List<Track>();

            string? artwork = null;
            foreach (var a in albumList) { if (artwork is not null) break; artwork = a.ArtworkPath; }
            if (artwork is null)
                foreach (var t in trackList) { if (t.ArtworkPath is not null) { artwork = t.ArtworkPath; break; } }

            var artist = new Artist
            {
                Key = key,
                Name = albumList.Count > 0 ? albumList[0].AlbumArtist : nameByKey.GetValueOrDefault(key, "Unknown Artist"),
                ArtworkPath = artwork,
                Albums = albumList,
                Tracks = trackList
            };
            artists.Add(artist);
            byKey[key] = artist;
        }

        artists.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return artists;
    }

    private List<FolderNode> BuildFolders(List<Track> tracks)
    {
        var roots = new List<FolderNode>();
        var nodesByPath = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in _settings.Current.MusicFolders)
        {
            var full = Path.GetFullPath(rootPath);
            if (nodesByPath.ContainsKey(full)) continue;
            var node = new FolderNode { Name = NiceName(full), Path = full, IsRoot = true };
            nodesByPath[full] = node;
            roots.Add(node);
        }

        foreach (var track in tracks)
        {
            var dir = track.Directory;
            if (string.IsNullOrEmpty(dir)) continue;

            var root = roots.Find(r => IsUnderOrEqual(dir, r.Path));
            if (root is null) continue;

            var node = EnsureFolderChain(root, dir, nodesByPath);
            node.Tracks.Add(track);
        }

        foreach (var node in nodesByPath.Values)
            node.Tracks.Sort(CompareForAlbum);

        roots.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return roots;
    }

    private static FolderNode EnsureFolderChain(FolderNode root, string targetDir, Dictionary<string, FolderNode> nodesByPath)
    {
        if (string.Equals(targetDir, root.Path, StringComparison.OrdinalIgnoreCase))
            return root;

        var relative = Path.GetRelativePath(root.Path, targetDir);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var current = root;
        var currentPath = root.Path;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || part == ".") continue;
            currentPath = Path.Combine(currentPath, part);
            if (!nodesByPath.TryGetValue(currentPath, out var child))
            {
                child = new FolderNode { Name = part, Path = currentPath };
                nodesByPath[currentPath] = child;
                current.Children.Add(child);
                current.Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            current = child;
        }
        return current;
    }

    private static List<string> BuildGenres(List<Track> tracks)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tracks)
            foreach (var g in t.Genres)
                if (!string.IsNullOrWhiteSpace(g)) set.Add(g);
        return new List<string>(set);
    }

    private static bool IsUnderOrEqual(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string NiceName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static int CompareForAlbum(Track a, Track b)
    {
        var disc = a.DiscNumber.CompareTo(b.DiscNumber);
        if (disc != 0) return disc;
        var track = a.TrackNumber.CompareTo(b.TrackNumber);
        if (track != 0) return track;
        return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LibrarySnapshot
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset ScannedAt { get; set; }
        public List<Track> Tracks { get; set; } = new();
    }
}
