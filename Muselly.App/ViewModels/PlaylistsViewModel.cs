using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// Manages playlists: create / delete, browse a selected playlist's tracks, reorder by sorting on any
/// field, randomise the order, and play / queue the whole playlist or individual tracks. Persists through
/// <see cref="IPlaylistService"/>.
/// </summary>
public sealed partial class PlaylistsViewModel : ViewModelBase
{
    private readonly IPlaylistService _playlists;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IQueueService _queue;
    private readonly IShareLinkService _shareLinks;

    public PlaylistsViewModel(IPlaylistService playlists, PlaybackCoordinator coordinator, IQueueService queue,
        IShareLinkService shareLinks)
    {
        _playlists = playlists;
        _coordinator = coordinator;
        _queue = queue;
        _shareLinks = shareLinks;
        _playlists.Changed += (_, _) => OnUi(ReloadList);
        ReloadList();
    }

    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<Track> SelectedTracks { get; } = new();

    [ObservableProperty] private Playlist? _selectedPlaylist;
    [ObservableProperty] private string _newPlaylistName = string.Empty;
    [ObservableProperty] private bool _hasSelection;

    partial void OnSelectedPlaylistChanged(Playlist? value)
    {
        HasSelection = value is not null;
        ReloadTracks();
    }

    [RelayCommand]
    private void Create()
    {
        var name = string.IsNullOrWhiteSpace(NewPlaylistName) ? "New Playlist" : NewPlaylistName.Trim();
        var created = _playlists.Create(name);
        NewPlaylistName = string.Empty;
        ReloadList();
        SelectedPlaylist = FindById(created.Id);
    }

    [RelayCommand]
    private void Delete(Playlist? playlist)
    {
        var target = playlist ?? SelectedPlaylist;
        if (target is null) return;
        _playlists.Delete(target.Id);
        if (ReferenceEquals(target, SelectedPlaylist)) SelectedPlaylist = null;
    }

    [RelayCommand]
    private void Play()
    {
        if (SelectedPlaylist is null) return;
        _coordinator.Play(_playlists.ResolveTracks(SelectedPlaylist.Id));
    }

    [RelayCommand]
    private void Shuffle()
    {
        if (SelectedPlaylist is null) return;
        _coordinator.PlayShuffled(_playlists.ResolveTracks(SelectedPlaylist.Id));
    }

    [RelayCommand]
    private void AddToQueue()
    {
        if (SelectedPlaylist is null) return;
        _queue.Enqueue(_playlists.ResolveTracks(SelectedPlaylist.Id));
    }

    [RelayCommand]
    private void Randomize()
    {
        if (SelectedPlaylist is null) return;
        _playlists.Randomize(SelectedPlaylist.Id);
        ReloadTracks();
    }

    [RelayCommand]
    private void Sort(TrackSortField field)
    {
        if (SelectedPlaylist is null) return;
        _playlists.SetSort(SelectedPlaylist.Id, field, SortDirection.Ascending);
        ReloadTracks();
    }

    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        if (track is null) return;
        var index = SelectedTracks.IndexOf(track);
        var list = new List<Track>(SelectedTracks);
        if (index >= 0) _coordinator.Play(list, index);
    }

    /// <summary>True where guest share links can be created (host heads).</summary>
    public bool CanShare => _shareLinks.CanShare;

    [ObservableProperty] private string _shareStatus = string.Empty;

    public bool HasShareStatus => !string.IsNullOrEmpty(ShareStatus);

    partial void OnShareStatusChanged(string value) => OnPropertyChanged(nameof(HasShareStatus));

    [RelayCommand]
    private async Task Share(Playlist? playlist)
    {
        var target = playlist ?? SelectedPlaylist;
        if (target is null) return;
        var result = await _shareLinks.CreateAsync(ShareKind.Playlist, target.Id, target.Name);
        if (result is null) { await SetShareStatus("Sign in as a User or Admin to create share links."); return; }
        var copied = await Services.AppClipboard.SetTextAsync(result.Url);
        await SetShareStatus(copied ? "Share link copied to clipboard." : "Share link created.");
    }

    private async Task SetShareStatus(string message)
    {
        ShareStatus = message;
        await Task.Delay(3500);
        if (ShareStatus == message) ShareStatus = string.Empty;
    }

    [RelayCommand]
    private void RemoveTrack(Track? track)
    {
        if (track is null || SelectedPlaylist is null) return;
        var index = SelectedTracks.IndexOf(track);
        if (index >= 0)
        {
            _playlists.RemoveTrackAt(SelectedPlaylist.Id, index);
            ReloadTracks();
        }
    }

    private void ReloadList()
    {
        var previousId = SelectedPlaylist?.Id;
        Playlists.Clear();
        foreach (var p in _playlists.Playlists) Playlists.Add(p);
        if (previousId is not null) SelectedPlaylist = FindById(previousId);
    }

    private void ReloadTracks()
    {
        SelectedTracks.Clear();
        if (SelectedPlaylist is null) return;
        foreach (var t in _playlists.ResolveTracks(SelectedPlaylist.Id)) SelectedTracks.Add(t);
    }

    private Playlist? FindById(string id)
    {
        foreach (var p in Playlists) if (p.Id == id) return p;
        return null;
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
