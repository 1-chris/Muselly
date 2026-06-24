using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// The full-screen Now Playing page: large cover art (opens the album), the track title and a clickable
/// artist (opens the artist), and the shared transport via <see cref="Player"/> (the same scrobbler/buttons
/// as the bottom bar).
/// </summary>
public sealed partial class NowPlayingViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly IQueueService _queue;
    private readonly IPlaybackService _playback;

    public NowPlayingViewModel(PlayerBarViewModel player, INavigationService nav, IQueueService queue,
        IPlaybackService playback)
    {
        Player = player;
        _nav = nav;
        _queue = queue;
        _playback = playback;

        _queue.CurrentChanged += (_, _) => OnUi(Refresh);
        _playback.StateChanged += (_, _) => OnUi(Refresh);
        Refresh();
    }

    /// <summary>Shared transport view model (scrobbler, play/pause, next/prev, shuffle, repeat, volume).</summary>
    public PlayerBarViewModel Player { get; }

    [ObservableProperty] private Track? _currentTrack;

    public string Title => CurrentTrack?.Title ?? "Nothing playing";
    public string Artist => CurrentTrack?.DisplayArtist ?? "—";
    public string AlbumTitle => CurrentTrack?.DisplayAlbum ?? string.Empty;
    public string? ArtworkPath => CurrentTrack?.ArtworkPath;

    [RelayCommand] private void Back() => _nav.GoBack();

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

    private void Refresh()
    {
        CurrentTrack = _queue.Current ?? _playback.Current;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(AlbumTitle));
        OnPropertyChanged(nameof(ArtworkPath));
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
