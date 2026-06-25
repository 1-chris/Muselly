using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;
using Muselly.Server.Api;
using Muselly.Server.Discovery;
using Muselly.Server.Transport;

namespace Muselly.WebHost;

/// <summary>
/// The ASP.NET Core (Kestrel) web server. Serves the browser app as static files and a JSON API mapped onto
/// the shared <see cref="MusellyApiService"/>, honouring the user roles and the web-scope firewall. HTTP and
/// HTTPS ports come from the shared <see cref="ServerSettings"/>; HTTPS reuses the server's self-signed
/// certificate. Has no UI dependency, so the desktop app and the headless host run it identically.
/// </summary>
public sealed class MusellyWebServer : IWebServerHost
{
    private readonly MusellyApiService _api;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MusellyWebServer> _logger;
    private readonly PortMapper _portMapper;
    private readonly object _gate = new();

    private WebApplication? _app;

    public MusellyWebServer(MusellyApiService api, ILoggerFactory loggerFactory)
    {
        _api = api;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MusellyWebServer>();
        _portMapper = new PortMapper(loggerFactory.CreateLogger<PortMapper>());
    }

    public bool IsRunning { get; private set; }
    public int HttpPort { get; private set; }
    public int HttpsPort { get; private set; }
    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsRunning) return;
        }

        var config = _api.Engine.Config;
        var httpPort = config.WebHttpPort;
        var httpsPort = config.WebHttpsPort;
        LastError = null;

        X509Certificate2? certificate = null;
        if (httpsPort > 0)
        {
            try { certificate = CertificateManager.LoadOrCreate(StoragePaths.ServerCertificateFile()); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not load HTTPS certificate; HTTPS disabled."); }
        }

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_loggerFactory);
        builder.Services.AddSingleton(_api);

        builder.WebHost.ConfigureKestrel(options =>
        {
            if (httpPort > 0)
                options.ListenAnyIP(httpPort);
            if (httpsPort > 0 && certificate is not null)
                options.ListenAnyIP(httpsPort, listen => listen.UseHttps(certificate));
        });

        var app = builder.Build();

        // Firewall (web scope): refuse blocked clients before anything else runs.
        app.Use(async (ctx, next) =>
        {
            if (!IpFilter.IsAllowed(ctx.Connection.RemoteIpAddress, _api.Engine.Config.Firewall, FirewallScope.Web))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });

        ServeStaticApp(app);
        WebApi.Map(app, _api);

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogError(ex, "Web server failed to start.");
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            RaiseStateChanged();
            throw;
        }

        _app = app;
        IsRunning = true;
        HttpPort = httpPort;
        HttpsPort = certificate is not null ? httpsPort : 0;

        // Best-effort router port forwarding for the web ports (HTTP + HTTPS).
        if (config.WebUpnpEnabled) _portMapper.Start(HttpPort, HttpsPort);

        _api.Engine.Log($"Web server started (http:{HttpPort} https:{HttpsPort})");
        _logger.LogInformation("Muselly web server started (http:{Http} https:{Https}).", HttpPort, HttpsPort);
        RaiseStateChanged();
    }

    public async Task StopAsync()
    {
        WebApplication? app;
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
            app = _app;
            _app = null;
        }

        _portMapper.Stop();

        if (app is not null)
        {
            try { await app.StopAsync().ConfigureAwait(false); } catch { /* ignore */ }
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }

        HttpPort = 0;
        HttpsPort = 0;
        _api.Engine.Log("Web server stopped");
        _logger.LogInformation("Muselly web server stopped.");
        RaiseStateChanged();
    }

    private void ServeStaticApp(WebApplication app)
    {
        var root = WebAppRoot.Resolve();
        if (root is null)
        {
            _logger.LogWarning("No browser app bundle found; serving API only. Set {Env} to the AppBundle path.",
                WebAppRoot.EnvironmentVariable);
            app.MapGet("/", () => Results.Text(
                "Muselly web server is running, but the browser app bundle was not found. " +
                "Publish Muselly.Web and point MUSELLY_WEBROOT at its AppBundle.", "text/plain"));
            return;
        }

        var provider = new PhysicalFileProvider(root);
        var contentTypes = new FileExtensionContentTypeProvider();
        // WASM bundles include files Kestrel doesn't know (e.g. .dat, .blat); serve them as binary.
        var staticOptions = new StaticFileOptions
        {
            FileProvider = provider,
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream"
        };

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(staticOptions);

        // SPA fallback: any non-API path serves index.html so client routing works.
        app.MapFallback(async ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            ctx.Response.ContentType = "text/html";
            await ctx.Response.SendFileAsync(provider.GetFileInfo("index.html"));
        });
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
