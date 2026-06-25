using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Muselly.App.ViewModels;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.Services;

/// <summary>
/// Drives the shell's main content region with a browser-style history. Every destination is recorded as a
/// history entry and pushed onto a back stack, so the user can move back/forward freely without losing
/// where they were — including switching between the Library tabs (Albums/Artists/Songs/Folders), which are
/// each their own entry. Section view models are singletons (resolved lazily to avoid construction cycles);
/// album/artist/now-playing pages are built on demand.
/// </summary>
public interface INavigationService
{
    object? Current { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    event EventHandler? Changed;

    /// <summary>A stable, URL-friendly identifier of the current destination (e.g. "albums", "album/{key}").</summary>
    string CurrentRoute { get; }

    void ShowLibrary();
    void ShowLibraryTab(int tabIndex);
    void ShowPlaylists();
    void ShowConnect();
    void ShowSettings();
    void ShowNowPlaying();
    void OpenAlbum(string albumKey);
    void OpenArtist(string artistKey);

    /// <summary>Navigates to a destination identified by <see cref="CurrentRoute"/> (used for URL deep-links).</summary>
    void NavigateRoute(string route);

    void GoBack();
    void GoForward();
}

public sealed class NavigationService : INavigationService
{
    /// <summary>A history entry: the page to display, the Library tab to restore (-1 = not a tab), and the route.</summary>
    private sealed record NavEntry(object Page, int TabIndex, string Route);

    private readonly IServiceProvider _sp;
    private readonly ILibraryService _library;
    private readonly PlaybackCoordinator _coordinator;
    private readonly IQueueService _queue;
    private readonly IPlaylistService _playlists;
    private readonly Stack<NavEntry> _back = new();
    private readonly Stack<NavEntry> _forward = new();
    private NavEntry? _current;

    public NavigationService(IServiceProvider sp, ILibraryService library, PlaybackCoordinator coordinator,
        IQueueService queue, IPlaylistService playlists)
    {
        _sp = sp;
        _library = library;
        _coordinator = coordinator;
        _queue = queue;
        _playlists = playlists;
    }

    public object? Current => _current?.Page;
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public string CurrentRoute => _current?.Route ?? "albums";
    public event EventHandler? Changed;

    public void ShowLibrary()
    {
        var lib = _sp.GetRequiredService<LibraryViewModel>();
        Navigate(new NavEntry(lib, lib.SelectedTabIndex, TabRoute(lib.SelectedTabIndex)));
    }

    public void ShowLibraryTab(int tabIndex)
    {
        var lib = _sp.GetRequiredService<LibraryViewModel>();
        Navigate(new NavEntry(lib, tabIndex, TabRoute(tabIndex)));
    }

    public void ShowPlaylists() => Navigate(new NavEntry(_sp.GetRequiredService<PlaylistsViewModel>(), -1, "playlists"));
    public void ShowConnect() => Navigate(new NavEntry(_sp.GetRequiredService<ConnectViewModel>(), -1, "connect"));
    public void ShowSettings() => Navigate(new NavEntry(_sp.GetRequiredService<SettingsViewModel>(), -1, "settings"));
    public void ShowNowPlaying() => Navigate(new NavEntry(_sp.GetRequiredService<NowPlayingViewModel>(), -1, "nowplaying"));

    public void OpenAlbum(string albumKey)
    {
        var album = _library.FindAlbum(albumKey);
        if (album is null) return;
        Navigate(new NavEntry(new AlbumDetailViewModel(album, _coordinator, _queue, _playlists, GoBack, OpenArtist),
            -1, "album/" + albumKey));
    }

    public void OpenArtist(string artistKey)
    {
        var artist = _library.FindArtist(artistKey);
        if (artist is null) return;
        var artistInfo = _sp.GetRequiredService<Muselly.Core.Services.Web.IArtistInfoService>();
        var settings = _sp.GetRequiredService<ISettingsService>();
        Navigate(new NavEntry(new ArtistDetailViewModel(artist, _coordinator, _queue, artistInfo,
            settings.Current.AutomaticArtistBiography, a => OpenAlbum(a.Key), GoBack), -1, "artist/" + artistKey));
    }

    public void NavigateRoute(string route)
    {
        route = (route ?? string.Empty).Trim('/');
        if (route.Length == 0) { ShowLibraryTab(0); return; }

        var slash = route.IndexOf('/');
        if (slash > 0)
        {
            var kind = route[..slash];
            var key = System.Uri.UnescapeDataString(route[(slash + 1)..]);
            if (kind == "album") OpenAlbum(key);
            else if (kind == "artist") OpenArtist(key);
            else ShowLibraryTab(0);
            return;
        }

        switch (route)
        {
            case "albums": ShowLibraryTab(0); break;
            case "artists": ShowLibraryTab(1); break;
            case "songs": ShowLibraryTab(2); break;
            case "folders": ShowLibraryTab(3); break;
            case "playlists": ShowPlaylists(); break;
            case "connect": ShowConnect(); break;
            case "settings": ShowSettings(); break;
            case "nowplaying": ShowNowPlaying(); break;
            default: ShowLibraryTab(0); break;
        }
    }

    private static string TabRoute(int tabIndex) => tabIndex switch
    {
        1 => "artists",
        2 => "songs",
        3 => "folders",
        _ => "albums"
    };

    public void GoBack()
    {
        if (_back.Count == 0) return;
        if (_current is not null) _forward.Push(_current);
        _current = _back.Pop();
        Apply(_current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void GoForward()
    {
        if (_forward.Count == 0) return;
        if (_current is not null) _back.Push(_current);
        _current = _forward.Pop();
        Apply(_current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Navigate(NavEntry entry)
    {
        // Same page AND same tab → just re-assert (no new history entry).
        if (_current is not null && ReferenceEquals(_current.Page, entry.Page) && _current.TabIndex == entry.TabIndex)
        {
            Apply(entry);
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_current is not null) _back.Push(_current);
        _forward.Clear();
        _current = entry;
        Apply(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Apply(NavEntry entry)
    {
        // Restore the Library tab for a library entry (set directly so it doesn't re-navigate).
        if (entry.Page is LibraryViewModel lib && entry.TabIndex >= 0)
            lib.SelectedTabIndex = entry.TabIndex;
    }
}
