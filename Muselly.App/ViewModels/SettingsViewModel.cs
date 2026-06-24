using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.App.Theming;
using Muselly.Core.Audio;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;

namespace Muselly.App.ViewModels;

/// <summary>
/// The settings page: choose the folders scanned for music (via the native picker), rescan the library
/// with live progress, switch the Catppuccin theme, adjust UI scale, and review supported formats and
/// library stats.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILibraryService _library;
    private readonly IThemeService _themes;
    private readonly IFolderPicker _folderPicker;
    private readonly IAudioEqualizer _equalizer;
    private readonly IArtworkScanService _artworkScan;

    public SettingsViewModel(ISettingsService settings, ILibraryService library, IThemeService themes,
        IFolderPicker folderPicker, IAudioEqualizer equalizer, IArtworkScanService artworkScan)
    {
        _settings = settings;
        _library = library;
        _themes = themes;
        _folderPicker = folderPicker;
        _equalizer = equalizer;
        _artworkScan = artworkScan;

        _scanSubdirectories = settings.Current.ScanSubdirectories;
        _fontScale = settings.Current.FontScale;
        _currentThemeName = settings.Current.ThemeName;
        _eqEnabled = equalizer.Enabled;
        _automaticArtistBiography = settings.Current.AutomaticArtistBiography;

        for (var i = 0; i < equalizer.BandCount; i++)
            EqBands.Add(new EqBandViewModel(equalizer, i));

        _library.ScanProgressChanged += (_, p) => OnUi(() => UpdateProgress(p));
        _library.LibraryChanged += (_, _) => OnUi(RefreshStats);
        _artworkScan.ProgressChanged += (_, p) => OnUi(() => UpdateArtworkProgress(p));

        ReloadFolders();
        RefreshStats();
    }

    public ObservableCollection<string> MusicFolders { get; } = new();
    public IReadOnlyList<ThemeDefinition> Themes => _themes.BuiltIns;
    public IReadOnlyList<string> SupportedFormats => AudioFormats.DisplayNames;
    public ObservableCollection<EqBandViewModel> EqBands { get; } = new();
    public IReadOnlyList<string> EqPresets { get; } = new[] { "Flat", "Bass Boost", "Treble Boost", "Vocal", "Rock", "Loudness" };

    [ObservableProperty] private bool _scanSubdirectories;
    [ObservableProperty] private double _fontScale;
    [ObservableProperty] private string _currentThemeName;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanFraction;
    [ObservableProperty] private string _scanStatus = "Idle";
    [ObservableProperty] private int _trackCount;
    [ObservableProperty] private int _albumCount;
    [ObservableProperty] private int _artistCount;
    [ObservableProperty] private bool _eqEnabled;
    [ObservableProperty] private bool _automaticArtistBiography;
    [ObservableProperty] private bool _isArtworkScanning;
    [ObservableProperty] private double _artworkScanFraction;
    [ObservableProperty] private string _artworkScanStatus = "Find cover art for albums that are missing it.";

    partial void OnEqEnabledChanged(bool value) => _equalizer.Enabled = value;

    partial void OnAutomaticArtistBiographyChanged(bool value) =>
        _settings.Update(s => s.AutomaticArtistBiography = value);

    [RelayCommand]
    private async Task ScanMissingArtwork()
    {
        if (IsArtworkScanning) return;
        await _artworkScan.ScanMissingAsync();
    }

    partial void OnScanSubdirectoriesChanged(bool value) => _settings.Update(s => s.ScanSubdirectories = value);

    partial void OnFontScaleChanged(double value)
    {
        App.ApplyFontScale(value);
        _settings.Update(s => s.FontScale = value);
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        var path = await _folderPicker.PickFolderAsync("Add a music folder");
        if (string.IsNullOrWhiteSpace(path)) return;
        _settings.AddMusicFolder(path);
        ReloadFolders();
        await Rescan();
    }

    [RelayCommand]
    private void RemoveFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _settings.RemoveMusicFolder(path);
        ReloadFolders();
    }

    [RelayCommand]
    private async Task Rescan()
    {
        if (IsScanning) return;
        await _library.ScanAsync();
    }

    [RelayCommand]
    private void ApplyTheme(ThemeDefinition? theme)
    {
        if (theme is null) return;
        _themes.Apply(theme);
        CurrentThemeName = theme.Name;
        _settings.Update(s => s.ThemeName = theme.Name);
    }

    [RelayCommand]
    private void ResetEq()
    {
        _equalizer.Reset();
        foreach (var band in EqBands) band.SyncFromEq();
    }

    [RelayCommand]
    private void ApplyEqPreset(string? preset)
    {
        var curve = preset switch
        {
            // 31 62 125 250 500 1k 2k 4k 8k 16k
            "Bass Boost"   => new[] { 6.0, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
            "Treble Boost" => new[] { 0.0, 0, 0, 0, 0, 1, 3, 5, 6, 6 },
            "Vocal"        => new[] { -2.0, -1, 0, 2, 4, 4, 3, 1, 0, -1 },
            "Rock"         => new[] { 4.0, 3, 1, 0, -1, -1, 0, 2, 3, 4 },
            "Loudness"     => new[] { 5.0, 4, 2, 0, -1, -1, 0, 2, 4, 5 },
            _              => new[] { 0.0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
        };

        for (var i = 0; i < EqBands.Count && i < curve.Length; i++)
            EqBands[i].Gain = curve[i];

        if (!EqEnabled) EqEnabled = true;
    }

    private void ReloadFolders()
    {
        MusicFolders.Clear();
        foreach (var f in _settings.Current.MusicFolders) MusicFolders.Add(f);
    }

    private void UpdateProgress(ScanProgress p)
    {
        IsScanning = p.Phase != ScanPhase.Completed && p.Phase != ScanPhase.Idle;
        ScanFraction = p.Fraction;
        ScanStatus = p.Phase switch
        {
            ScanPhase.Discovering => "Discovering files…",
            ScanPhase.Reading => $"Reading metadata… {p.Processed}/{p.Total}",
            ScanPhase.Organizing => "Organising library…",
            ScanPhase.Completed => $"Done — {p.Total} tracks",
            _ => "Idle"
        };
    }

    private void UpdateArtworkProgress(ArtworkScanProgress p)
    {
        IsArtworkScanning = p.IsRunning;
        ArtworkScanFraction = p.Fraction;
        if (p.IsRunning)
            ArtworkScanStatus = $"Searching… {p.Processed}/{p.Total}  •  {p.Found} found"
                                + (string.IsNullOrEmpty(p.CurrentItem) ? "" : $"  •  {p.CurrentItem}");
        else if (p.Total > 0)
            ArtworkScanStatus = $"Done — added {p.Found} cover(s) for {p.Total} album(s) without art.";
        else
            ArtworkScanStatus = "Every album already has cover art.";
    }

    private void RefreshStats()
    {
        TrackCount = _library.Tracks.Count;
        AlbumCount = _library.Albums.Count;
        ArtistCount = _library.Artists.Count;
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
