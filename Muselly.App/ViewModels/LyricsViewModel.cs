using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;

namespace Muselly.App.ViewModels;

/// <summary>How Japanese lyrics are rendered.</summary>
public enum LyricsScript
{
    Original,
    Furigana,
    Romaji
}

/// <summary>One line of lyrics in the panel; <see cref="IsActive"/> drives the current-line highlight.
/// For Japanese lyrics it also carries a romaji form and furigana segmentation.</summary>
public sealed partial class LyricsLineViewModel : ObservableObject
{
    public LyricsLineViewModel(string text, int? timeMs)
    {
        OriginalText = string.IsNullOrEmpty(text) ? "♪" : text;
        TimeMs = timeMs;
    }

    public string OriginalText { get; }
    public int? TimeMs { get; }

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string? _romajiText;
    [ObservableProperty] private System.Collections.Generic.IReadOnlyList<Services.FuriganaSegment>? _furiganaSegments;
    [ObservableProperty] private LyricsScript _script;

    /// <summary>The text shown by the plain TextBlock (original or romaji).</summary>
    public string DisplayText =>
        Script == LyricsScript.Romaji && !string.IsNullOrEmpty(RomajiText) ? RomajiText! : OriginalText;

    /// <summary>True when this line should render as furigana segments instead of a plain TextBlock.</summary>
    public bool ShowFurigana => Script == LyricsScript.Furigana && FuriganaSegments is { Count: > 0 };

    partial void OnScriptChanged(LyricsScript value)
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ShowFurigana));
    }

    partial void OnRomajiTextChanged(string? value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnFuriganaSegmentsChanged(System.Collections.Generic.IReadOnlyList<Services.FuriganaSegment>? value) =>
        OnPropertyChanged(nameof(ShowFurigana));
}

/// <summary>
/// Backs the lyrics panel. When the current track changes it resolves lyrics (embedded → cache → web) and,
/// while playing, highlights the current line: exactly for synced lyrics, or by scrolling in proportion to
/// progress for plain lyrics. Clicking a synced line seeks to it.
/// </summary>
public sealed partial class LyricsViewModel : ViewModelBase
{
    private const double MinFontSize = 11;
    private const double MaxFontSize = 30;

    private readonly ILyricsService _lyrics;
    private readonly IPlaybackService _playback;
    private readonly IQueueService _queue;
    private readonly PlaybackCoordinator _coordinator;
    private readonly Services.IJapaneseTextService _japanese;
    private readonly ISettingsService _settings;

    private string? _loadedTrackId;
    private int _activeLineIndex = -1;
    private CancellationTokenSource? _loadCts;
    private bool _isOpen;

    public LyricsViewModel(ILyricsService lyrics, IPlaybackService playback, IQueueService queue,
        PlaybackCoordinator coordinator, Services.IJapaneseTextService japanese, ISettingsService settings)
    {
        _lyrics = lyrics;
        _playback = playback;
        _queue = queue;
        _coordinator = coordinator;
        _japanese = japanese;
        _settings = settings;

        _fontSize = Math.Clamp(settings.Current.LyricsFontSize <= 0 ? 15 : settings.Current.LyricsFontSize,
            MinFontSize, MaxFontSize);

        _queue.CurrentChanged += (_, _) => OnUi(OnTrackChanged);
        _playback.PositionChanged += (_, _) => OnUi(UpdateActiveLine);
    }

    public ObservableCollection<LyricsLineViewModel> Lines { get; } = new();

    [ObservableProperty] private bool _hasLyrics;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSynced;
    [ObservableProperty] private string _statusText = "Nothing playing";
    [ObservableProperty] private string? _sourceText;
    [ObservableProperty] private int _activeIndex = -1;
    [ObservableProperty] private string _trackTitle = string.Empty;
    [ObservableProperty] private bool _hasJapanese;
    [ObservableProperty] private LyricsScript _scriptMode = LyricsScript.Original;

    [ObservableProperty] private double _fontSize;

    /// <summary>Font size for the small furigana reading above kanji.</summary>
    public double RubyFontSize => Math.Round(FontSize * 0.62);

    /// <summary>Line height for lyric lines, scaled with the font size.</summary>
    public double LineSpacing => Math.Round(FontSize * 1.5);

    /// <summary>Reserved height for the furigana row so baselines stay aligned.</summary>
    public double RubyLineHeight => Math.Round(FontSize * 0.85);

    public bool CanIncreaseFont => FontSize < MaxFontSize;
    public bool CanDecreaseFont => FontSize > MinFontSize;

