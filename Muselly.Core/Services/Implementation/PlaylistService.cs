using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// File-backed <see cref="IPlaylistService"/>. Persists ordered track-id lists and resolves them against
/// the live library. Sorting reorders the stored ids (so the order survives restart) while remembering the
/// chosen field for the UI.
/// </summary>
public sealed class PlaylistService : IPlaylistService
{
    private readonly ILibraryService _library;
    private readonly object _gate = new();
    private List<Playlist> _playlists = new();

    public PlaylistService(ILibraryService library) => _library = library;

    public IReadOnlyList<Playlist> Playlists => _playlists;

    public event EventHandler? Changed;

    public Task LoadAsync() => Task.Run(() =>
    {
        var loaded = JsonStore.Load(StoragePaths.PlaylistsFile(), () => new PlaylistFile());
        lock (_gate) _playlists = loaded.Playlists ?? new List<Playlist>();
        Changed?.Invoke(this, EventArgs.Empty);
    });

    public Playlist Create(string name)
    {
        var playlist = new Playlist
        {
            Id = "pl_" + Identifiers.Hash(Guid.NewGuid().ToString()),
            Name = string.IsNullOrWhiteSpace(name) ? "New Playlist" : name.Trim()
        };
        lock (_gate) _playlists.Add(playlist);
        Persist();
        return playlist;
    }

    public void Rename(string id, string name)
    {
        var playlist = Find(id);
        if (playlist is null || string.IsNullOrWhiteSpace(name)) return;
        playlist.Name = name.Trim();
        Touch(playlist);
        Persist();
    }

    public void Delete(string id)
    {
        lock (_gate) _playlists.RemoveAll(p => p.Id == id);
        Persist();
    }

    public void AddTracks(string id, IReadOnlyList<string> trackIds)
    {
        var playlist = Find(id);
        if (playlist is null || trackIds.Count == 0) return;
        playlist.TrackIds.AddRange(trackIds);
        Touch(playlist);
        Persist();
    }

    public void RemoveTrackAt(string id, int index)
    {
        var playlist = Find(id);
        if (playlist is null || index < 0 || index >= playlist.TrackIds.Count) return;
        playlist.TrackIds.RemoveAt(index);
        Touch(playlist);
        Persist();
    }

    public void Move(string id, int fromIndex, int toIndex)
    {
        var playlist = Find(id);
        if (playlist is null) return;
        var ids = playlist.TrackIds;
        if (fromIndex < 0 || fromIndex >= ids.Count || toIndex < 0 || toIndex >= ids.Count || fromIndex == toIndex)
            return;
        var item = ids[fromIndex];
        ids.RemoveAt(fromIndex);
        ids.Insert(toIndex, item);
        playlist.SortField = TrackSortField.Custom;
        Touch(playlist);
        Persist();
    }

    public void Randomize(string id)
    {
        var playlist = Find(id);
        if (playlist is null) return;
        var ids = playlist.TrackIds;
        var rng = new Random();
        for (var i = ids.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        playlist.SortField = TrackSortField.Custom;
        Touch(playlist);
        Persist();
    }

    public void SetSort(string id, TrackSortField field, SortDirection direction)
    {
        var playlist = Find(id);
        if (playlist is null) return;

        playlist.SortField = field;
        playlist.SortDirection = direction;

        if (field != TrackSortField.Custom)
        {
            var tracks = _library.ResolveTracks(playlist.TrackIds);
            var sorted = TrackSorter.Sort(tracks, field, direction);
            playlist.TrackIds.Clear();
            foreach (var t in sorted) playlist.TrackIds.Add(t.Id);
        }

        Touch(playlist);
        Persist();
    }

    public IReadOnlyList<Track> ResolveTracks(string id)
    {
        var playlist = Find(id);
        return playlist is null ? Array.Empty<Track>() : _library.ResolveTracks(playlist.TrackIds);
    }

    public void Save() => Persist();

    private Playlist? Find(string id)
    {
        lock (_gate) return _playlists.Find(p => p.Id == id);
    }

    private static void Touch(Playlist playlist) => playlist.Modified = DateTimeOffset.Now;

    private void Persist()
    {
        List<Playlist> snapshot;
        lock (_gate) snapshot = new List<Playlist>(_playlists);
        JsonStore.Save(StoragePaths.PlaylistsFile(), new PlaylistFile { Playlists = snapshot });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class PlaylistFile
    {
        public int Version { get; set; } = 1;
        public List<Playlist> Playlists { get; set; } = new();
    }
}
