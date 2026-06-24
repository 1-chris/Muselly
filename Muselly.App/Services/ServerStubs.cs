using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.Services;

/// <summary>
/// No-op <see cref="IServerHost"/> for heads that cannot host (e.g. the browser sandbox). Registered as the
/// shared default so the Connect panel resolves everywhere; the desktop head overrides it with the real one.
/// </summary>
public sealed class NullServerHost : IServerHost
{
    public bool IsRunning => false;
    public int Port => 0;
    public string Fingerprint => string.Empty;
    public int ConnectedClients => 0;
    public ServerSettings Settings { get; } = new();
    public IServerUserStore Users { get; } = new NullServerUserStore();

    public event EventHandler? StateChanged { add { } remove { } }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task UpdateSettingsAsync(Action<ServerSettings> mutate) { mutate(Settings); return Task.CompletedTask; }
}

/// <summary>No-op user store backing <see cref="NullServerHost"/>.</summary>
public sealed class NullServerUserStore : IServerUserStore
{
    public IReadOnlyList<ServerUser> Users => Array.Empty<ServerUser>();
    public event EventHandler? Changed { add { } remove { } }
    public ServerUser? Validate(string username, string password) => null;
    public ServerUser? Find(string username) => null;
    public void AddOrUpdate(string username, string password, UserRole role) { }
    public void SetRole(string username, UserRole role) { }
    public void SetPassword(string username, string password) { }
    public void Remove(string username) { }
}

/// <summary>No-op <see cref="IRemoteServerManager"/> for heads without networking.</summary>
public sealed class NullRemoteServerManager : IRemoteServerManager
{
    public IReadOnlyList<RemoteServer> Servers => Array.Empty<RemoteServer>();
    public event EventHandler? ServersChanged { add { } remove { } }

    public bool IsConnected(string serverId) => false;
    public RemoteServer? Find(string serverId) => null;

    public Task<RemoteConnectResult> ConnectAsync(string host, int port, string? username, string? password,
        bool guest, bool remember, string? acceptFingerprint = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(RemoteConnectResult.Fail("Server connections are not available on this platform."));

    public Task<RemoteConnectResult> ReconnectAsync(string serverId, string? acceptFingerprint = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RemoteConnectResult.Fail("Server connections are not available on this platform."));

    public Task DisconnectAsync(string serverId) => Task.CompletedTask;
    public Task ForgetAsync(string serverId) => Task.CompletedTask;
    public Task<byte[]?> GetResourceAsync(string remoteUri, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);
    public Task<string?> GetLyricsAsync(string serverId, string trackId, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
    public Task<string?> GetArtistBioAsync(string serverId, string artistKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
    public Task<string?> DownloadTrackAsync(string serverId, string trackId, string destinationPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
