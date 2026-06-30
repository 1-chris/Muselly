namespace Muselly.Core.Models;

/// <summary>A music-scrobbling service Muselly can submit listens to.</summary>
public enum ScrobbleProvider
{
    LastFm = 0,
    ListenBrainz = 1,
    LibreFm = 2
}

/// <summary>
/// A user's connection to one scrobbling service. Stores only the long-lived credential needed to submit
/// listens — a session key (Last.fm / Libre.fm) or a user token (ListenBrainz) — never the password.
/// Persisted per user in <c>scrobble-accounts.json</c>.
/// </summary>
public sealed class ScrobbleAccount
{
    public ScrobbleProvider Provider { get; set; }

    /// <summary>Whether listens are submitted to this service.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The service-side account name (for display), if known.</summary>
    public string? AccountName { get; set; }

    /// <summary>Last.fm/Libre.fm session key, or the ListenBrainz user token.</summary>
    public string Credential { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>The outcome of attempting to connect a scrobbling account.</summary>
public sealed class ScrobbleConnectResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? AccountName { get; init; }

    public static ScrobbleConnectResult Ok(string? account) => new() { Success = true, AccountName = account };
    public static ScrobbleConnectResult Fail(string error) => new() { Success = false, Error = error };
}
