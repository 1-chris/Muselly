using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;

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
    private readonly IFavoritesService _favorites;
    private readonly Action _back;
    private readonly Action<string> _openArtist;

    public AlbumDetailViewModel(Album album, PlaybackCoordinator coordinator, IQueueService queue,
        IPlaylistService playlists, IFavoritesService favorites, Action back, Action<string> openArtist)
    {
        Album = album;
        _coordinator = coordinator;
        _queue = queue;
        _playlists = playlists;
        _favorites = favorites;
        _back = back;
        _openArtist = openArtist;
        _isFavorite = favorites.IsFavorite(FavoriteKind.Album, album.Key);
        favorites.Changed += OnFavoritesChanged;
        Discs = BuildDiscs(album);
    }

    private static IReadOnlyList<AlbumDisc> BuildDiscs(Album album)
    {
        var multi = album.DiscCount > 1;
        var groups = new List<AlbumDisc>();
        List<Track>? current = null;
        uint currentDisc = 0;

        // Tracks arrive ordered by disc then track number, so discs are contiguous — split on each change.
        foreach (var t in album.Tracks)
        {
            var disc = t.DiscNumber == 0 ? 1u : t.DiscNumber;
            if (current is null || disc != currentDisc)
            {
                current = new List<Track>();
                groups.Add(new AlbumDisc(disc, current, multi));
                currentDisc = disc;
            }
            current.Add(t);
        }
        return groups;
    }

    [ObservableProperty] private bool _isFavorite;

    private void OnFavoritesChanged(object? sender, EventArgs e) =>
        IsFavorite = _favorites.IsFavorite(FavoriteKind.Album, Album.Key);

    [RelayCommand] private void ToggleFavorite() => _favorites.Toggle(FavoriteKind.Album, Album.Key);

    public Album Album { get; }

    /// <summary>Stable key for remembering this page's scroll position across back/forward navigation.</summary>
    public string ScrollKey => "album/" + Album.Key;

    public IReadOnlyList<Track> Tracks => Album.Tracks;

    /// <summary>True when the album spans more than one disc (drives the per-disc headers/separators).</summary>
    public bool HasMultipleDiscs => Album.DiscCount > 1;

    /// <summary>The album's tracks grouped into discs (already ordered disc-then-track by the library). For a
    /// single-disc album this is one group with no header.</summary>
    public IReadOnlyList<AlbumDisc> Discs { get; }

    /// <summary>The album-artist's grouping key, for navigating to the artist page. Derived from the album
    /// artist (not a track) so a compilation header links to "Various Artists", while each track row still
    /// links to its own performer.</summary>
    public string ArtistKey => Identifiers.ArtistKey(Album.AlbumArtist);

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

/// <summary>One disc's worth of an album's tracks, with a header shown only on multi-disc albums.</summary>
public sealed class AlbumDisc
{
    public AlbumDisc(uint number, IReadOnlyList<Track> tracks, bool showHeader)
    {
        Number = number;
        Tracks = tracks;
        ShowHeader = showHeader;
    }

    public uint Number { get; }
    public IReadOnlyList<Track> Tracks { get; }
    public bool ShowHeader { get; }
    public string Title => $"Disc {Number}";
}
