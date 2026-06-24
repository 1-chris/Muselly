using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;
using Muselly.Core.Storage;
using Muselly.Server.Auth;
using Muselly.Server.Discovery;
using Muselly.Server.Transcoding;
using Muselly.Server.Transport;

namespace Muselly.Server.ServerHost;

/// <summary>
/// The integrated server. Listens on a stable TCP port, wraps every connection in TLS 1.3 with the
/// self-signed certificate, and hands each off to a <see cref="ServerSession"/>. Reads its config from
/// <c>server.json</c> and is built purely from Core abstractions so a headless host can reuse it verbatim.
/// </summary>
public sealed class MusellyServerHost : IServerHost
{
    private readonly ILibraryService _library;
    private readonly ISettingsService _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MusellyServerHost> _logger;
    private readonly ILyricsService? _lyrics;
    private readonly IArtistInfoService? _artistInfo;

    private readonly UserStore _users;
    private readonly SessionManager _sessions = new();
    private readonly OpusTranscoder _transcoder = new();
    private readonly TranscodeCache _transcodeCache;
    private readonly PortMapper _portMapper;
    private readonly object _gate = new();

    private ServerSettings _config;
    private X509Certificate2? _certificate;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _clientCount;
    private string _libraryEtag = Guid.NewGuid().ToString("N");

    public MusellyServerHost(
        ILibraryService library,
        ISettingsService settings,
        ILoggerFactory loggerFactory,
        ILyricsService? lyrics = null,
        IArtistInfoService? artistInfo = null)
    {
        _library = library;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MusellyServerHost>();
        _lyrics = lyrics;
        _artistInfo = artistInfo;

        _config = JsonStore.Load(StoragePaths.ServerConfigFile(), () => new ServerSettings());
        _users = new UserStore();
        _transcodeCache = new TranscodeCache(StoragePaths.TranscodeCacheDirectory(), _transcoder,
            loggerFactory.CreateLogger<TranscodeCache>());
        _transcodeCache.MaxBytes = _config.TranscodeCacheMaxBytes;
        _portMapper = new PortMapper(loggerFactory.CreateLogger<PortMapper>());

        _library.LibraryChanged += (_, _) => _libraryEtag = Guid.NewGuid().ToString("N");
    }

    public bool IsRunning { get; private set; }
    public int Port => IsRunning ? _config.Port : 0;
    public string Fingerprint { get; private set; } = string.Empty;
    public int ConnectedClients => Volatile.Read(ref _clientCount);
    public ServerSettings Settings => _config;
    public IServerUserStore Users => _users;

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsRunning) return;
        }

        _certificate = CertificateManager.LoadOrCreate(StoragePaths.ServerCertificateFile());
        Fingerprint = Transport.Fingerprint.Of(_certificate);

        // Stable port: keep the configured one, else choose a free port now and persist it.
        if (_config.Port == 0)
            _config.Port = FindFreePort();
        if (_config.CertificateFingerprint != Fingerprint)
            _config.CertificateFingerprint = Fingerprint;
        Persist();

        var listener = new TcpListener(IPAddress.Any, _config.Port);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start server listener on port {Port}.", _config.Port);
            throw;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = listener;
        IsRunning = true;

        if (_config.UpnpEnabled)
            _portMapper.Start(_config.Port);

        _logger.LogInformation("Muselly server listening on port {Port} (fingerprint {Fp}).", _config.Port, Fingerprint);
        RaiseStateChanged();

        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        await Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener is null) return;

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Accept failed.");
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        Interlocked.Increment(ref _clientCount);
        RaiseStateChanged();
        try
        {
            client.NoDelay = true;
            var ssl = await TlsTransport.AuthenticateServerAsync(client.GetStream(), _certificate!, ct)
                .ConfigureAwait(false);
            var session = new ServerSession(ssl, BuildContext(), ct);
            await session.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Client handler failed.");
        }
        finally
        {
            try { client.Dispose(); } catch { /* ignore */ }
            Interlocked.Decrement(ref _clientCount);
            RaiseStateChanged();
        }
    }

    private ServerContext BuildContext() => new()
    {
        Logger = _logger,
        Library = _library,
        Settings = _settings,
        Users = _users,
        Sessions = _sessions,
        TranscodeCache = _transcodeCache,
        ServerSettings = () => _config,
        LibraryEtag = () => _libraryEtag,
        Lyrics = _lyrics,
        ArtistInfo = _artistInfo,
        Rescan = ct => _library.ScanAsync(ct),
        UpdateSettings = async mutate =>
        {
            mutate();
            _transcodeCache.MaxBytes = _config.TranscodeCacheMaxBytes;
            Persist();
            RaiseStateChanged();
            await Task.CompletedTask;
        }
    };

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
        }

        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _listener?.Stop(); } catch { /* ignore */ }
        _portMapper.Stop();
        _listener = null;
        _logger.LogInformation("Muselly server stopped.");
        RaiseStateChanged();
        await Task.CompletedTask;
    }

    public async Task UpdateSettingsAsync(Action<ServerSettings> mutate)
    {
        var wasRunning = IsRunning;
        var oldPort = _config.Port;
        var oldUpnp = _config.UpnpEnabled;

        mutate(_config);
        _config.OpusBitrateKbps = OpusTranscoder.ClampBitrate(_config.OpusBitrateKbps);
        _transcodeCache.MaxBytes = _config.TranscodeCacheMaxBytes;
        Persist();

        // A port change requires a listener restart; a UPnP toggle just (un)maps.
        if (wasRunning && _config.Port != oldPort)
        {
            await StopAsync();
            await StartAsync();
        }
        else if (wasRunning && _config.UpnpEnabled != oldUpnp)
        {
            if (_config.UpnpEnabled) _portMapper.Start(_config.Port);
            else _portMapper.Stop();
        }

        RaiseStateChanged();
    }

    private void Persist() => JsonStore.Save(StoragePaths.ServerConfigFile(), _config);

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
