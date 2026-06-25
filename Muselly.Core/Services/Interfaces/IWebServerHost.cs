namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// The optional HTTP/S web server that serves the browser app and a JSON API onto the same library the
/// built-in TLS server exposes. It honours the same accounts/roles and the IP firewall (web scope). The
/// concrete implementation lives in Muselly.WebHost (ASP.NET Core); heads that can't host (e.g. the browser)
/// use a no-op stub. Reads its ports / enabled flag from the shared server settings.
/// </summary>
public interface IWebServerHost
{
    bool IsRunning { get; }

    /// <summary>The HTTP port actually bound (0 when not listening on HTTP).</summary>
    int HttpPort { get; }

    /// <summary>The HTTPS port actually bound (0 when not listening on HTTPS).</summary>
    int HttpsPort { get; }

    /// <summary>The last start error, if the server failed to come up.</summary>
    string? LastError { get; }

    /// <summary>Raised when the running state changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Starts the server using the configured ports. No-op if already running.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
