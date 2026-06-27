using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// A user's profile page. Shows the profile, and — for facets the viewer is allowed to see — their
/// favourites, now playing and listening history. Editing (bio, picture, privacy, and for the built-in user
/// the username + remote-login credentials) is allowed only for one's own profile, or for an admin.
/// </summary>
public sealed partial class UserProfileViewModel : ViewModelBase
{
    private readonly IUserService _users;
    private readonly IFavoritesService _favorites;
    private readonly IListeningHistoryService _history;
    private readonly ILibraryService _library;
    private readonly IPlaybackService _playback;
    private readonly IServerUserStore? _serverUsers;

    private UserProfile _target = new() { Username = string.Empty };

    public UserProfileViewModel(IUserService users, IFavoritesService favorites, IListeningHistoryService history,
        ILibraryService library, IPlaybackService playback, IServerUserStore? serverUsers = null)
    {
        _users = users;
        _favorites = favorites;
        _history = history;
        _library = library;
        _playback = playback;
        _serverUsers = serverUsers;

        _favorites.Changed += (_, _) => OnUi(RefreshData);
        _history.Changed += (_, _) => OnUi(RefreshData);
        _playback.StateChanged += (_, _) => OnUi(() => OnPropertyChanged(nameof(NowPlayingText)));
    }

    public ObservableCollection<Track> FavoriteSongs { get; } = new();
    public ObservableCollection<Track> RecentTracks { get; } = new();

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string? _bio;
    [ObservableProperty] private string? _profilePicturePath;
    [ObservableProperty] private bool _isProfilePublic;
    [ObservableProperty] private bool _isFavoritesPublic;
    [ObservableProperty] private bool _isNowPlayingPublic;
    [ObservableProperty] private bool _isHistoryPublic;

    [ObservableProperty] private bool _isSelf;
    [ObservableProperty] private bool _isBuiltIn;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private bool _remoteLoginEnabled;

    // Built-in credential setup (enables web/server login).
    [ObservableProperty] private string _credentialUsername = string.Empty;
    [ObservableProperty] private string _credentialPassword = string.Empty;
    [ObservableProperty] private string _credentialStatus = string.Empty;

    public bool CanSeeFavorites { get; private set; }
    public bool CanSeeNowPlaying { get; private set; }
    public bool CanSeeHistory { get; private set; }

    public int FavoriteAlbumCount { get; private set; }
    public int FavoriteArtistCount { get; private set; }

    public string NowPlayingText =>
        CanSeeNowPlaying && _playback.Current is { } t ? $"{t.Title} — {t.DisplayArtist}" : "Nothing playing";

    public void Load(string username)
    {
        var viewer = _users.Current;
        _target = _users.Get(username) ?? new UserProfile { Username = username };

        Username = _target.Username;
        Bio = _target.Bio;
        ProfilePicturePath = _target.ProfilePicturePath;
        IsProfilePublic = _target.ProfileVisibility == PrivacyVisibility.Public;
        IsFavoritesPublic = _target.FavoritesVisibility == PrivacyVisibility.Public;
        IsNowPlayingPublic = _target.NowPlayingVisibility == PrivacyVisibility.Public;
        IsHistoryPublic = _target.ListeningHistoryVisibility == PrivacyVisibility.Public;
        IsBuiltIn = _target.IsBuiltIn;
        IsSelf = string.Equals(viewer.Username, _target.Username, System.StringComparison.OrdinalIgnoreCase);
        CanEdit = _users.CanEdit(viewer, _target.Username);
        RemoteLoginEnabled = _target.RemoteLoginEnabled;
        CredentialUsername = _target.Username;

        CanSeeFavorites = _users.CanView(viewer, _target, ProfileFacet.Favorites);
        CanSeeNowPlaying = _users.CanView(viewer, _target, ProfileFacet.NowPlaying);
        CanSeeHistory = _users.CanView(viewer, _target, ProfileFacet.ListeningHistory);
        OnPropertyChanged(nameof(CanSeeFavorites));
        OnPropertyChanged(nameof(CanSeeNowPlaying));
        OnPropertyChanged(nameof(CanSeeHistory));
        OnPropertyChanged(nameof(NowPlayingText));

        RefreshData();
    }

    private void RefreshData()
    {
        FavoriteSongs.Clear();
        RecentTracks.Clear();

        // Favourites and history come from the current user's own services (the built-in user on desktop, or
        // the signed-in account on the web), so show them on that user's own page.
        if (IsSelf)
        {
            if (CanSeeFavorites)
            {
                var albums = 0; var artists = 0;
                foreach (var fav in _favorites.Items)
                {
                    switch (fav.Kind)
                    {
                        case FavoriteKind.Song when _library.FindTrack(fav.Key) is { } t: FavoriteSongs.Add(t); break;
                        case FavoriteKind.Album: albums++; break;
                        case FavoriteKind.Artist: artists++; break;
                    }
                }
                FavoriteAlbumCount = albums;
                FavoriteArtistCount = artists;
                OnPropertyChanged(nameof(FavoriteAlbumCount));
                OnPropertyChanged(nameof(FavoriteArtistCount));
            }

            if (CanSeeHistory)
                foreach (var entry in _history.Recent)
                    if (_library.FindTrack(entry.TrackId) is { } t)
                    {
                        RecentTracks.Add(t);
                        if (RecentTracks.Count >= 50) break;
                    }
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanEdit) return;
        var updated = _target.Clone();
        if (_target.IsBuiltIn && !string.IsNullOrWhiteSpace(Username)) updated.Username = Username.Trim();
        updated.Bio = string.IsNullOrWhiteSpace(Bio) ? null : Bio.Trim();
        updated.ProfilePicturePath = ProfilePicturePath;
        updated.ProfileVisibility = IsProfilePublic ? PrivacyVisibility.Public : PrivacyVisibility.Private;
        updated.FavoritesVisibility = IsFavoritesPublic ? PrivacyVisibility.Public : PrivacyVisibility.Private;
        updated.NowPlayingVisibility = IsNowPlayingPublic ? PrivacyVisibility.Public : PrivacyVisibility.Private;
        updated.ListeningHistoryVisibility = IsHistoryPublic ? PrivacyVisibility.Public : PrivacyVisibility.Private;

        if (_users.UpdateProfile(_users.Current.Username, updated))
            Load(updated.Username); // re-read canonical state
    }

    /// <summary>Built-in user only: set a username + password, which creates the admin account used to log in
    /// from the web/server and flips on remote login.</summary>
    [RelayCommand]
    private void SetCredentials()
    {
        if (!_target.IsBuiltIn || !CanEdit) return;
        var user = CredentialUsername?.Trim() ?? string.Empty;
        if (user.Length == 0 || CredentialPassword.Length < 4)
        {
            CredentialStatus = "Enter a username and a password of at least 4 characters.";
            return;
        }
        if (_serverUsers is null)
        {
            CredentialStatus = "Remote login isn't available in this build.";
            return;
        }

        _serverUsers.AddOrUpdate(user, CredentialPassword, UserRole.Admin);

        var updated = _target.Clone();
        updated.Username = user;
        updated.RemoteLoginEnabled = true;
        _users.UpdateProfile(_users.Current.Username, updated);
        CredentialPassword = string.Empty;
        CredentialStatus = "Remote login enabled.";
        Load(user);
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
