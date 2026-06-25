using Microsoft.Extensions.Logging;
using Mono.Nat;
using MapProtocol = Mono.Nat.Protocol;

namespace Muselly.Server.Discovery;

/// <summary>
/// Best-effort router port forwarding via UPnP IGD / NAT-PMP (using Mono.Nat). Enabled by a server toggle so
/// the server is reachable from outside the LAN. All failures are swallowed — port mapping is a convenience,
/// never a requirement.
/// </summary>
public sealed class PortMapper : IDisposable
{
    private readonly ILogger _logger;
    private readonly List<INatDevice> _devices = new();
    private readonly object _gate = new();
    private readonly List<int> _ports = new();
    private bool _running;

    public PortMapper(ILogger logger) => _logger = logger;

    /// <summary>Maps one or more TCP ports (e.g. the server port, or the web server's HTTP + HTTPS ports).</summary>
    public void Start(params int[] ports)
    {
        lock (_gate)
        {
            if (_running) Stop();
            _ports.Clear();
            foreach (var p in ports)
                if (p > 0 && !_ports.Contains(p)) _ports.Add(p);
            if (_ports.Count == 0) return;

            _running = true;
            NatUtility.DeviceFound += OnDeviceFound;
            try { NatUtility.StartDiscovery(NatProtocol.Pmp, NatProtocol.Upnp); }
            catch (Exception ex) { _logger.LogDebug(ex, "UPnP discovery failed to start."); }
        }
    }

    private async void OnDeviceFound(object? sender, DeviceEventArgs e)
    {
        int[] ports;
        lock (_gate)
        {
            if (!_running) return;
            if (!_devices.Contains(e.Device)) _devices.Add(e.Device);
            ports = _ports.ToArray();
        }
        foreach (var port in ports)
        {
            try
            {
                await e.Device.CreatePortMapAsync(new Mapping(MapProtocol.Tcp, port, port)).ConfigureAwait(false);
                _logger.LogInformation("Mapped external TCP port {Port} via {Device}.", port, e.Device.NatProtocol);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to map port {Port} on a discovered device.", port);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
            NatUtility.DeviceFound -= OnDeviceFound;
            try { NatUtility.StopDiscovery(); } catch { /* ignore */ }

            foreach (var device in _devices)
                foreach (var port in _ports)
                {
                    try { device.DeletePortMap(new Mapping(MapProtocol.Tcp, port, port)); }
                    catch { /* ignore */ }
                }
            _devices.Clear();
            _ports.Clear();
        }
    }

    public void Dispose() => Stop();
}
