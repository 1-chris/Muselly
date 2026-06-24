using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;
using Muselly.Server.Protocol;
using Muselly.Server.Transport;

namespace Muselly.Server.Client;

/// <summary>
/// Client-side <see cref="IRemoteServerManager"/>: owns the saved-server list (<c>remotes.json</c>) and the
/// live connections, merges each connected server's tracks into the shared <see cref="ILibraryService"/>, and
/// serves remote media (art/lyrics) and audio on demand. Fingerprints are pinned on first use and enforced
/// thereafter.
/// </summary>
public sealed class RemoteServerManager : IRemoteServerManager
{
    private readonly ILibraryService _library;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RemoteServerManager> _logger;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, MusellyServerConnection> _connections = new();
    private List<RemoteServer> _servers;

    public RemoteServerManager(ILibraryService library, ILoggerFactory loggerFactory)
    {
        _library = library;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RemoteServerManager>();
        _servers = Load();
    }

    public IReadOnlyList<RemoteServer> Servers
    {
        get { lock (_gate) return _servers.ToList(); }
    }

    public event EventHandler? ServersChanged;

    public bool IsConnected(string serverId) => _connections.TryGetValue(serverId, out var c) && c.IsConnected;

    public RemoteServer? Find(string serverId)
    {
        lock (_gate) return _servers.FirstOrDefault(s => s.Id == serverId);
    }

    public async Task<RemoteConnectResult> ConnectAsync(string host, int port, string? username, string? password,
        bool guest, bool remember, string? acceptFingerprint = null, CancellationToken cancellationToken = default)
    {
        RemoteServer record;
        lock (_gate)
        {
            record = _servers.FirstOrDefault(s =>
                         string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase) && s.Port == port)
                     ?? new RemoteServer { Id = Guid.NewGuid().ToString("N"), Host = host, Port = port };
        }

        var expected = acceptFingerprint ?? (string.IsNullOrEmpty(record.PinnedFingerprint) ? null : record.PinnedFingerprint);

        var connection = new MusellyServerConnection(_loggerFactory.CreateLogger<MusellyServerConnection>());
        try
        {
            await connection.ConnectAsync(host, port, expected, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            connection.Dispose();
            // A handshake failure when a fingerprint was pinned almost always means the cert changed.
            if (!string.IsNullOrEmpty(record.PinnedFingerprint) && !string.IsNullOrEmpty(connection.Fingerprint)
                && !Fingerprint.Equal(connection.Fingerprint, record.PinnedFingerprint))
            {
                return new RemoteConnectResult
                {
                    Success = false,
                    FingerprintMismatch = true,
                    Fingerprint = connection.Fingerprint,
                    Server = record,
                    Error = "The server's certificate fingerprint has changed."
                };
            }
            _logger.LogDebug(ex, "Connect to {Host}:{Port} failed.", host, port);
            return RemoteConnectResult.Fail(ex.Message);
        }

        try
        {
            await connection.RequestAsync<HelloResponse>(MessageType.Hello, new HelloRequest(), cancellationToken)
                .ConfigureAwait(false);

            var login = await connection.RequestAsync<LoginResponse>(MessageType.Login, new LoginRequest
            {
                Username = username ?? string.Empty,
                Password = password ?? string.Empty,
                Guest = guest
            }, cancellationToken).ConfigureAwait(false);

            if (login is null || !login.Success)
            {
                connection.Dispose();
                return RemoteConnectResult.Fail(login?.Error ?? "Login failed.");
            }

            var lib = await connection.RequestAsync<GetLibraryResponse>(MessageType.GetLibrary,
                new GetLibraryRequest(), cancellationToken).ConfigureAwait(false);

            var tracks = MapTracks(record.Id, lib?.Tracks ?? new List<RemoteTrackDto>());
            _library.AddRemoteTracks(record.Id, tracks);

            // Persist the (now connected) server with the pinned fingerprint and remembered credentials.
            record.PinnedFingerprint = connection.Fingerprint;
            record.Guest = guest;
            record.Username = guest ? string.Empty : (username ?? string.Empty);
            record.Password = (!guest && remember) ? (password ?? string.Empty) : string.Empty;
            if (string.IsNullOrEmpty(record.DisplayName)) record.DisplayName = host;
            Upsert(record);

            _connections[record.Id] = connection;
            ServersChanged?.Invoke(this, EventArgs.Empty);

            return new RemoteConnectResult
            {
                Success = true,
                Server = record,
                Fingerprint = connection.Fingerprint,
                Role = login.Role
            };
        }
        catch (Exception ex)
        {
            connection.Dispose();
            _logger.LogDebug(ex, "Handshake/login to {Host}:{Port} failed.", host, port);
            return RemoteConnectResult.Fail(ex.Message);
        }
    }

    public Task<RemoteConnectResult> ReconnectAsync(string serverId, string? acceptFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        var s = Find(serverId);
        if (s is null) return Task.FromResult(RemoteConnectResult.Fail("Unknown server."));
        return ConnectAsync(s.Host, s.Port, s.Username, s.Password, s.Guest, remember: !string.IsNullOrEmpty(s.Password),
            acceptFingerprint ?? s.PinnedFingerprint, cancellationToken);
    }

