using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// The Favourites page: the user's favourited albums, artists and songs (newest first), resolved against the
/// current library. Items open/play just like the library views, and can be un-favourited from here.
/// </summary>
public sealed partial class FavoritesViewModel : ViewModelBase
{
    private readonly IFavoritesService _favorites;
    private readonly ILibraryService _library;
    private readonly PlaybackCoordinator _coordinator;
    private readonly INavigationService _nav;

    public FavoritesViewModel(IFavoritesService favorites, ILibraryService library,
        PlaybackCoordinator coordinator, INavigationService nav)
    {
        _favorites = favorites;
        _library = library;
        _coordinator = coordinator;
        _nav = nav;

        _favorites.Changed += (_, _) => OnUi(Reload);
        _library.LibraryChanged += (_, _) => OnUi(Reload);
        Reload();
    }

    public ObservableCollection<Album> Albums { get; } = new();
    public ObservableCollection<Artist> Artists { get; } = new();
    public ObservableCollection<Track> Songs { get; } = new();

    public bool IsEmpty => Albums.Count == 0 && Artists.Count == 0 && Songs.Count == 0;
    public bool HasAlbums => Albums.Count > 0;
    public bool HasArtists => Artists.Count > 0;
    public bool HasSongs => Songs.Count > 0;

    private void Reload()
    {
        Albums.Clear();
        Artists.Clear();
        Songs.Clear();

        // _favorites.Items is already newest-first.
        foreach (var fav in _favorites.Items)
        {
            switch (fav.Kind)
            {
                case FavoriteKind.Album when _library.FindAlbum(fav.Key) is { } a: Albums.Add(a); break;
                case FavoriteKind.Artist when _library.FindArtist(fav.Key) is { } ar: Artists.Add(ar); break;
                case FavoriteKind.Song when _library.FindTrack(fav.Key) is { } t: Songs.Add(t); break;
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasAlbums));
        OnPropertyChanged(nameof(HasArtists));
        OnPropertyChanged(nameof(HasSongs));
    }

    [RelayCommand] private void OpenAlbum(Album? album) { if (album is not null) _nav.OpenAlbum(album.Key); }
    [RelayCommand] private void OpenArtist(Artist? artist) { if (artist is not null) _nav.OpenArtist(artist.Key); }
    [RelayCommand] private void PlayAlbum(Album? album) { if (album is not null) _coordinator.Play(album.Tracks); }

    [RelayCommand]
    private void PlaySong(Track? track)
    {
        if (track is null) return;
        var index = 0;
        for (var i = 0; i < Songs.Count; i++)
            if (Songs[i].Id == track.Id) { index = i; break; }
        _coordinator.Play(Songs, index);
    }

    [RelayCommand] private void UnfavoriteAlbum(Album? a) { if (a is not null) _favorites.Remove(FavoriteKind.Album, a.Key); }
    [RelayCommand] private void UnfavoriteArtist(Artist? a) { if (a is not null) _favorites.Remove(FavoriteKind.Artist, a.Key); }
    [RelayCommand] private void UnfavoriteSong(Track? t) { if (t is not null) _favorites.Remove(FavoriteKind.Song, t.Id); }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
