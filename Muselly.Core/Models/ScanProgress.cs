namespace Muselly.Core.Models;

/// <summary>How a library scan treats files already in the library.</summary>
public enum ScanMode
{
    /// <summary>Re-read every file: add new tracks, refresh existing ones, and drop tracks whose files are
    /// gone. Previously-fetched album art is preserved.</summary>
    Full,

    /// <summary>Only read files not already in the library (add new songs); existing tracks are left as-is
    /// and nothing is removed. Fast.</summary>
    NewOnly
}

public enum ScanPhase
{
    Discovering,
    Reading,
    Organizing,
    Completed,
    Idle
}

/// <summary>Progress snapshot raised while the library scans, suitable for a progress bar + status line.</summary>
public sealed class ScanProgress
{
    public ScanPhase Phase { get; init; }

    public int Processed { get; init; }

    public int Total { get; init; }

    public string? CurrentItem { get; init; }

    public double Fraction => Total > 0 ? Math.Clamp((double)Processed / Total, 0, 1) : 0;
}
