using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.App.Theming;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;

namespace Muselly.App.ViewModels;

/// <summary>
/// Root view model for the shell. Owns the navigation rail (Library / Playlists / Settings), the back /
/// forward history (via <see cref="INavigationService"/>) that drives the content region, the always-on
/// player bar and the queue drawer. It also runs startup work: loading the cached library and playlists,
/// restoring the saved theme, and kicking off an initial scan when folders are configured but nothing is
/// cached yet.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _nav;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlists;
    private readonly ISettingsService _settings;
    private readonly IThemeService _themes;
    private readonly IArtistInfoService _artistInfo;
    private bool _syncingNav;

    public MainViewModel(
        INavigationService nav,
        PlayerBarViewModel player,
        QueueViewModel queue,
        LyricsViewModel lyrics,
        LibraryViewModel libraryViewModel,
        ILibraryService libraryService,
        IPlaylistService playlistService,
        ISettingsService settingsService,
        IThemeService themes,
        IArtistInfoService artistInfo)
    {
        _nav = nav;
        Player = player;
        Queue = queue;
        Lyrics = lyrics;
        Library = libraryViewModel;
        _library = libraryService;
        _playlists = playlistService;
        _settings = settingsService;
        _themes = themes;
        _artistInfo = artistInfo;

        Player.QueueToggleRequested = () => IsQueueOpen = !IsQueueOpen;
        Player.LyricsToggleRequested = () => IsLyricsOpen = !IsLyricsOpen;
        _nav.Changed += (_, _) => OnUi(OnNavChanged);

        _nav.ShowLibrary();
    }

    public PlayerBarViewModel Player { get; }
    public QueueViewModel Queue { get; }
    public LyricsViewModel Lyrics { get; }
    public LibraryViewModel Library { get; }

    public string Title => AppInfo.Name;
    public string Version => $"v{AppInfo.Version}";

    public object? CurrentContent => _nav.Current;
    public bool CanGoBack => _nav.CanGoBack;
    public bool CanGoForward => _nav.CanGoForward;

    [ObservableProperty] private int _selectedNavIndex;
    [ObservableProperty] private bool _isQueueOpen;
    [ObservableProperty] private bool _isLyricsOpen;

    /// <summary>True while a Library tab is shown, so the shared top bar can reveal the search box.</summary>
    [ObservableProperty] private bool _isLibraryActive = true;

    partial void OnIsQueueOpenChanged(bool value)
    {
        // The queue and lyrics share the right-hand drawer space; only one is open at a time.
        if (value) IsLyricsOpen = false;
    }

    partial void OnIsLyricsOpenChanged(bool value)
    {
        if (value) IsQueueOpen = false;
        Lyrics.IsOpen = value;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        if (_syncingNav) return;
        switch (value)
        {
            case 0: _nav.ShowLibraryTab(0); break; // Albums
            case 1: _nav.ShowLibraryTab(1); break; // Artists
            case 2: _nav.ShowLibraryTab(2); break; // Songs
            case 3: _nav.ShowLibraryTab(3); break; // Folders
            case 4: _nav.ShowPlaylists(); break;
            case 5: _nav.ShowConnect(); break;
            case 6: _nav.ShowSettings(); break;
        }
    }

    [RelayCommand] private void GoBack() => _nav.GoBack();
    [RelayCommand] private void GoForward() => _nav.GoForward();
    [RelayCommand] private void ToggleQueue() => IsQueueOpen = !IsQueueOpen;
    [RelayCommand] private void CloseQueue() => IsQueueOpen = false;
    [RelayCommand] private void ToggleLyrics() => IsLyricsOpen = !IsLyricsOpen;
    [RelayCommand] private void CloseLyrics() => IsLyricsOpen = false;

    private void OnNavChanged()
    {
        OnPropertyChanged(nameof(CurrentContent));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));

        IsLibraryActive = _nav.Current is LibraryViewModel;

        // Keep the rail selection in step with the current section (without re-triggering navigation).
        // The four library tabs map to rail slots 0-3, then Playlists (4) and Settings (5).
        var index = _nav.Current switch
        {
            LibraryViewModel lib => System.Math.Clamp(lib.SelectedTabIndex, 0, 3),
            PlaylistsViewModel => 4,
            ConnectViewModel => 5,
            SettingsViewModel => 6,
            _ => -1 // album/artist/now-playing pages: leave the rail as-is
        };
        if (index >= 0 && index != SelectedNavIndex)
        {
            _syncingNav = true;
            SelectedNavIndex = index;
            _syncingNav = false;
        }
    }

    /// <summary>Startup work, invoked by the platform head once the shell is shown.</summary>
    public async Task InitializeAsync()
    {
        App.ApplyFontScale(_settings.Current.FontScale <= 0 ? 1.0 : _settings.Current.FontScale);
        ApplySavedTheme();

        await _library.LoadAsync();
        await _playlists.LoadAsync();
        await _artistInfo.LoadAsync();

        if (_library.Tracks.Count == 0 && _settings.Current.MusicFolders.Count > 0)
            await _library.ScanAsync();
    }

    private void ApplySavedTheme()
    {
        var name = _settings.Current.ThemeName;
        foreach (var theme in _themes.BuiltIns)
        {
            if (theme.Name == name)
            {
                _themes.Apply(theme);
                return;
            }
        }
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