    public Task DisconnectAsync(string serverId)
    {
        if (_connections.TryRemove(serverId, out var conn))
        {
            conn.Dispose();
            _library.RemoveRemoteSource(serverId);
            ServersChanged?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    public async Task ForgetAsync(string serverId)
    {
        await DisconnectAsync(serverId).ConfigureAwait(false);
        lock (_gate)
        {
            _servers.RemoveAll(s => s.Id == serverId);
            Save();
        }
        ServersChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<byte[]?> GetResourceAsync(string remoteUri, CancellationToken cancellationToken = default)
    {
        if (!RemoteSource.TryParse(remoteUri, out var serverId, out var kind, out var key)) return null;

        var cachePath = ResourceCachePath(serverId, kind, key);
        if (File.Exists(cachePath))
        {
            try { return await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false); }
            catch { /* fall through to refetch */ }
        }

        if (!_connections.TryGetValue(serverId, out var conn) || !conn.IsConnected) return null;

        var type = kind == RemoteSource.KindArtistImage ? MessageType.GetArtistImage : MessageType.GetAlbumArt;
        using var ms = new MemoryStream();
        try
        {
            var len = await conn.RequestStreamAsync(type, new KeyRequest { Key = key }, ms, cancellationToken)
                .ConfigureAwait(false);
            if (len <= 0) return null;
            var bytes = ms.ToArray();
            try { await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken).ConfigureAwait(false); }
            catch { /* best-effort cache */ }
            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch remote resource {Uri}.", remoteUri);
            return null;
        }
    }

    public async Task<string?> GetLyricsAsync(string serverId, string trackId, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverId, out var conn) || !conn.IsConnected) return null;
        try
        {
            var resp = await conn.RequestAsync<TextResponse>(MessageType.GetLyrics, new KeyRequest { Key = trackId },
                cancellationToken).ConfigureAwait(false);
            return resp?.Text;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Remote lyrics fetch failed."); return null; }
    }

    public async Task<string?> GetArtistBioAsync(string serverId, string artistKey, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverId, out var conn) || !conn.IsConnected) return null;
        try
        {
            var resp = await conn.RequestAsync<TextResponse>(MessageType.GetArtistBio, new KeyRequest { Key = artistKey },
                cancellationToken).ConfigureAwait(false);
            return resp?.Text;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Remote bio fetch failed."); return null; }
    }

    public async Task<string?> DownloadTrackAsync(string serverId, string trackId, string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverId, out var conn) || !conn.IsConnected) return null;
        try
        {
            await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var len = await conn.RequestStreamAsync(MessageType.StreamTrack,
                new StreamTrackRequest { TrackId = trackId, BitrateKbps = 0 }, fs, cancellationToken).ConfigureAwait(false);
            return len > 0 ? destinationPath : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Remote track download failed.");
            return null;
        }
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static List<Track> MapTracks(string serverId, List<RemoteTrackDto> dtos)
    {
        var list = new List<Track>(dtos.Count);
        foreach (var d in dtos)
        {
            list.Add(new Track
            {
                // Namespace the id so it never collides with a local or other-server track of the same hash.
                Id = serverId + "_" + d.Id,
                Source = RemoteSource.Build(serverId, RemoteSource.KindTrack, d.Id),
                Title = d.Title,
                Artist = d.Artist,
                AlbumArtist = d.AlbumArtist,
                Album = d.Album,
                Composer = d.Composer,
                Genres = d.Genres,
                Label = d.Label,
                Year = d.Year,
                TrackNumber = d.TrackNumber,
                TrackCount = d.TrackCount,
                DiscNumber = d.DiscNumber,
                DiscCount = d.DiscCount,
                Duration = TimeSpan.FromSeconds(d.DurationSeconds),
                Bitrate = d.Bitrate,
                SampleRate = d.SampleRate,
                Channels = d.Channels,
                Codec = d.Codec,
                Extension = d.Extension,
                FileSizeBytes = d.FileSizeBytes,
                ArtworkPath = d.HasArtwork ? RemoteSource.Build(serverId, RemoteSource.KindAlbumArt, d.AlbumKey) : null,
                AlbumKey = d.AlbumKey,
                ArtistKey = d.ArtistKey,
                Directory = string.Empty // folders are never shared
            });
        }
        return list;
    }

    private static string ResourceCachePath(string serverId, string kind, string key)
    {
        var safe = Identifiers.Hash(serverId + "/" + kind + "/" + key);
        return Path.Combine(StoragePaths.RemoteCacheDirectory(), $"{kind}_{safe}.bin");
    }

    private void Upsert(RemoteServer server)
    {
        lock (_gate)
        {
            var idx = _servers.FindIndex(s => s.Id == server.Id);
            if (idx >= 0) _servers[idx] = server;
            else _servers.Add(server);
            Save();
        }
    }

    private void Save() => JsonStore.Save(StoragePaths.RemotesFile(), new RemotesFile { Servers = _servers });

    private static List<RemoteServer> Load()
    {
        var file = JsonStore.Load(StoragePaths.RemotesFile(), () => new RemotesFile());
        return file.Servers ?? new List<RemoteServer>();
    }

    private sealed class RemotesFile
    {
        public int Version { get; set; } = 1;
        public List<RemoteServer> Servers { get; set; } = new();
    }
}
