namespace Muselly.Core.Models;

/// <summary>
/// A saved remote Muselly server the client can connect to. The <see cref="PinnedFingerprint"/> is recorded
/// on first successful connection (trust-on-first-use) and verified on every subsequent connection so a
/// changed certificate is detected. Serialised to <c>remotes.json</c>.
/// </summary>
public sealed class RemoteServer
{
    /// <summary>Stable local id for this saved server (also used as the prefix of remote <c>muselly://</c> URIs).</summary>
    public required string Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public required string Host { get; set; }

    public required int Port { get; set; }

    /// <summary>Saved username (empty for guest connections).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Saved password. Optional; only present when the user opted to remember it.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Connect as a guest (no credentials).</summary>
    public bool Guest { get; set; }

    /// <summary>SHA-256 fingerprint pinned on first connect (hex, colon-separated).</summary>
    public string PinnedFingerprint { get; set; } = string.Empty;

    /// <summary>Reconnect automatically at startup.</summary>
    public bool AutoConnect { get; set; }
}
