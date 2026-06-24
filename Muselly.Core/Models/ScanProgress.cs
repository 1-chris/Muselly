namespace Muselly.Core.Models;

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
