using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;
using Muselly.Server.Discovery;
using Muselly.Server.Transport;

namespace Muselly.Server.ServerHost;

/// <summary>
/// The integrated TLS server. Listens on a stable TCP port, applies the IP firewall, wraps every accepted
/// connection in TLS with the self-signed certificate, and hands each off to a <see cref="ServerSession"/>.
/// All shared state and operations live in the transport-agnostic <see cref="ServerEngine"/>; this type is
/// just the framed-protocol transport in front of it.
/// </summary>
public sealed class MusellyServerHost : IServerHost
{
    private readonly ServerEngine _engine;
    private readonly ILogger<MusellyServerHost> _logger;
    private readonly PortMapper _portMapper;
    private readonly object _gate = new();

    private X509Certificate2? _certificate;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _clientCount;

    public MusellyServerHost(ServerEngine engine, ILoggerFactory loggerFactory)
    {
        _engine = engine;
        _logger = loggerFactory.CreateLogger<MusellyServerHost>();
        _portMapper = new PortMapper(loggerFactory.CreateLogger<PortMapper>());

        _engine.SettingsChanged += (_, _) => RaiseStateChanged();
    }

    public bool IsRunning { get; private set; }
    public int Port => IsRunning ? _engine.Config.Port : 0;
    public string Fingerprint { get; private set; } = string.Empty;
    public int ConnectedClients => Volatile.Read(ref _clientCount);
    public ServerSettings Settings => _engine.Config;
    public IServerUserStore Users => _engine.Users;
    public IReadOnlyList<ServerLogEntry> RecentLogs => _engine.RecentLogs;

    public event EventHandler? StateChanged;
    public event EventHandler<ServerLogEntry>? Logged
    {
        add => _engine.Logged += value;
        remove => _engine.Logged -= value;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsRunning) return;
        }

        var config = _engine.Config;
        _certificate = CertificateManager.LoadOrCreate(Core.Storage.StoragePaths.ServerCertificateFile());
        Fingerprint = Transport.Fingerprint.Of(_certificate);

        // Stable port: keep the configured one, else choose a free port now and persist it.
        if (config.Port == 0) config.Port = FindFreePort();
        if (config.CertificateFingerprint != Fingerprint) config.CertificateFingerprint = Fingerprint;
        _engine.Persist();

        var listener = new TcpListener(IPAddress.Any, config.Port);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start server listener on port {Port}.", config.Port);
            throw;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = listener;
        IsRunning = true;

        if (config.UpnpEnabled) _portMapper.Start(config.Port);

        _logger.LogInformation("Muselly server listening on port {Port} (fingerprint {Fp}).", config.Port, Fingerprint);
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
        var remoteAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        var remote = remoteAddress?.ToString() ?? "unknown";

        // Firewall: silently drop connections the policy refuses (don't even complete the TLS handshake).
        if (!IpFilter.IsAllowed(remoteAddress, _engine.Config.Firewall, FirewallScope.Server))
        {
            try { client.Dispose(); } catch { /* ignore */ }
            _engine.Log($"Firewall blocked connection ({remote})");
            return;
        }

        Interlocked.Increment(ref _clientCount);
        RaiseStateChanged();
        _engine.Log($"Client connected ({remote})");
        try
        {
            client.NoDelay = true;
            var ssl = await TlsTransport.AuthenticateServerAsync(client.GetStream(), _certificate!, ct)
                .ConfigureAwait(false);
            var session = new ServerSession(ssl, _engine.BuildContext(), ct);
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
            _engine.Log($"Client disconnected ({remote})");
        }
    }

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
        var config = _engine.Config;
        var wasRunning = IsRunning;
        var oldPort = config.Port;
        var oldUpnp = config.UpnpEnabled;

        _engine.UpdateConfig(mutate);

        // A port change requires a listener restart; a UPnP toggle just (un)maps.
        if (wasRunning && config.Port != oldPort)
        {
            await StopAsync();
            await StartAsync();
        }
        else if (wasRunning && config.UpnpEnabled != oldUpnp)
        {
            if (config.UpnpEnabled) _portMapper.Start(config.Port);
            else _portMapper.Stop();
        }

        RaiseStateChanged();
    }

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
