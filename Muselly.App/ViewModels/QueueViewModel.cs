using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// Backs the queue drawer: a live mirror of the play queue with the currently playing item highlighted.
/// Supports jumping to a track, removing items and clearing the queue.
/// </summary>
public sealed partial class QueueViewModel : ViewModelBase
{
    private readonly IQueueService _queue;
    private readonly PlaybackCoordinator _coordinator;

    public QueueViewModel(IQueueService queue, PlaybackCoordinator coordinator)
    {
        _queue = queue;
        _coordinator = coordinator;
        _queue.QueueChanged += (_, _) => OnUi(Rebuild);
        _queue.CurrentChanged += (_, _) => OnUi(RefreshCurrent);
        Rebuild();
    }

    public ObservableCollection<Track> Items { get; } = new();

    [ObservableProperty] private Track? _currentTrack;
    [ObservableProperty] private int _count;

    [RelayCommand]
    private void Play(Track? track)
    {
        if (track is null) return;
        var index = Items.IndexOf(track);
        if (index >= 0) _coordinator.PlayQueueIndex(index);
    }

    [RelayCommand]
    private void Remove(Track? track)
    {
        if (track is null) return;
        var index = Items.IndexOf(track);
        if (index >= 0) _queue.RemoveAt(index);
    }

    [RelayCommand]
    private void Clear() => _queue.Clear();

    /// <summary>Inserts dropped tracks (from the library) at the given position in the queue.</summary>
    public void InsertTracks(int insertIndex, System.Collections.Generic.IReadOnlyList<Track> tracks)
    {
        if (tracks.Count == 0) return;
        _queue.InsertAt(insertIndex, tracks);
    }

    /// <summary>Reorders an existing queue item to a new insertion position (drag-to-reorder).</summary>
    public void MoveTrack(int fromIndex, int insertIndex)
    {
        if (fromIndex < 0) return;
        var target = insertIndex;
        // The insertion index is computed against the pre-removal list; account for the gap left behind.
        if (fromIndex < target) target--;
        target = System.Math.Clamp(target, 0, Items.Count - 1);
        if (target == fromIndex) return;
        _queue.Move(fromIndex, target);
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var t in _queue.Items) Items.Add(t);
        Count = Items.Count;
        RefreshCurrent();
    }

    private void RefreshCurrent() => CurrentTrack = _queue.Current;

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
