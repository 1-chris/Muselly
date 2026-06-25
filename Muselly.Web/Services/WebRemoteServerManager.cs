using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;

namespace Muselly.Web.Services;

/// <summary>
/// Browser implementation of <see cref="IRemoteServerManager"/> that resolves <c>muselly://</c> resources
/// (album art, artist images, lyrics, biographies) by calling the same-origin web API. This lets the shared
/// app's <c>ArtworkCache</c> and lyrics/bio code work unchanged in the browser. Connection management is
/// not applicable here (the host is the page's own origin), so those members are inert.
/// </summary>
public sealed class WebRemoteServerManager : IRemoteServerManager
{
    private readonly WebHostClient _client;

    public WebRemoteServerManager(WebHostClient client) => _client = client;

    public IReadOnlyList<RemoteServer> Servers => Array.Empty<RemoteServer>();
    public event EventHandler? ServersChanged { add { } remove { } }

    public bool IsConnected(string serverId) => true;
    public RemoteServer? Find(string serverId) => null;

    public Task<RemoteConnectResult> ConnectAsync(string host, int port, string? username, string? password,
        bool guest, bool remember, string? acceptFingerprint = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(RemoteConnectResult.Fail("Not supported in the browser."));

    public Task<RemoteConnectResult> ReconnectAsync(string serverId, string? acceptFingerprint = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RemoteConnectResult.Fail("Not supported in the browser."));

    public Task DisconnectAsync(string serverId) => Task.CompletedTask;
    public Task ForgetAsync(string serverId) => Task.CompletedTask;
    public Task SetAutoConnectAsync(string serverId, bool autoConnect) => Task.CompletedTask;

    public Task<byte[]?> GetResourceAsync(string remoteUri, CancellationToken cancellationToken = default)
    {
        if (!RemoteSource.TryParse(remoteUri, out _, out var kind, out var key))
            return Task.FromResult<byte[]?>(null);

        var path = kind == RemoteSource.KindArtistImage
            ? "/api/artist-image?key=" + Uri.EscapeDataString(key)
            : "/api/album-art?key=" + Uri.EscapeDataString(key);
        return _client.GetBytesAsync(path, cancellationToken);
    }

    public Task<string?> GetLyricsAsync(string serverId, string trackId, CancellationToken cancellationToken = default) =>
        _client.GetTextAsync("/api/lyrics?id=" + Uri.EscapeDataString(trackId), cancellationToken);

    public Task<string?> GetArtistBioAsync(string serverId, string artistKey, CancellationToken cancellationToken = default) =>
        _client.GetTextAsync("/api/artist-bio?key=" + Uri.EscapeDataString(artistKey), cancellationToken);

    public Task<string?> DownloadTrackAsync(string serverId, string trackId, string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null); // playback streams via the HTML audio element instead
}
