using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// The album detail page: header (art, title, artist, year, stats) plus the track list and the full set of
/// queue/playlist actions (play, play shuffled, add to queue, add shuffled, add to a playlist).
/// </summary>
public sealed partial class AlbumDetailViewModel : ViewModelBase
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly IQueueService _queue;
    private readonly IPlaylistService _playlists;
    private readonly Action _back;
    private readonly Action<string> _openArtist;

    public AlbumDetailViewModel(Album album, PlaybackCoordinator coordinator, IQueueService queue,
        IPlaylistService playlists, Action back, Action<string> openArtist)
    {
        Album = album;
        _coordinator = coordinator;
        _queue = queue;
        _playlists = playlists;
        _back = back;
        _openArtist = openArtist;
    }

    public Album Album { get; }

    public IReadOnlyList<Track> Tracks => Album.Tracks;

    /// <summary>The album-artist's grouping key (tracks share it), for navigating to the artist page.</summary>
    public string ArtistKey => Album.Tracks.Count > 0 ? Album.Tracks[0].ArtistKey : string.Empty;

    public string MetaLine =>
        $"{(Album.Year > 0 ? Album.Year + "  •  " : "")}{Album.TrackCount} tracks";

    public IReadOnlyList<Playlist> Playlists => _playlists.Playlists;

    [RelayCommand] private void Back() => _back();

    [RelayCommand]
    private void OpenArtist()
    {
        if (!string.IsNullOrEmpty(ArtistKey)) _openArtist(ArtistKey);
    }

    [RelayCommand]
    private void OpenTrackArtist(Track? track)
    {
        if (track is not null && !string.IsNullOrEmpty(track.ArtistKey)) _openArtist(track.ArtistKey);
    }
    [RelayCommand] private void Play() => _coordinator.Play(Album.Tracks);
    [RelayCommand] private void PlayShuffled() => _coordinator.PlayShuffled(Album.Tracks);
    [RelayCommand] private void PlayNext() => _queue.EnqueueNext(Album.Tracks);
    [RelayCommand] private void AddToQueue() => _queue.Enqueue(Album.Tracks);
    [RelayCommand] private void AddToQueueShuffled() => _queue.EnqueueShuffled(Album.Tracks);

    [RelayCommand]
    private void PlayTrackNext(Track? track)
    {
        if (track is not null) _queue.EnqueueNext(new[] { track });
    }

    [RelayCommand]
    private void PlayTrack(Track? track)
    {
        if (track is null) return;
        var index = IndexOf(track);
        if (index >= 0) _coordinator.Play(Album.Tracks, index);
    }

    [RelayCommand]
    private void AddTrackToQueue(Track? track)
    {
        if (track is not null) _queue.Enqueue(new[] { track });
    }

    [RelayCommand]
    private void AddToPlaylist(Playlist? playlist)
    {
        if (playlist is null) return;
        var ids = new List<string>(Album.Tracks.Count);
        foreach (var t in Album.Tracks) ids.Add(t.Id);
        _playlists.AddTracks(playlist.Id, ids);
    }

    private int IndexOf(Track track)
    {
        for (var i = 0; i < Album.Tracks.Count; i++)
            if (ReferenceEquals(Album.Tracks[i], track) || Album.Tracks[i].Id == track.Id)
                return i;
        return -1;
    }
}
