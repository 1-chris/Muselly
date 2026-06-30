using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Manages per-user scrobbling to Last.fm, ListenBrainz and Libre.fm: connecting/disconnecting accounts and
/// submitting "now playing" updates and completed scrobbles. Each user (including the desktop's built-in
/// user) has their own set of connected services.
/// </summary>
public interface IScrobbleService
{
    /// <summary>The connected accounts for a user (any provider not present is unconnected).</summary>
    IReadOnlyList<ScrobbleAccount> GetAccounts(string username);

    /// <summary>Raised when a user's connected accounts change.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Connects a service for a user. For ListenBrainz, <paramref name="secret1"/> is the user token (and
    /// <paramref name="secret2"/> is unused); for Last.fm/Libre.fm they are the service username and password
    /// (exchanged for a session key over HTTPS — the password is never stored).
    /// </summary>
    Task<ScrobbleConnectResult> ConnectAsync(string username, ScrobbleProvider provider,
        string secret1, string? secret2 = null, CancellationToken ct = default);

    void Disconnect(string username, ScrobbleProvider provider);

    void SetEnabled(string username, ScrobbleProvider provider, bool enabled);

    /// <summary>Whether a given provider can be configured in this build (Last.fm needs an API key).</summary>
    bool IsProviderAvailable(ScrobbleProvider provider);

    /// <summary>Sends a "now playing" update to all of the user's enabled services (best-effort).</summary>
    Task UpdateNowPlayingAsync(string username, Track track, CancellationToken ct = default);

    /// <summary>Submits a completed scrobble to all of the user's enabled services (best-effort, with retry).</summary>
    Task ScrobbleAsync(string username, Track track, DateTimeOffset startedAt, CancellationToken ct = default);

    Task LoadAsync();
}
