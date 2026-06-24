using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>The outcome of a connection attempt to a remote server.</summary>
public sealed class RemoteConnectResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>The saved-server record (present on success, or on a fingerprint-mismatch needing confirmation).</summary>
    public RemoteServer? Server { get; init; }

    /// <summary>The fingerprint presented by the server (for first-use verification / mismatch display).</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>True when the server presented a fingerprint that does not match the pinned one.</summary>
    public bool FingerprintMismatch { get; init; }

    /// <summary>The role granted to this client by the server.</summary>
    public UserRole Role { get; init; }

    public static RemoteConnectResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Manages connections to remote Muselly servers from the client side: saving servers, connecting/
/// disconnecting (with fingerprint pinning), merging their libraries into <see cref="ILibraryService"/>, and
/// fetching remote media (art, artist images, lyrics) and audio on demand. Implemented in Muselly.Server.
/// </summary>
public interface IRemoteServerManager
{
    IReadOnlyList<RemoteServer> Servers { get; }

    event EventHandler? ServersChanged;

    /// <summary>True if a live session exists for the saved server id.</summary>
    bool IsConnected(string serverId);

    RemoteServer? Find(string serverId);

    /// <summary>
    /// Connects to a server, logging in (or as guest), pinning/verifying the fingerprint, and merging its
    /// library. When <paramref name="acceptFingerprint"/> is non-null it is trusted (first-use or rotation).
    /// </summary>
    Task<RemoteConnectResult> ConnectAsync(string host, int port, string? username, string? password,
        bool guest, bool remember, string? acceptFingerprint = null, CancellationToken cancellationToken = default);

    /// <summary>Reconnects a previously saved server.</summary>
    Task<RemoteConnectResult> ReconnectAsync(string serverId, string? acceptFingerprint = null,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(string serverId);

    /// <summary>Removes a saved server (disconnecting first) and its merged library content.</summary>
    Task ForgetAsync(string serverId);

    /// <summary>Fetches a remote resource (album art / artist image) by its <c>muselly://</c> URI.</summary>
    Task<byte[]?> GetResourceAsync(string remoteUri, CancellationToken cancellationToken = default);

    /// <summary>Fetches lyrics text (LRC or plain) for a remote track id on the given server.</summary>
    Task<string?> GetLyricsAsync(string serverId, string trackId, CancellationToken cancellationToken = default);

    /// <summary>Fetches the artist biography text for a remote artist key on the given server.</summary>
    Task<string?> GetArtistBioAsync(string serverId, string artistKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a track's transcoded Opus to <paramref name="destinationPath"/> and returns it (or null on
    /// failure). Used by the remote audio-source resolver before decoding for playback.
    /// </summary>
    Task<string?> DownloadTrackAsync(string serverId, string trackId, string destinationPath,
        CancellationToken cancellationToken = default);
}
