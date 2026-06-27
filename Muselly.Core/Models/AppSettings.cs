namespace Muselly.Core.Models;

/// <summary>
/// The persisted user settings. Serialised to <c>settings.json</c> in the per-user config directory.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Absolute paths of the directories scanned for music.</summary>
    public List<string> MusicFolders { get; set; } = new();

    /// <summary>Recurse into sub-directories when scanning.</summary>
    public bool ScanSubdirectories { get; set; } = true;

    /// <summary>Watch scanned folders for changes and rescan automatically.</summary>
    public bool WatchFolders { get; set; } = true;

    /// <summary>Collapse multi-artist compilation albums (detected by the file's compilation tag, or by a
    /// shared album title in one folder with differing artists) into a single "Various Artists" album.</summary>
    public bool MergeCompilationAlbums { get; set; } = true;

    /// <summary>Hide duplicate copies of the same song (same title/artist and near-equal duration) from the
    /// Songs, Albums and Artists views, keeping only the highest-quality copy. Files on disk and the Folder
    /// view are left untouched.</summary>
    public bool DeduplicateTracks { get; set; } = true;

    public string ThemeName { get; set; } = "Catppuccin Mocha";

    public double FontScale { get; set; } = 1.0;

    /// <summary>Output volume, 0.0–1.0.</summary>
    public double Volume { get; set; } = 0.8;

    public bool Muted { get; set; }

    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;

    public bool ShuffleEnabled { get; set; }

    public NavSection LastSection { get; set; } = NavSection.Albums;

    /// <summary>Automatically fetch artist biographies/images from the web when an artist page is opened.</summary>
    public bool AutomaticArtistBiography { get; set; } = true;

    /// <summary>Base font size (in px) for the lyrics panel.</summary>
    public double LyricsFontSize { get; set; } = 15;

    public EqualizerSettings Equalizer { get; set; } = new();
}

/// <summary>Persisted 10-band graphic EQ state.</summary>
public sealed class EqualizerSettings
{
    public bool Enabled { get; set; }

    public double PreampDb { get; set; }

    /// <summary>Per-band gain in dB (index matches the equaliser's band order). Empty = flat.</summary>
    public List<double> BandGainsDb { get; set; } = new();
}
