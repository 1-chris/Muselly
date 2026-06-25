using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// The built-in, integrated server. Hosts the local library over the bespoke TLS 1.3 protocol, transcodes
/// on demand to Opus, and enforces user roles. Designed so a future headless host can drive the same engine
/// with only a Core library + settings. Implemented in Muselly.Server; a no-op stub is used where hosting is
/// unavailable (e.g. the browser head).
/// </summary>
public interface IServerHost
{
    bool IsRunning { get; }

    /// <summary>The port currently listened on (0 when stopped).</summary>
    int Port { get; }

    /// <summary>SHA-256 certificate fingerprint clients verify (empty when no certificate yet).</summary>
    string Fingerprint { get; }

    /// <summary>Number of currently connected client sessions.</summary>
    int ConnectedClients { get; }

    /// <summary>The persisted server configuration (bitrate, cache size, guest/UPnP toggles, port).</summary>
    ServerSettings Settings { get; }

    IServerUserStore Users { get; }

    /// <summary>A snapshot of the most recent server activity (connections, requests, commands).</summary>
    IReadOnlyList<ServerLogEntry> RecentLogs { get; }

    /// <summary>Raised when running state, client count or settings change.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised (possibly off the UI thread) whenever a new activity log entry is recorded.</summary>
    event EventHandler<ServerLogEntry>? Logged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>Mutates and persists <see cref="Settings"/>. Restarts the listener if needed.</summary>
    Task UpdateSettingsAsync(Action<ServerSettings> mutate);
}
