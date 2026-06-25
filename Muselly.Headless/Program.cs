using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Muselly.Core.DependencyInjection;
using Muselly.Core.Services.Interfaces;
using Muselly.Server.DependencyInjection;
using Muselly.Server.Transcoding;
using Muselly.WebHost;
using Muselly.WebHost.DependencyInjection;

namespace Muselly.Headless;

/// <summary>
/// A UI-less Muselly host. It composes exactly the same engine the desktop uses (Core + Server + WebHost),
/// scans the configured library, and runs both the bespoke TLS server and the HTTP/S web server until
/// interrupted. Point <c>MUSELLY_WEBROOT</c> at a published Muselly.Web AppBundle to serve the browser app.
///
/// Usage:
///   muselly-headless [--add &lt;folder&gt;]... [--http &lt;port&gt;] [--https &lt;port&gt;] [--no-web] [--no-server]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = HeadlessOptions.Parse(args);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
        services.AddMusellyCore();
        services.AddMusellyServer();
        services.AddMusellyWebHost();

        await using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Muselly.Headless");

        UseFfmpegFromPathOrBundle();

        var settings = provider.GetRequiredService<ISettingsService>();
        foreach (var folder in options.FoldersToAdd) settings.AddMusicFolder(folder);

        var host = provider.GetRequiredService<IServerHost>();
        if (options.HttpPort is { } http) await host.UpdateSettingsAsync(s => s.WebHttpPort = http);
        if (options.HttpsPort is { } https) await host.UpdateSettingsAsync(s => s.WebHttpsPort = https);

        var library = provider.GetRequiredService<ILibraryService>();
        logger.LogInformation("Loading library…");
        await library.LoadAsync();
        if (library.Tracks.Count == 0 && settings.Current.MusicFolders.Count > 0)
        {
            logger.LogInformation("Scanning {Count} folder(s)…", settings.Current.MusicFolders.Count);
            await library.ScanAsync();
        }
        logger.LogInformation("Library ready: {Tracks} tracks.", library.Tracks.Count);

        if (!options.NoServer)
        {
            await host.StartAsync();
            logger.LogInformation("TLS server on port {Port}. Fingerprint: {Fp}", host.Port, host.Fingerprint);
        }

        if (!options.NoWeb)
        {
            var web = provider.GetRequiredService<IWebServerHost>();
            try
            {
                await web.StartAsync();
                logger.LogInformation("Web server on http:{Http} https:{Https}.", web.HttpPort, web.HttpsPort);
                if (WebAppRoot.Resolve() is null)
                    logger.LogWarning("No browser app bundle found; serving API only. Set {Env}.", WebAppRoot.EnvironmentVariable);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Web server failed to start.");
            }
        }

        logger.LogInformation("Muselly headless is running. Press Ctrl+C to stop.");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

        try { await Task.Delay(Timeout.Infinite, stop.Token); }
        catch (OperationCanceledException) { /* shutting down */ }

        logger.LogInformation("Stopping…");
        if (!options.NoServer) await host.StopAsync();
        if (!options.NoWeb) await provider.GetRequiredService<IWebServerHost>().StopAsync();
        return 0;
    }

    /// <summary>Prefer an ffmpeg next to the binary; otherwise rely on one on PATH (used for Opus transcoding).</summary>
    private static void UseFfmpegFromPathOrBundle()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var bundled = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(bundled)) OpusTranscoder.FfmpegPath = bundled;
    }
}

/// <summary>Parsed command-line options for the headless host.</summary>
internal sealed class HeadlessOptions
{
    public List<string> FoldersToAdd { get; } = new();
    public int? HttpPort { get; private set; }
    public int? HttpsPort { get; private set; }
    public bool NoWeb { get; private set; }
    public bool NoServer { get; private set; }

    public static HeadlessOptions Parse(string[] args)
    {
        var o = new HeadlessOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--add" when i + 1 < args.Length: o.FoldersToAdd.Add(args[++i]); break;
                case "--http" when i + 1 < args.Length && int.TryParse(args[i + 1], out var h): o.HttpPort = h; i++; break;
                case "--https" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s): o.HttpsPort = s; i++; break;
                case "--no-web": o.NoWeb = true; break;
                case "--no-server": o.NoServer = true; break;
            }
        }
        return o;
    }
}
