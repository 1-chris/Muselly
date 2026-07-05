using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Muselly.Core.Audio;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// Default <see cref="ILibraryService"/>. The tracks live in an <see cref="ILibraryStore"/> (SQLite on the
/// desktop/server); this service holds only the lightweight album / artist / folder summaries and serves
/// individual tracks lazily from the store, so a large library is never resident in memory. Compilation
/// merging and de-duplication are baked into the stored data at scan time, so the store is the single source
/// of truth and every lazy query against it is correct. Remote (connected-server) tracks are kept in memory
/// per server and merged into the summaries.
/// </summary>
public sealed class LibraryService : ILibraryService
{
    private readonly ISettingsService _settings;
    private readonly IMetadataReader _metadata;
    private readonly ILibraryStore _store;
    private readonly ILogger<LibraryService> _logger;
    private readonly object _gate = new();

    // Remote (in-memory only) tracks per connected server. Local tracks are not held here — they're served
    // from the store on demand. Summaries below are rebuilt from store + remote.
    private readonly Dictionary<string, IReadOnlyList<Track>> _remoteTracks = new();
    private Dictionary<string, Track> _remoteById = new();

    private List<Album> _albums = new();
    private Dictionary<string, Album> _albumsByKey = new();
    private List<Artist> _artists = new();
    private Dictionary<string, Artist> _artistsByKey = new();
    private List<FolderNode> _folders = new();
    private List<string> _genres = new();
    private int _trackCount;

    // Bounded cache for FindTrack so resolving queue/playlist ids doesn't hit the store repeatedly. Cleared
    // whenever the library changes (scan, artwork apply, rebuild) so it never serves stale rows.
    private const int FindCacheMax = 8000;
    private readonly Dictionary<string, Track> _findCache = new();
    private readonly object _findGate = new();

    public LibraryService(ISettingsService settings, IMetadataReader metadata, ILibraryStore store,
        ILogger<LibraryService> logger)
    {
        _settings = settings;
        _metadata = metadata;
        _store = store;
        _logger = logger;
    }

    public int TrackCount => _trackCount;
    public IReadOnlyList<Album> Albums => _albums;
    public IReadOnlyList<Artist> Artists => _artists;
    public IReadOnlyList<FolderNode> Folders => _folders;
    public IReadOnlyList<string> Genres => _genres;

    public bool IsScanning { get; private set; }
    public bool IsLoading { get; private set; } = true;

    /// <summary>True when a folder-wide scan was started but never finished (the app was closed mid-scan), so
    /// startup should resume it. Backed by a marker file written/removed around the scan.</summary>
    public bool ScanIncomplete
    {
        get { try { return File.Exists(StoragePaths.LibraryScanMarkerFile()); } catch { return false; } }
    }

    private static void SetScanMarker(bool scanning)
    {
        try
        {
            var path = StoragePaths.LibraryScanMarkerFile();
            if (scanning) File.WriteAllText(path, DateTimeOffset.Now.ToString("o"));
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort: a missing/failed marker only affects auto-resume, not correctness */ }
    }

    public event EventHandler? LibraryChanged;
    public event EventHandler<ScanProgress>? ScanProgressChanged;

    public Album? FindAlbum(string key) => _albumsByKey.GetValueOrDefault(key);
    public Artist? FindArtist(string key) => _artistsByKey.GetValueOrDefault(key);

    public Track? FindTrack(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        lock (_gate)
            if (_remoteById.TryGetValue(id, out var remote)) return remote;

        lock (_findGate)
            if (_findCache.TryGetValue(id, out var cached)) return cached;

        var track = _store.FindTrack(id);
        if (track is not null)
            lock (_findGate)
            {
                if (_findCache.Count >= FindCacheMax) _findCache.Clear();
                _findCache[id] = track;
            }
        return track;
    }

    public IReadOnlyList<Track> ResolveTracks(IEnumerable<string> ids)
    {
        var list = new List<Track>();
        foreach (var id in ids)
        {
            var t = FindTrack(id);
            if (t is not null) list.Add(t);
        }
        return list;
    }

    public IReadOnlyList<Track> AllTracks() => CombineLocalAndRemote(LoadLocal());

