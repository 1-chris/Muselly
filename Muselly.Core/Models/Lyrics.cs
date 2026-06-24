namespace Muselly.Core.Models;

/// <summary>A single line of lyrics, with an optional start time (milliseconds) for synced lyrics.</summary>
public sealed class LyricsLine
{
    public required string Text { get; init; }

    /// <summary>Start time in milliseconds for synced lyrics, or null for plain (unsynced) lyrics.</summary>
    public int? TimeMs { get; init; }
}

/// <summary>Where a set of lyrics came from.</summary>
public enum LyricsOrigin
{
    None,
    EmbeddedSynced,
    EmbeddedPlain,
    WebSynced,
    WebPlain
}

/// <summary>
/// A resolved set of lyrics for a track. <see cref="IsSynced"/> indicates the lines carry timestamps and
/// can be highlighted exactly; otherwise the UI auto-scrolls them in proportion to playback progress.
/// </summary>
public sealed class LyricsDocument
{
    public required IReadOnlyList<LyricsLine> Lines { get; init; }

    public LyricsOrigin Origin { get; init; }

    /// <summary>Human-readable source, e.g. "file", "lrclib.net", "NetEase".</summary>
    public string? SourceName { get; init; }

    public bool IsSynced => Origin is LyricsOrigin.EmbeddedSynced or LyricsOrigin.WebSynced;

    public bool HasLines => Lines.Count > 0;

    public static readonly LyricsDocument Empty = new() { Lines = Array.Empty<LyricsLine>(), Origin = LyricsOrigin.None };
}
