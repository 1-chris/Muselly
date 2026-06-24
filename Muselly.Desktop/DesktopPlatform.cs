using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Muselly.App.Platform;
using Muselly.App.ViewModels;
using Muselly.App.Views.Windows;
using Muselly.Audio;
using Muselly.Audio.Decoding;
using Muselly.Core.Services.Interfaces;
using Muselly.Server.DependencyInjection;
using Muselly.Server.Transcoding;

namespace Muselly.Desktop;

/// <summary>
/// Desktop host integration. Registers the dependency-free native audio backend (ffmpeg-decoded PCM played
/// through CoreAudio / WASAPI / ALSA), overriding the portable silent default, and shows the custom-chrome
/// <see cref="MainWindow"/>. If no native device opens, the backend itself degrades to silent output, so the
/// app always runs.
/// </summary>
public sealed class DesktopPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        // Surface logs to the console so audio/scan diagnostics are visible when run from a terminal.
        services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));

        // Prefer an ffmpeg binary shipped next to the app; otherwise fall back to one on the PATH.
        UseBundledFfmpegIfPresent();

        // Integrated server + remote-client services. Registered after the core defaults so the remote-aware
        // audio source resolver replaces the local-only one (last registration wins).
        services.AddMusellyServer();

        services.AddSingleton<IPlaybackService>(sp =>
            new FfmpegPlaybackService(
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<FfmpegPlaybackService>(),
                sp.GetRequiredService<Muselly.Core.Audio.IAudioEqualizer>(),
                sp.GetRequiredService<IAudioSourceResolver>()));
    }

    private static void UseBundledFfmpegIfPresent()
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var bundled = Path.Combine(AppContext.BaseDirectory, exe);
        if (!File.Exists(bundled)) return;

        // The build copy can drop the executable bit on Unix; restore it so we can launch the binary.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var mode = File.GetUnixFileMode(bundled);
                File.SetUnixFileMode(bundled, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch { /* best-effort */ }
        }

        FfmpegDecoder.FfmpegPath = bundled;
        OpusTranscoder.FfmpegPath = bundled;
    }

    public object CreateShell(IServiceProvider services) => new MainWindow
    {
        DataContext = services.GetRequiredService<MainViewModel>()
    };

    public void OnStarted(IServiceProvider services)
    {
        // Kick off startup work (load cache, restore theme, initial scan) without blocking the UI thread.
        _ = services.GetRequiredService<MainViewModel>().InitializeAsync();

        // Start the integrated server if it was left enabled, then reconnect any auto-connect remotes. This
        // runs after the library has had a chance to load so the server has content to serve.
        _ = StartServerModeAsync(services);
    }

    private static async Task StartServerModeAsync(IServiceProvider services)
    {
        try
        {
            var host = services.GetRequiredService<IServerHost>();
            if (host.Settings.Enabled)
                await host.StartAsync();

            var remotes = services.GetRequiredService<IRemoteServerManager>();
            foreach (var server in remotes.Servers)
                if (server.AutoConnect)
                    _ = remotes.ReconnectAsync(server.Id);
        }
        catch
        {
            // Server mode is optional; never let it block app startup.
        }
    }
}
