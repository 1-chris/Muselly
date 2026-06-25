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
        var snapshot = JsonStore.Load(StoragePaths.LibraryCacheFile(), () => new LibrarySnapshot());
        _localTracks = snapshot.Tracks ?? new List<Track>();
        RebuildCombined();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    });

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            var folders = new List<string>(_settings.Current.MusicFolders);
            var recurse = _settings.Current.ScanSubdirectories;

            Report(ScanPhase.Discovering, 0, 0, null);

            var files = new List<string>();
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.AddRange(EnumerateAudioFiles(folder, recurse));
            }

            var scanned = await ReadTracksAsync(files, cancellationToken).ConfigureAwait(false);

            _localTracks = scanned;
            RebuildCombined();

            JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot
            {
                Tracks = scanned,
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

    public async Task ScanFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        if (IsScanning || string.IsNullOrWhiteSpace(folder)) return;
        IsScanning = true;
        try
        {
            var recurse = _settings.Current.ScanSubdirectories;
            var full = Path.GetFullPath(folder);

            Report(ScanPhase.Discovering, 0, 0, null);
            var files = EnumerateAudioFiles(full, recurse);

            var scanned = await ReadTracksAsync(files, cancellationToken).ConfigureAwait(false);

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
        return new List<Track>(results);
    }

    public Task ApplyAlbumArtworkAsync(string albumKey, string artworkPath) => Task.Run(() =>
    {
        if (string.IsNullOrEmpty(albumKey) || string.IsNullOrEmpty(artworkPath)) return;

        List<Track> updated;
        lock (_gate)
        {
            var changed = false;
            updated = new List<Track>(_localTracks.Count);
            foreach (var t in _localTracks)
            {
                if (t.AlbumKey == albumKey && t.ArtworkPath != artworkPath)
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
        Rebuild(combined);
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
        var byId = new Dictionary<string, Track>(tracks.Count);
        foreach (var t in tracks) byId[t.Id] = t;

        var albums = BuildAlbums(tracks, out var albumsByKey);
        var artists = BuildArtists(albums, tracks, out var artistsByKey);
        var folders = BuildFolders(tracks);
        var genres = BuildGenres(tracks);

        lock (_gate)
        {
            _tracks = tracks;
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
        var albumGroups = new Dictionary<string, List<Album>>();
        foreach (var a in albums)
        {
            var key = Identifiers.ArtistKey(a.AlbumArtist);
            if (!albumGroups.TryGetValue(key, out var list))
                albumGroups[key] = list = new List<Album>();
            list.Add(a);
        }

        var trackGroups = new Dictionary<string, List<Track>>();
        foreach (var t in tracks)
        {
            if (!trackGroups.TryGetValue(t.ArtistKey, out var list))
                trackGroups[t.ArtistKey] = list = new List<Track>();
            list.Add(t);
        }

        var artists = new List<Artist>(albumGroups.Count);
        byKey = new Dictionary<string, Artist>(albumGroups.Count);

        foreach (var (key, albumList) in albumGroups)
        {
            albumList.Sort((a, b) => b.Year.CompareTo(a.Year));
            string? artwork = null;
            foreach (var a in albumList) { artwork ??= a.ArtworkPath; }

            var artist = new Artist
            {
                Key = key,
                Name = albumList[0].AlbumArtist,
                ArtworkPath = artwork,
                Albums = albumList,
                Tracks = trackGroups.GetValueOrDefault(key, new List<Track>())
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
