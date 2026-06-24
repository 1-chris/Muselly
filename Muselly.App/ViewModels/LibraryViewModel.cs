using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Controls;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// The library browser: four organised views of the scanned music — Albums (grid), Artists (grid), Songs
/// (list) and Folders (directory tree) — with live search. Opening an album/artist navigates to a dedicated
/// page via the navigation service (so back/forward works). It exposes the queue actions used throughout.
/// </summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IQueueService _queue;
    private readonly IPlaylistService _playlists;
    private readonly INavigationService _nav;

    public LibraryViewModel(ILibraryService library, PlaybackCoordinator coordinator, IQueueService queue,
        IPlaylistService playlists, INavigationService nav)
    {
        _library = library;
        _coordinator = coordinator;
        _queue = queue;
        _playlists = playlists;
        _nav = nav;
        _library.LibraryChanged += (_, _) => OnUi(Reload);
        Reload();
    }

    public RangeObservableCollection<Album> Albums { get; } = new();
    public RangeObservableCollection<Artist> Artists { get; } = new();
    public RangeObservableCollection<Track> Songs { get; } = new();
    public RangeObservableCollection<FolderNode> FolderRoots { get; } = new();
    public RangeObservableCollection<Track> FolderTracks { get; } = new();

    private CancellationTokenSource? _filterCts;

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private FolderNode? _selectedFolder;
    [ObservableProperty] private bool _isEmpty;

    public bool IsAlbumsTab => SelectedTabIndex == 0;
    public bool IsArtistsTab => SelectedTabIndex == 1;
    public bool IsSongsTab => SelectedTabIndex == 2;
    public bool IsFoldersTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAlbumsTab));
        OnPropertyChanged(nameof(IsArtistsTab));
        OnPropertyChanged(nameof(IsSongsTab));
        OnPropertyChanged(nameof(IsFoldersTab));
    }

    partial void OnSearchTextChanged(string value) => ScheduleFilter(180);

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        FolderTracks.Reset(value is null ? System.Array.Empty<Track>() : CollectTracks(value));
    }

    [RelayCommand]
    private void SelectTab(string? index)
    {
        // Route through navigation so each tab switch is its own back/forward history entry.
        if (int.TryParse(index, out var i)) _nav.ShowLibraryTab(i);
    }

    [RelayCommand] private void OpenAlbum(Album? album) { if (album is not null) _nav.OpenAlbum(album.Key); }
    [RelayCommand] private void OpenArtist(Artist? artist) { if (artist is not null) _nav.OpenArtist(artist.Key); }
    [RelayCommand] private void OpenTrackAlbum(Track? track) { if (track is not null) _nav.OpenAlbum(track.AlbumKey); }
    [RelayCommand] private void OpenTrackArtist(Track? track) { if (track is not null) _nav.OpenArtist(track.ArtistKey); }

    [RelayCommand] private void PlayAlbum(Album? album) { if (album is not null) _coordinator.Play(album.Tracks); }
    [RelayCommand] private void PlayAlbumShuffled(Album? album) { if (album is not null) _coordinator.PlayShuffled(album.Tracks); }
    [RelayCommand] private void PlayNextAlbum(Album? album) { if (album is not null) _queue.EnqueueNext(album.Tracks); }
    [RelayCommand] private void QueueAlbum(Album? album) { if (album is not null) _queue.Enqueue(album.Tracks); }
    [RelayCommand] private void QueueAlbumShuffled(Album? album) { if (album is not null) _queue.EnqueueShuffled(album.Tracks); }
    [RelayCommand] private void PlayArtist(Artist? artist) { if (artist is not null) _coordinator.Play(artist.Tracks); }
    [RelayCommand] private void PlayArtistShuffled(Artist? artist) { if (artist is not null) _coordinator.PlayShuffled(artist.Tracks); }
    [RelayCommand] private void PlayNextArtist(Artist? artist) { if (artist is not null) _queue.EnqueueNext(artist.Tracks); }
    [RelayCommand] private void QueueArtist(Artist? artist) { if (artist is not null) _queue.Enqueue(artist.Tracks); }
    [RelayCommand] private void QueueArtistShuffled(Artist? artist) { if (artist is not null) _queue.EnqueueShuffled(artist.Tracks); }

    [RelayCommand]
    private void PlaySong(Track? track)
    {
        if (track is null) return;
        var index = Songs.IndexOf(track);
        var list = new List<Track>(Songs);
        if (index >= 0) _coordinator.Play(list, index);
    }

    [RelayCommand] private void QueueSong(Track? track) { if (track is not null) _queue.Enqueue(new[] { track }); }
    [RelayCommand] private void QueueSongNext(Track? track) { if (track is not null) _queue.EnqueueNext(new[] { track }); }

    [RelayCommand] private void PlayAllSongs() => _coordinator.Play(new List<Track>(Songs));
    [RelayCommand] private void ShuffleAllSongs() => _coordinator.PlayShuffled(new List<Track>(Songs));

    [RelayCommand]
    private void PlayFolder(FolderNode? folder)
    {
        if (folder is null) return;
        _coordinator.Play(CollectTracks(folder));
    }

    [RelayCommand]
    private void QueueFolder(FolderNode? folder)
    {
        if (folder is null) return;
        _queue.Enqueue(CollectTracks(folder));
    }

    [RelayCommand]
    private void PlayFolderTrack(Track? track)
    {
        if (track is null) return;
        var index = FolderTracks.IndexOf(track);
        var list = new List<Track>(FolderTracks);
        if (index >= 0) _coordinator.Play(list, index);
    }

    private void Reload()
    {
        FolderRoots.Reset(new List<FolderNode>(_library.Folders));
        IsEmpty = _library.Tracks.Count == 0;
        ScheduleFilter(0); // refilter immediately against the new library contents
    }

    /// <summary>
    /// Debounced, off-UI-thread filtering. Heavy iteration runs on the thread pool and the results are
    /// applied as a single batched reset per collection, so typing in search never blocks the UI thread or
    /// triggers the allocation/notification storm that was stalling audio.
    /// </summary>
    private void ScheduleFilter(int delayMs)
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        var query = SearchText?.Trim() ?? string.Empty;
        _ = FilterAsync(query, delayMs, cts.Token);
    }

    private async Task FilterAsync(string query, int delayMs, CancellationToken ct)
    {
        try
        {
            if (delayMs > 0) await Task.Delay(delayMs, ct).ConfigureAwait(false);

            // Snapshot the (immutably swapped) source lists, then filter off the UI thread.
            var albumsSrc = _library.Albums;
            var artistsSrc = _library.Artists;
            var tracksSrc = _library.Tracks;

            var result = await Task.Run(() =>
            {
                var albums = new List<Album>();
                foreach (var a in albumsSrc)
                {
                    ct.ThrowIfCancellationRequested();
                    if (query.Length == 0 || Contains(a.Title, query) || Contains(a.AlbumArtist, query))
                        albums.Add(a);
                }

                var artists = new List<Artist>();
                foreach (var a in artistsSrc)
                    if (query.Length == 0 || Contains(a.Name, query))
                        artists.Add(a);

                var songs = new List<Track>();
                foreach (var t in tracksSrc)
                {
                    ct.ThrowIfCancellationRequested();
                    if (query.Length == 0 || Contains(t.Title, query) || Contains(t.DisplayArtist, query) || Contains(t.DisplayAlbum, query))
                        songs.Add(t);
                }

                return (albums, artists, songs);
            }, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested) return;

            OnUi(() =>
            {
                if (ct.IsCancellationRequested) return;
                Albums.Reset(result.albums);
                Artists.Reset(result.artists);
                Songs.Reset(result.songs);
            });
        }
        catch (System.OperationCanceledException)
        {
            // Superseded by a newer query — ignore.
        }
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, System.StringComparison.OrdinalIgnoreCase);

    private static List<Track> CollectTracks(FolderNode node)
    {
        var list = new List<Track>();
        Walk(node, list);
        return list;
    }

    private static void Walk(FolderNode node, List<Track> into)
    {
        into.AddRange(node.Tracks);
        foreach (var child in node.Children) Walk(child, into);
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
