using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Implementation;

/// <summary>Default <see cref="IListeningHistoryService"/>: an in-memory ring of recent plays, persisted.</summary>
public sealed class ListeningHistoryService : IListeningHistoryService
{
    private const int MaxEntries = 500;

    private readonly object _gate = new();
    private readonly List<ListeningHistoryEntry> _entries = new(); // newest first

    public IReadOnlyList<ListeningHistoryEntry> Recent
    {
        get { lock (_gate) return _entries.ToArray(); }
    }

    public event EventHandler? Changed;

    public void Record(Track track)
    {
        if (track is null || string.IsNullOrEmpty(track.Id)) return;
        lock (_gate)
        {
            // Collapse immediate repeats (e.g. a restart) so the history isn't spammed.
            if (_entries.Count > 0 && _entries[0].TrackId == track.Id &&
                DateTimeOffset.Now - _entries[0].PlayedAt < TimeSpan.FromSeconds(20))
                return;

            _entries.Insert(0, new ListeningHistoryEntry { TrackId = track.Id, PlayedAt = DateTimeOffset.Now });
            if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        Save();
    }

    public Task LoadAsync() => Task.Run(() =>
    {
        var snapshot = JsonStore.Load(StoragePaths.ListeningHistoryFile(), () => new HistorySnapshot());
        lock (_gate)
        {
            _entries.Clear();
            _entries.AddRange(snapshot.Entries);
            _entries.Sort((a, b) => b.PlayedAt.CompareTo(a.PlayedAt));
            if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    });

    private void Save()
    {
        HistorySnapshot snapshot;
        lock (_gate) snapshot = new HistorySnapshot { Entries = new List<ListeningHistoryEntry>(_entries) };
        _ = Task.Run(() =>
        {
            try { JsonStore.Save(StoragePaths.ListeningHistoryFile(), snapshot); }
            catch { /* best-effort */ }
        });
    }

    private sealed class HistorySnapshot
    {
        public int Version { get; set; } = 1;
        public List<ListeningHistoryEntry> Entries { get; set; } = new();
    }
}