    partial void OnFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(RubyFontSize));
        OnPropertyChanged(nameof(LineSpacing));
        OnPropertyChanged(nameof(RubyLineHeight));
        OnPropertyChanged(nameof(CanIncreaseFont));
        OnPropertyChanged(nameof(CanDecreaseFont));
        _settings.Update(s => s.LyricsFontSize = value);
    }

    [RelayCommand]
    private void IncreaseFont() => FontSize = Math.Min(MaxFontSize, FontSize + 1);

    [RelayCommand]
    private void DecreaseFont() => FontSize = Math.Max(MinFontSize, FontSize - 1);

    public bool IsOriginalScript => ScriptMode == LyricsScript.Original;
    public bool IsFuriganaScript => ScriptMode == LyricsScript.Furigana;
    public bool IsRomajiScript => ScriptMode == LyricsScript.Romaji;

    partial void OnScriptModeChanged(LyricsScript value)
    {
        OnPropertyChanged(nameof(IsOriginalScript));
        OnPropertyChanged(nameof(IsFuriganaScript));
        OnPropertyChanged(nameof(IsRomajiScript));
        foreach (var line in Lines) line.Script = value;
    }

    [RelayCommand]
    private void SetScript(string? mode)
    {
        ScriptMode = mode switch
        {
            "1" => LyricsScript.Furigana,
            "2" => LyricsScript.Romaji,
            _ => LyricsScript.Original
        };
    }

    /// <summary>Set by the shell when the panel is shown/hidden. Lyrics only load while the panel is open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value) return;
            _isOpen = value;
            if (value) OnTrackChanged();
        }
    }

    [RelayCommand]
    private void SeekToLine(LyricsLineViewModel? line)
    {
        if (line?.TimeMs is int ms) _coordinator.Seek(TimeSpan.FromMilliseconds(ms));
    }

    private Track? CurrentTrack => _queue.Current ?? _playback.Current;

    private void OnTrackChanged()
    {
        if (!_isOpen) return;
        var track = CurrentTrack;
        if (track is null)
        {
            Reset("Nothing playing");
            _loadedTrackId = null;
            return;
        }

        if (track.Id == _loadedTrackId) return;
        _loadedTrackId = track.Id;
        _ = LoadAsync(track);
    }

    private async Task LoadAsync(Track track)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        Reset($"Searching lyrics for “{track.Title}”…");
        IsLoading = true;
        TrackTitle = track.Title;

        LyricsDocument doc;
        try
        {
            doc = await _lyrics.GetAsync(track, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            doc = LyricsDocument.Empty;
        }

        if (cts.IsCancellationRequested) return;

        OnUi(() =>
        {
            if (cts.IsCancellationRequested || track.Id != _loadedTrackId) return;
            IsLoading = false;
            PopulateLines(doc);
        });
    }

    private void PopulateLines(LyricsDocument doc)
    {
        Lines.Clear();
        _activeLineIndex = -1;
        ActiveIndex = -1;
        HasJapanese = false;

        if (!doc.HasLines)
        {
            HasLyrics = false;
            IsSynced = false;
            SourceText = null;
            StatusText = "No lyrics found for this track.";
            return;
        }

        var anyJapanese = false;
        foreach (var line in doc.Lines)
        {
            var vm = new LyricsLineViewModel(line.Text, line.TimeMs) { Script = ScriptMode };
            Lines.Add(vm);
            if (!anyJapanese && _japanese.ContainsJapanese(line.Text)) anyJapanese = true;
        }

        HasJapanese = anyJapanese;
        if (anyJapanese) _ = ConvertJapaneseAsync(_loadedTrackId);

        HasLyrics = true;
        IsSynced = doc.IsSynced;
        var source = string.IsNullOrEmpty(doc.SourceName) ? "web" : doc.SourceName;
        SourceText = (doc.IsSynced ? "Synced • from " : "from ") + source;
        StatusText = string.Empty;
        UpdateActiveLine();
    }

    private async Task ConvertJapaneseAsync(string? generationTrackId)
    {
        // Snapshot the lines for this track; bail out if the track changes mid-conversion.
        var snapshot = new List<LyricsLineViewModel>(Lines);
        foreach (var line in snapshot)
        {
            if (generationTrackId != _loadedTrackId) return;
            if (!_japanese.ContainsJapanese(line.OriginalText)) continue;

            var converted = await _japanese.ConvertLineAsync(line.OriginalText).ConfigureAwait(false);
            OnUi(() =>
            {
                if (generationTrackId != _loadedTrackId) return;
                line.RomajiText = converted.Romaji;
                line.FuriganaSegments = converted.Furigana;
            });
        }
    }

    private void UpdateActiveLine()
    {
        if (!_isOpen || !HasLyrics || Lines.Count == 0) return;

        int newIndex;
        if (IsSynced)
        {
            var posMs = _playback.Position.TotalMilliseconds;
            newIndex = 0;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].TimeMs is int t && t <= posMs) newIndex = i;
                else if (Lines[i].TimeMs is int t2 && t2 > posMs) break;
            }
        }
        else
        {
            // Plain lyrics: scroll in proportion to how far through the track we are.
            var total = _playback.Duration.TotalMilliseconds;
            if (total <= 0) return;
            var fraction = Math.Clamp(_playback.Position.TotalMilliseconds / total, 0, 1);
            newIndex = Math.Clamp((int)(fraction * Lines.Count), 0, Lines.Count - 1);
        }

        if (newIndex == _activeLineIndex) return;

        if (_activeLineIndex >= 0 && _activeLineIndex < Lines.Count)
            Lines[_activeLineIndex].IsActive = false;
        if (newIndex >= 0 && newIndex < Lines.Count)
            Lines[newIndex].IsActive = true;

        _activeLineIndex = newIndex;
        ActiveIndex = newIndex;
    }

    private void Reset(string status)
    {
        Lines.Clear();
        _activeLineIndex = -1;
        ActiveIndex = -1;
        HasLyrics = false;
        IsSynced = false;
        IsLoading = false;
        HasJapanese = false;
        SourceText = null;
        StatusText = status;
        TrackTitle = CurrentTrack?.Title ?? string.Empty;
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
