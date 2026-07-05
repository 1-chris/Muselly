using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// Default <see cref="ILibraryStore"/>: the library in a single JSON file (<c>library.json</c>). Used where a
/// native SQLite library isn't available — notably the browser head, which has no real filesystem and gets
/// its library from a server (those tracks live in the library service, so this store is effectively empty
/// there). The per-key queries therefore operate on a small/empty in-memory list.
/// </summary>
public sealed class JsonLibraryStore : ILibraryStore
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<List<Track>> GetAllTracksAsync() => Task.Run(LoadAll);

    public Task ReplaceAllAsync(IReadOnlyList<Track> tracks) => Task.Run(() =>
        JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot
        {
            Tracks = tracks as List<Track> ?? new List<Track>(tracks),
            ScannedAt = DateTimeOffset.Now
        }));

    public Task AppendAsync(IReadOnlyList<Track> tracks) => Task.Run(() =>
    {
        if (tracks.Count == 0) return;
        var all = LoadAll();
        var byId = new Dictionary<string, int>(all.Count);
        for (var i = 0; i < all.Count; i++) byId[all[i].Id] = i;
        foreach (var t in tracks)
        {
            if (byId.TryGetValue(t.Id, out var idx)) all[idx] = t;
            else { byId[t.Id] = all.Count; all.Add(t); }
        }
        JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot { Tracks = all, ScannedAt = DateTimeOffset.Now });
    });

    public Task SetAlbumArtworkAsync(string albumKey, string artworkPath) => Task.Run(() =>
    {
        var all = LoadAll();
        var changed = false;
        for (var i = 0; i < all.Count; i++)
            if (all[i].AlbumKey == albumKey && all[i].ArtworkPath != artworkPath)
            {
                all[i] = all[i].WithArtwork(artworkPath);
                changed = true;
            }
        if (changed)
            JsonStore.Save(StoragePaths.LibraryCacheFile(), new LibrarySnapshot { Tracks = all, ScannedAt = DateTimeOffset.Now });
    });

    public int Count() => LoadAll().Count;

    public Track? FindTrack(string id) => LoadAll().FirstOrDefault(t => t.Id == id);

    public IReadOnlyList<Track> GetAlbumTracks(string albumKey) =>
        LoadAll().Where(t => t.AlbumKey == albumKey)
                 .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ToList();

    public IReadOnlyList<Track> GetArtistTracks(string artistKey) =>
        LoadAll().Where(t => t.ArtistKey == artistKey)
                 .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ToList();

    public IReadOnlyList<Track> GetFolderTracks(string directory) =>
        LoadAll().Where(t => string.Equals(t.Directory, directory, StringComparison.OrdinalIgnoreCase))
                 .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ToList();

    public IReadOnlyList<Track> Search(string? query, int offset, int limit)
    {
        IEnumerable<Track> q = LoadAll();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var s = query.Trim();
            q = q.Where(t => Contains(t.Title, s) || Contains(t.Artist, s) || Contains(t.Album, s));
        }
        return q.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)).ToList();
    }

    private static List<Track> LoadAll() =>
        JsonStore.LoadStreaming(StoragePaths.LibraryCacheFile(), () => new LibrarySnapshot(), internStrings: true).Tracks
        ?? new List<Track>();

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