    public IReadOnlyList<Track> SearchTracks(string? query, int offset, int limit)
    {
        bool hasRemote;
        lock (_gate) hasRemote = _remoteTracks.Count > 0;

        if (!hasRemote)
            return _store.Search(query, offset, limit);

        // With a remote connected, search across both. Local matches come paged from the store (cheap on a
        // desktop with no local library); remote is small/in-memory. Merge, order by title, then page.
        IEnumerable<Track> matches = _store.Search(query, 0, int.MaxValue);
        foreach (var t in RemoteSnapshot())
            if (Matches(t, query)) matches = matches.Append(t);

        return matches.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                      .Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)).ToList();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _store.InitializeAsync().ConfigureAwait(false);
            RebuildSummaries();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the library failed.");
        }
        finally
        {
            IsLoading = false;
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task ScanAsync(CancellationToken cancellationToken = default) =>
        ScanInternalAsync(ScanMode.Full, cancellationToken);

    public Task ScanNewAsync(CancellationToken cancellationToken = default) =>
        ScanInternalAsync(ScanMode.NewOnly, cancellationToken);

    private async Task ScanInternalAsync(ScanMode mode, CancellationToken cancellationToken)
    {
        if (IsScanning) return;
        IsScanning = true;
        SetScanMarker(true);
        try
        {
            var folders = new List<string>(_settings.Current.MusicFolders);
            var recurse = _settings.Current.ScanSubdirectories;

            // Persist each batch as it's read so a long scan (e.g. an hour over SMB) survives an early quit
            // and the next launch resumes from where it left off instead of starting over.
            Func<IReadOnlyList<Track>, Task> sink = batch => _store.AppendAsync(batch);

            List<Track> finalLocal;
            if (mode == ScanMode.NewOnly)
            {
                // Incremental/resumable: only read files we don't already have, and never drop existing tracks.
                var existing = await _store.GetAllTracksAsync().ConfigureAwait(false);

                var existingSources = new HashSet<string>(existing.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var t in existing) existingSources.Add(t.Source);

                // Stream the tree, skipping files already stored, reading/persisting the rest as we discover them.
                var scanned = await ReadStreamingAsync(folders, recurse, existingSources, sink, cancellationToken)
                    .ConfigureAwait(false);

                if (scanned.Count == 0)
                {
                    // Nothing new — just refresh the projections (cheap) without rewriting the database.
                    RebuildSummaries();
                    SetScanMarker(false);
                    Report(ScanPhase.Completed, _trackCount, _trackCount, null);
                    LibraryChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                var ids = new HashSet<string>(existing.Count);
                foreach (var t in existing) ids.Add(t.Id);
                finalLocal = new List<Track>(existing.Count + scanned.Count);
                finalLocal.AddRange(existing);
                foreach (var t in scanned)
                    if (ids.Add(t.Id)) finalLocal.Add(t);
            }
            else
            {
                // Full: re-read everything (so removed files drop out and edits are picked up). Batches are
                // appended as we go; the final ReplaceAll below makes the stored set canonical (and prunes
                // files that disappeared).
                finalLocal = await ReadStreamingAsync(folders, recurse, null, sink, cancellationToken)
                    .ConfigureAwait(false);
            }

            await PersistAndRebuildAsync(BakeOrganization(finalLocal)).ConfigureAwait(false);

            SetScanMarker(false);
            Report(ScanPhase.Completed, _trackCount, _trackCount, null);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Leave the scan marker in place so the scan resumes on the next launch.
            Report(ScanPhase.Idle, 0, 0, null);
        }
        catch (Exception ex)
        {
            // Never leave the UI stuck on "loading": log, drop back to idle and notify so the library view
            // re-evaluates (shows whatever is already stored / the empty state) instead of spinning forever.
            _logger.LogError(ex, "Scan failed.");
            Report(ScanPhase.Idle, 0, 0, null);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
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

            Func<IReadOnlyList<Track>, Task> sink = batch => _store.AppendAsync(batch);
            var scanned = await ReadStreamingAsync(new[] { full }, recurse, null, sink, cancellationToken)
                .ConfigureAwait(false);

            // Drop any existing local tracks living under this folder, then add the fresh scan.
            var existing = await _store.GetAllTracksAsync().ConfigureAwait(false);
            var merged = new List<Track>(existing.Count + scanned.Count);
            foreach (var t in existing)
                if (!IsUnderOrEqual(t.Directory, full)) merged.Add(t);
            merged.AddRange(scanned);

            await PersistAndRebuildAsync(BakeOrganization(merged)).ConfigureAwait(false);

            Report(ScanPhase.Completed, scanned.Count, scanned.Count, null);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            Report(ScanPhase.Idle, 0, 0, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Folder scan failed.");
            Report(ScanPhase.Idle, 0, 0, null);
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Bakes compilation merging and de-duplication into the track set so the persisted store is the
    /// canonical, query-ready library (no runtime projection needed).</summary>
    private List<Track> BakeOrganization(List<Track> tracks)
    {
        var merged = TrackNormalizer.MergeCompilations(tracks, _settings.Current.MergeCompilationAlbums);
        var deduped = TrackNormalizer.Deduplicate(merged, _settings.Current.DeduplicateTracks,
            TrackNormalizer.DefaultDuplicateTolerance);
        return deduped;
    }

    private async Task PersistAndRebuildAsync(IReadOnlyList<Track> tracks)
    {
        await _store.ReplaceAllAsync(tracks).ConfigureAwait(false);
        RebuildSummaries();
    }

    private const int ScanBatchSize = 200;

    /// <summary>
    /// Streams audio files out of the folder tree(s) and reads their metadata in batches, persisting each
    /// batch via <paramref name="onBatch"/> as it's read. Enumeration runs on a background producer feeding a
    /// bounded channel, so reading/saving begins within seconds of starting rather than after the whole
    /// (potentially huge, network-mounted) tree has been walked. Files whose source is in
    /// <paramref name="skipSources"/> are ignored (incremental scan). Returns every successfully-read track.
    /// </summary>
    private async Task<List<Track>> ReadStreamingAsync(IReadOnlyList<string> folders, bool recurse,
        HashSet<string>? skipSources, Func<IReadOnlyList<Track>, Task> onBatch, CancellationToken cancellationToken)
    {
        Report(ScanPhase.Discovering, 0, 0, null);

        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;
                    var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    IEnumerator<string> e;
                    try { e = Directory.EnumerateFiles(folder, "*", option).GetEnumerator(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to enumerate {Folder}", folder); continue; }

                    using (e)
                    {
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string current;
                            try { if (!e.MoveNext()) break; current = e.Current; }
                            catch (Exception ex) { _logger.LogWarning(ex, "Enumeration error under {Folder}", folder); break; }
                            if (AudioFormats.IsSupported(current))
                                await channel.Writer.WriteAsync(current, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            finally { channel.Writer.Complete(); }
        }, cancellationToken);

        // Reading tags over a network share is latency-bound, so oversubscribe the CPU count heavily — many
        // concurrent reads hide per-file SMB latency. Raise the thread-pool floor so all readers start at once
        // (blocking I/O reads otherwise ramp up only ~1 thread/sec).
        var workerCount = Math.Clamp(Environment.ProcessorCount * 4, 8, 48);
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        if (minWorker < workerCount + 4) ThreadPool.SetMinThreads(workerCount + 4, minIo);

        var all = new List<Track>();
        var allLock = new object();
        var pending = new List<Track>(ScanBatchSize);
        var pendingLock = new object();
        var processed = 0;
        var skipped = 0;

        async Task FlushAsync(bool force)
        {
            List<Track>? toPersist = null;
            lock (pendingLock)
            {
                if (pending.Count > 0 && (force || pending.Count >= ScanBatchSize))
                {
                    toPersist = pending;
                    pending = new List<Track>(ScanBatchSize);
                }
            }
            if (toPersist is null) return;

            // Re-link any cached cover art, then persist this batch so the work so far is durable.
            var linked = LinkCachedArtwork(toPersist);
            lock (allLock) all.AddRange(linked);
            await onBatch(linked).ConfigureAwait(false);
        }

        try
        {
            // A pool of workers pulls file paths as they're discovered and reads them concurrently — no waiting
            // for a whole batch to fill and no batch-wide stall on the slowest file (a slow file only ties up
            // its own worker). Results accumulate and flush to the store every ScanBatchSize tracks.
            await Parallel.ForEachAsync(
                channel.Reader.ReadAllAsync(cancellationToken),
                new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = cancellationToken },
                async (file, token) =>
                {
                    if (skipSources is not null && skipSources.Contains(file)) return;

                    var track = _metadata.Read(file);
                    var done = Interlocked.Increment(ref processed);

                    if (track is null)
                    {
                        Interlocked.Increment(ref skipped);
                    }
                    else
                    {
                        bool flushNow;
                        lock (pendingLock) { pending.Add(track); flushNow = pending.Count >= ScanBatchSize; }
                        if (flushNow) await FlushAsync(false).ConfigureAwait(false);
                    }

                    if (done % 50 == 0) Report(ScanPhase.Reading, done, 0, Path.GetFileName(file));
                }).ConfigureAwait(false);

            await FlushAsync(true).ConfigureAwait(false);
        }
        finally
        {
            await producer.ConfigureAwait(false); // observe producer completion / surface cancellation
        }

        Report(ScanPhase.Organizing, processed, processed, null);

        if (skipped > 0)
            _logger.LogWarning(
                "Scan: {Skipped} files could not be read and were skipped. On a macOS SMB share this is usually " +
                "filenames in NFD (decomposed Unicode) form, which macOS cannot open over SMB.", skipped);

        return all;
    }

    private static readonly string[] ArtworkExtensions = { "jpg", "jpeg", "png", "webp", "gif", "bmp" };

    /// <summary>
    /// Fills in artwork for freshly-scanned tracks that have none, by re-linking a cover already sitting in
    /// the artwork cache for that album key (covers are cached as <c>&lt;albumKey&gt;.&lt;ext&gt;</c>). This
    /// re-attaches art fetched from the internet that a full rescan would otherwise lose, without overwriting
    /// art the scan just extracted from the file itself.
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

    public async Task ApplyAlbumArtworkAsync(string albumKey, string artworkPath)
    {
        if (string.IsNullOrEmpty(albumKey) || string.IsNullOrEmpty(artworkPath)) return;

        await _store.SetAlbumArtworkAsync(albumKey, artworkPath).ConfigureAwait(false);

        // Patch the in-memory summaries in place so the change shows immediately without a full rebuild (the
        // album's tracks pick up the new path from the store on their next lazy load).
        if (_albumsByKey.TryGetValue(albumKey, out var album))
        {
            album.ArtworkPath = artworkPath;
            if (_artistsByKey.TryGetValue(Identifiers.ArtistKey(album.AlbumArtist), out var artist) &&
                string.IsNullOrEmpty(artist.ArtworkPath))
                artist.ArtworkPath = artworkPath;
        }

        lock (_findGate) _findCache.Clear();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRemoteTracks(string serverId, IReadOnlyList<Track> tracks)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        lock (_gate)
        {
            _remoteTracks[serverId] = tracks ?? Array.Empty<Track>();
            RebuildRemoteIndex();
        }
        RebuildSummaries();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRemoteSource(string serverId)
    {
        if (string.IsNullOrEmpty(serverId)) return;
        bool removed;
        lock (_gate)
        {
            removed = _remoteTracks.Remove(serverId);
            if (removed) RebuildRemoteIndex();
        }
        if (!removed) return;
        RebuildSummaries();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshOrganization()
    {
        RebuildSummaries();
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

    // --- Lazy track access -------------------------------------------------------------------------------

    private List<Track> LoadLocal() => _store.GetAllTracksAsync().GetAwaiter().GetResult();

    private List<Track> RemoteSnapshot()
    {
        lock (_gate)
        {
            var list = new List<Track>(_remoteTracks.Values.Sum(v => v.Count));
            foreach (var set in _remoteTracks.Values) list.AddRange(set);
            return list;
        }
    }

    private List<Track> CombineLocalAndRemote(List<Track> local)
    {
        var remote = RemoteSnapshot();
        if (remote.Count == 0) return local;
        var combined = new List<Track>(local.Count + remote.Count);
        combined.AddRange(local);
        combined.AddRange(remote);
        return combined;
    }

    private void RebuildRemoteIndex()
    {
        var index = new Dictionary<string, Track>();
        foreach (var set in _remoteTracks.Values)
            foreach (var t in set)
                index[t.Id] = t;
        _remoteById = index;
    }

    private IReadOnlyList<Track> AlbumTracks(Album album)
    {
        var tracks = new List<Track>(_store.GetAlbumTracks(album.Key));
        foreach (var t in RemoteSnapshot())
            if (t.AlbumKey == album.Key) tracks.Add(t);
        tracks.Sort(CompareForAlbum);
        return tracks;
    }

    private IReadOnlyList<Track> ArtistTracks(Artist artist)
    {
        var tracks = new List<Track>(_store.GetArtistTracks(artist.Key));
        foreach (var t in RemoteSnapshot())
            if (t.ArtistKey == artist.Key) tracks.Add(t);
        tracks.Sort(CompareForAlbum);
        return tracks;
    }

    private IReadOnlyList<Track> FolderTracks(FolderNode node) => _store.GetFolderTracks(node.Path);

    // --- Summary building --------------------------------------------------------------------------------

    /// <summary>Streams every track (local from the store + remote in-memory) once to build the album /
    /// artist / folder summaries and counts, then discards the track list so nothing stays resident.</summary>
    private void RebuildSummaries()
    {
        var combined = CombineLocalAndRemote(LoadLocal());

        var albums = BuildAlbums(combined, out var albumsByKey);
        var artists = BuildArtists(albums, combined, out var artistsByKey);
        var folders = BuildFolders(combined);
        var genres = BuildGenres(combined);

        lock (_gate)
        {
            _albums = albums;
            _albumsByKey = albumsByKey;
            _artists = artists;
            _artistsByKey = artistsByKey;
            _folders = folders;
            _genres = genres;
            _trackCount = combined.Count;
        }
        lock (_findGate) _findCache.Clear();
    }

    private List<Album> BuildAlbums(List<Track> tracks, out Dictionary<string, Album> byKey)
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
            uint maxDisc = 0;
            var duration = TimeSpan.Zero;
            foreach (var t in list)
            {
                if (year == 0 && t.Year > 0) year = t.Year;
                label ??= t.Label;
                artwork ??= t.ArtworkPath;
                if (t.DiscNumber > maxDisc) maxDisc = t.DiscNumber;
                duration += t.Duration;
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
                TrackCount = list.Count,
                DiscCount = (int)Math.Max(1, maxDisc),
                Duration = duration,
                TracksProvider = AlbumTracks
            };
            albums.Add(album);
            byKey[key] = album;
        }

        albums.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        return albums;
    }

    private List<Artist> BuildArtists(List<Album> albums, List<Track> tracks, out Dictionary<string, Artist> byKey)
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

        // Tracks group under their own performer (Track.ArtistKey) for counts/genres and the per-artist song
        // list. For a merged compilation this is the real per-track artist, so each performer still gets an
        // entry even though the album lives under Various Artists.
        var counts = new Dictionary<string, int>();
        var genresByKey = new Dictionary<string, List<string>>();
        var nameByKey = new Dictionary<string, string>();
        var artByKey = new Dictionary<string, string?>();
        foreach (var t in tracks)
        {
            var key = t.ArtistKey;
            if (string.IsNullOrEmpty(key)) continue;
            counts[key] = counts.GetValueOrDefault(key) + 1;
            if (!nameByKey.ContainsKey(key)) nameByKey[key] = t.DisplayArtist;
            if (!artByKey.TryGetValue(key, out var existingArt) || existingArt is null) artByKey[key] = t.ArtworkPath;
            if (!genresByKey.TryGetValue(key, out var gl)) genresByKey[key] = gl = new List<string>();
            foreach (var g in t.Genres)
                if (!gl.Contains(g)) gl.Add(g);
        }

        var keys = new HashSet<string>(albumGroups.Keys);
        keys.UnionWith(counts.Keys);

        var artists = new List<Artist>(keys.Count);
        byKey = new Dictionary<string, Artist>(keys.Count);

        foreach (var key in keys)
        {
            var albumList = albumGroups.GetValueOrDefault(key) ?? new List<Album>();
            albumList.Sort((a, b) => b.Year.CompareTo(a.Year));

            string? artwork = null;
            foreach (var a in albumList) { if (a.ArtworkPath is not null) { artwork = a.ArtworkPath; break; } }
            artwork ??= artByKey.GetValueOrDefault(key);

            var artist = new Artist
            {
                Key = key,
                Name = albumList.Count > 0 ? albumList[0].AlbumArtist : nameByKey.GetValueOrDefault(key, "Unknown Artist"),
                ArtworkPath = artwork,
                Albums = albumList,
                TrackCount = counts.GetValueOrDefault(key),
                Genres = genresByKey.GetValueOrDefault(key) ?? new List<string>(),
                TracksProvider = ArtistTracks
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
            var node = new FolderNode { Name = NiceName(full), Path = full, IsRoot = true, TracksProvider = FolderTracks };
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
            node.DirectTrackCount++;
        }

        roots.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return roots;
    }

    private FolderNode EnsureFolderChain(FolderNode root, string targetDir, Dictionary<string, FolderNode> nodesByPath)
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
                child = new FolderNode { Name = part, Path = currentPath, TracksProvider = FolderTracks };
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

    private static bool Matches(Track t, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return Contains(t.Title, q) || Contains(t.Artist, q) || Contains(t.Album, q);
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

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
}
