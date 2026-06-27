using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.App.Services;

/// <summary>
/// Persists the play session — the queue, the current track and its position — so closing and reopening the
/// app resumes exactly where the user left off. Saves are debounced and triggered on queue changes, track
/// changes and transport (play/pause/stop) changes, plus periodically while playing and once on shutdown.
/// The restored track is loaded paused, so nothing blasts out audio on launch.
/// </summary>
public interface ISessionStateService
{
    /// <summary>Reads the saved session and restores the queue + paused current track. Call after the library
    /// has loaded (so track ids resolve). Enables subsequent saving.</summary>
    Task RestoreAsync();

    /// <summary>Writes the current session immediately (synchronously) — used on shutdown.</summary>
    void SaveNow();
}

public sealed class SessionStateService : ISessionStateService
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILibraryService _library;
    private readonly System.Timers.Timer _debounce;
    private readonly System.Timers.Timer _periodic;
    private volatile bool _enabled;

    public SessionStateService(PlaybackCoordinator coordinator, ILibraryService library)
    {
        _coordinator = coordinator;
        _library = library;

        _debounce = new System.Timers.Timer(600) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Dispatcher.UIThread.Post(WriteSnapshotAsync);

        // Position drifts continuously while playing without raising discrete events, so persist it on a slow
        // cadence too (cheap; the file is a small id list).
        _periodic = new System.Timers.Timer(5000) { AutoReset = true };
        _periodic.Elapsed += (_, _) => ScheduleSave();
        _periodic.Start();

        _coordinator.Queue.QueueChanged += (_, _) => ScheduleSave();
        _coordinator.Queue.CurrentChanged += (_, _) => ScheduleSave();
        _coordinator.Playback.StateChanged += (_, _) => ScheduleSave();
    }

    public Task RestoreAsync()
    {
        var state = JsonStore.Load(StoragePaths.SessionStateFile(), () => new SessionState());

        if (state.TrackIds.Count > 0)
        {
            var resolved = new List<Track>(state.TrackIds.Count);
            var currentIndex = -1;
            for (var i = 0; i < state.TrackIds.Count; i++)
            {
                var track = _library.FindTrack(state.TrackIds[i]);
                if (track is null) continue;            // dropped from the library (or a not-yet-connected remote)
                if (i == state.CurrentIndex) currentIndex = resolved.Count;
                resolved.Add(track);
            }

            if (resolved.Count > 0)
            {
                // If the saved current track is gone, start at the top with no offset.
                var position = currentIndex >= 0 ? TimeSpan.FromSeconds(Math.Max(0, state.PositionSeconds)) : TimeSpan.Zero;
                if (currentIndex < 0) currentIndex = 0;
                _coordinator.RestoreSession(resolved, currentIndex, position);
            }
        }

        _enabled = true; // only now allow saves, so we never clobber the file before restoring it
        return Task.CompletedTask;
    }

    public void SaveNow()
    {
        if (!_enabled) return;
        var state = Dispatcher.UIThread.CheckAccess() ? Gather() : Dispatcher.UIThread.Invoke(Gather);
        Write(state);
    }

    private void ScheduleSave()
    {
        if (!_enabled) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void WriteSnapshotAsync()
    {
        if (!_enabled) return;
        var state = Gather();
        _ = Task.Run(() => Write(state));
    }

    /// <summary>Snapshots the live queue/transport. Must run on the UI thread (where they mutate).</summary>
    private SessionState Gather()
    {
        var queue = _coordinator.Queue;
        var items = queue.Items;
        var ids = new List<string>(items.Count);
        foreach (var t in items) ids.Add(t.Id);
        return new SessionState
        {
            TrackIds = ids,
            CurrentIndex = queue.CurrentIndex,
            PositionSeconds = _coordinator.Playback.Position.TotalSeconds
        };
    }

    private static void Write(SessionState state)
    {
        try { JsonStore.Save(StoragePaths.SessionStateFile(), state); }
        catch { /* best-effort; a failed session save must never disrupt playback */ }
    }

    private sealed class SessionState
    {
        public int Version { get; set; } = 1;
        public List<string> TrackIds { get; set; } = new();
        public int CurrentIndex { get; set; } = -1;
        public double PositionSeconds { get; set; }
    }
}
