using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// Drives the always-visible bottom player bar: current track + art, transport (prev/play/next), shuffle
/// and repeat toggles, the custom scrobbler (position/duration + seek), volume and the queue toggle. It
/// mirrors the audio backend and queue state, marshalling their (possibly off-thread) events onto the UI
/// thread before raising change notifications.
/// </summary>
public sealed partial class PlayerBarViewModel : ViewModelBase
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private readonly INavigationService _nav;

    /// <summary>Set by the shell so the queue button can open/close the queue drawer.</summary>
    public Action? QueueToggleRequested { get; set; }

    /// <summary>Set by the shell so the lyrics button can open/close the lyrics panel.</summary>
    public Action? LyricsToggleRequested { get; set; }

    public PlayerBarViewModel(PlaybackCoordinator coordinator, IPlaybackService playback, IQueueService queue,
        INavigationService nav)
    {
        _coordinator = coordinator;
        _playback = playback;
        _queue = queue;
        _nav = nav;

        _playback.StateChanged += (_, _) => OnUi(RefreshState);
        _playback.PositionChanged += (_, _) => OnUi(RefreshPosition);
        _queue.CurrentChanged += (_, _) => OnUi(RefreshTrack);
        _queue.QueueChanged += (_, _) => OnUi(RefreshTransport);

        RefreshTrack();
        RefreshState();
    }

    [ObservableProperty] private Track? _currentTrack;
    [ObservableProperty] private bool _hasTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private TimeSpan _position;
    [ObservableProperty] private TimeSpan _duration;
    [ObservableProperty] private bool _shuffleActive;
    [ObservableProperty] private bool _repeatActive;
    [ObservableProperty] private bool _repeatOne;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _canGoNext;
    [ObservableProperty] private bool _canGoPrevious;

    public string TrackTitle => CurrentTrack?.Title ?? "Nothing playing";
    public string TrackArtist => CurrentTrack?.DisplayArtist ?? "—";
    public string? ArtworkPath => CurrentTrack?.ArtworkPath;

    partial void OnVolumeChanged(double value) => _coordinator.SetVolume(value);

    [RelayCommand] private void PlayPause() => _coordinator.TogglePlayPause();
    [RelayCommand] private void Next() => _coordinator.Next();
    [RelayCommand] private void Previous() => _coordinator.Previous();
    // Repeat mode isn't an observable event on the queue, and shuffle's change fires QueueChanged (which only
    // refreshes transport), so refresh the toggle indicators here so the icons update on click immediately.
    [RelayCommand] private void ToggleShuffle() { _coordinator.ToggleShuffle(); RefreshState(); }
    [RelayCommand] private void CycleRepeat() { _coordinator.CycleRepeat(); RefreshState(); }
    [RelayCommand] private void ToggleMute() => _coordinator.ToggleMute();
    [RelayCommand] private void ToggleQueue() => QueueToggleRequested?.Invoke();
    [RelayCommand] private void ToggleLyrics() => LyricsToggleRequested?.Invoke();
    [RelayCommand] private void Seek(TimeSpan position) => _coordinator.Seek(position);

    [RelayCommand]
    private void OpenAlbum()
    {
        if (CurrentTrack is { } t && !string.IsNullOrEmpty(t.AlbumKey)) _nav.OpenAlbum(t.AlbumKey);
    }

    [RelayCommand]
    private void OpenArtist()
    {
        if (CurrentTrack is { } t && !string.IsNullOrEmpty(t.ArtistKey)) _nav.OpenArtist(t.ArtistKey);
    }

    [RelayCommand]
    private void ShowNowPlaying()
    {
        if (CurrentTrack is not null) _nav.ShowNowPlaying();
    }

    private void RefreshTrack()
    {
        CurrentTrack = _queue.Current ?? _playback.Current;
        HasTrack = CurrentTrack is not null;
        OnPropertyChanged(nameof(TrackTitle));
        OnPropertyChanged(nameof(TrackArtist));
        OnPropertyChanged(nameof(ArtworkPath));
        RefreshState();
        RefreshTransport();
    }

    private void RefreshState()
    {
        IsPlaying = _playback.State == PlaybackState.Playing;
        Duration = _playback.Duration;
        Volume = _playback.Volume;
        IsMuted = _playback.Muted;
        ShuffleActive = _queue.Shuffle;
        RepeatActive = _queue.RepeatMode != RepeatMode.Off;
        RepeatOne = _queue.RepeatMode == RepeatMode.One;
    }

    private void RefreshPosition()
    {
        Position = _playback.Position;
        Duration = _playback.Duration;
    }

    private void RefreshTransport()
    {
        CanGoNext = _queue.HasNext;
        CanGoPrevious = _queue.HasPrevious;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
