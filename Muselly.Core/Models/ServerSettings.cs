namespace Muselly.Core.Models;

/// <summary>
/// Persisted configuration for the built-in server (<c>server.json</c>). The server is opt-in
/// (<see cref="Enabled"/>); the chosen <see cref="Port"/> is random on first enable but then stable, and
/// all traffic is TLS 1.3 with the self-signed certificate identified by <see cref="CertificateFingerprint"/>.
/// </summary>
public sealed class ServerSettings
{
    /// <summary>Whether the built-in server should be started.</summary>
    public bool Enabled { get; set; }

    /// <summary>A friendly name advertised to clients (defaults to the machine name when empty).</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>The TCP port the server listens on. 0 means "pick a random free port on first start".</summary>
    public int Port { get; set; }

    /// <summary>SHA-256 fingerprint (hex, colon-separated) of the server certificate, shown to users to verify.</summary>
    public string CertificateFingerprint { get; set; } = string.Empty;

    /// <summary>Opus transcode bitrate in kbps. Clamped to the 64-256 range.</summary>
    public int OpusBitrateKbps { get; set; } = 128;

    /// <summary>Maximum size of the on-disk transcode cache, in bytes. Oldest entries are evicted first.</summary>
    public long TranscodeCacheMaxBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>When true, clients may connect without a username/password as a <see cref="UserRole.Guest"/>.</summary>
    public bool GuestEnabled { get; set; }

    /// <summary>When true, attempt to open the port on the router via UPnP/NAT-PMP.</summary>
    public bool UpnpEnabled { get; set; }

    /// <summary>Optional IP firewall applied to the built-in server and/or the web server.</summary>
    public FirewallSettings Firewall { get; set; } = new();

    /// <summary>Whether the HTTP/S web server (serving the browser app + API) should be started.</summary>
    public bool WebEnabled { get; set; }

    /// <summary>HTTP port for the web server. 0 disables plain HTTP.</summary>
    public int WebHttpPort { get; set; } = 5180;

    /// <summary>HTTPS port for the web server. 0 disables HTTPS.</summary>
    public int WebHttpsPort { get; set; } = 5181;

    /// <summary>When true, try to open the web server's HTTP/HTTPS ports on the router via UPnP/NAT-PMP.</summary>
    public bool WebUpnpEnabled { get; set; }

    /// <summary>
    /// Optional public base URL for the web server (e.g. <c>https://music.example.com:1234/</c>). When set,
    /// share links are built from it instead of the detected LAN IP + port. Empty = derive automatically.
    /// </summary>
    public string WebExternalUrl { get; set; } = string.Empty;
}
