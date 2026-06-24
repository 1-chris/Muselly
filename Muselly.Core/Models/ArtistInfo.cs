namespace Muselly.Core.Models;

/// <summary>
/// Web-sourced metadata for an artist (biography + image), kept separate from the scanned library because
/// artists are projections of tracks. Persisted to <c>artist-info.json</c> keyed by artist key.
/// </summary>
public sealed class ArtistInfo
{
    public required string ArtistKey { get; set; }

    /// <summary>Plain-text biography (typically the lead section of a Wikipedia article).</summary>
    public string? Biography { get; set; }

    /// <summary>Where the biography came from, e.g. "Wikipedia".</summary>
    public string? BiographySource { get; set; }

    /// <summary>Canonical URL to read more (e.g. the Wikipedia page).</summary>
    public string? BiographyUrl { get; set; }

    /// <summary>Absolute path to the cached artist image, if one was found.</summary>
    public string? ImagePath { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>True when a fetch ran but found nothing — avoids hammering the APIs on every visit.</summary>
    public bool NotFound { get; set; }

    public bool HasBiography => !string.IsNullOrWhiteSpace(Biography);
}
