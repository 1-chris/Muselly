namespace Muselly.Core.Models;

/// <summary>How playback advances at the end of the queue.</summary>
public enum RepeatMode
{
    /// <summary>Stop after the last track.</summary>
    Off,

    /// <summary>Loop the whole queue.</summary>
    All,

    /// <summary>Repeat the current track.</summary>
    One
}

/// <summary>Fields a list of tracks (in a playlist or library view) can be sorted by.</summary>
public enum TrackSortField
{
    /// <summary>The user-defined / playlist order.</summary>
    Custom,
    Title,
    Artist,
    Album,
    AlbumArtist,
    Genre,
    Year,
    Duration,
    TrackNumber,
    DateAdded
}

public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>Top-level navigation destinations in the shell.</summary>
public enum NavSection
{
    Albums,
    Artists,
    Songs,
    Folders,
    Playlists,
    Settings
}
