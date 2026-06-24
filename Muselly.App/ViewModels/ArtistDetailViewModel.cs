using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;

namespace Muselly.App.ViewModels;

/// <summary>
/// The artist detail page: header (image + biography) plus the artist's albums and the full action set
/// across all of the artist's tracks (play, shuffle, queue, queue shuffled). The biography and image are
/// fetched from the web (Wikipedia) — automatically when enabled in settings, or on demand otherwise — and
/// can be expanded/collapsed in the UI.
/// </summary>
public sealed partial class ArtistDetailViewModel : ViewModelBase
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly IQueueService _queue;
    private readonly IArtistInfoService _artistInfo;
    private readonly bool _autoFetch;
    private readonly Action<Album> _openAlbum;
    private readonly Action _back;

    public ArtistDetailViewModel(Artist artist, PlaybackCoordinator coordinator, IQueueService queue,
        IArtistInfoService artistInfo, bool autoFetchBiography, Action<Album> openAlbum, Action back)
    {
        Artist = artist;
        _coordinator = coordinator;
        _queue = queue;
        _artistInfo = artistInfo;
        _autoFetch = autoFetchBiography;
        _openAlbum = openAlbum;
        _back = back;

        _imagePath = artist.ArtworkPath;
        _ = LoadBiographyAsync();
    }

    public Artist Artist { get; }

    public IReadOnlyList<Album> Albums => Artist.Albums;

    public string Subtitle => $"{Artist.AlbumCount} albums  •  {Artist.TrackCount} tracks";

    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private string? _biography;
    [ObservableProperty] private string? _biographySource;
    [ObservableProperty] private bool _hasBiography;
    [ObservableProperty] private bool _isLoadingBiography;
    [ObservableProperty] private bool _isBiographyExpanded;

    /// <summary>True when no biography is available and we aren't currently loading one.</summary>
    public bool CanFetchBiography => !HasBiography && !IsLoadingBiography;

    /// <summary>Line cap for the collapsed biography (0 = unlimited when expanded).</summary>
    public int BiographyMaxLines => IsBiographyExpanded ? 0 : 4;

    public string ExpandLabel => IsBiographyExpanded ? "Show less" : "Show more";

    partial void OnHasBiographyChanged(bool value) => OnPropertyChanged(nameof(CanFetchBiography));
    partial void OnIsLoadingBiographyChanged(bool value) => OnPropertyChanged(nameof(CanFetchBiography));

    partial void OnIsBiographyExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(BiographyMaxLines));
        OnPropertyChanged(nameof(ExpandLabel));
    }

    [RelayCommand] private void Back() => _back();
    [RelayCommand] private void Play() => _coordinator.Play(Artist.Tracks);
    [RelayCommand] private void PlayShuffled() => _coordinator.PlayShuffled(Artist.Tracks);
    [RelayCommand] private void PlayNext() => _queue.EnqueueNext(Artist.Tracks);
    [RelayCommand] private void AddToQueue() => _queue.Enqueue(Artist.Tracks);
    [RelayCommand] private void AddToQueueShuffled() => _queue.EnqueueShuffled(Artist.Tracks);

    [RelayCommand] private void ToggleBiography() => IsBiographyExpanded = !IsBiographyExpanded;

    [RelayCommand]
    private async Task FetchBiography()
    {
        IsLoadingBiography = true;
        var info = await _artistInfo.RefreshAsync(Artist.Key, Artist.Name).ConfigureAwait(false);
        OnUi(() =>
        {
            IsLoadingBiography = false;
            if (info is not null) Apply(info);
        });
    }

    [RelayCommand]
    private void OpenAlbum(Album? album)
    {
        if (album is not null) _openAlbum(album);
    }

    [RelayCommand]
    private void PlayAlbum(Album? album)
    {
        if (album is not null) _coordinator.Play(album.Tracks);
    }

    private async Task LoadBiographyAsync()
    {
        // Show anything already in memory instantly.
        var cached = _artistInfo.Get(Artist.Key);
        if (cached is not null) Apply(cached);

        if (!_autoFetch)
        {
            // Even with auto-fetch off, surface a previously saved biography once the cache has loaded.
            if (cached is null)
            {
                var saved = await _artistInfo.GetWhenLoadedAsync(Artist.Key).ConfigureAwait(false);
                if (saved is not null) OnUi(() => Apply(saved));
            }
            return;
        }

        IsLoadingBiography = true;
        var info = await _artistInfo.EnsureAsync(Artist.Key, Artist.Name).ConfigureAwait(false);
        OnUi(() =>
        {
            IsLoadingBiography = false;
            if (info is not null) Apply(info);
        });
    }

    private void Apply(ArtistInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.Biography))
        {
            Biography = info.Biography;
            BiographySource = info.BiographySource;
            HasBiography = true;
        }
        if (!string.IsNullOrEmpty(info.ImagePath))
            ImagePath = info.ImagePath;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
