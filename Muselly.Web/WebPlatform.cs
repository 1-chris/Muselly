using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Muselly.App.Platform;
using Muselly.App.ViewModels;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;
using Muselly.Web.Audio;
using Muselly.Web.Services;
using Muselly.Web.ViewModels;
using Muselly.Web.Views;

namespace Muselly.Web;

/// <summary>
/// Browser host integration. Unlike the old static demo, this head is a real client of a Muselly web host:
/// it talks to the same-origin API, streams Opus through an HTML audio element, and gates the shared UI
/// behind a sign-in. Services registered here override the portable defaults (playback + remote manager).
/// </summary>
public sealed class WebPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        // The browser can't manage the host from the Connect page; hide those sections.
        services.AddSingleton<Muselly.App.Services.IClientContext, WebClientContext>();

        // Signed-in Users/Admins create share links via the host API.
        services.AddSingleton<Muselly.App.Services.IShareLinkService, WebShareLinkService>();

        services.AddSingleton<WebSession>();
        services.AddSingleton<WebHostClient>();
        services.AddSingleton<WebLibraryLoader>();

        // Real browser audio (HTML audio element) replaces the silent simulator.
        services.AddSingleton<BrowserAudioPlaybackService>();
        services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<BrowserAudioPlaybackService>());

        // Resolve muselly:// art/lyrics/bio through the web API (reusing the shared ArtworkCache path).
        services.AddSingleton<IRemoteServerManager, WebRemoteServerManager>();

        // Lyrics and artist biographies are resolved (and cached) by the host, not fetched from public web
        // APIs in the browser. These override the Core defaults.
        services.AddSingleton<ILyricsService, WebLyricsService>();
        services.AddSingleton<IArtistInfoService, WebArtistInfoService>();

        // Favourites and the user directory/profiles come from the host's per-user API (the signed-in
        // account's server-side data), overriding the browser-local Core defaults.
        services.AddSingleton<IFavoritesService, WebFavoritesService>();
        services.AddSingleton<IUserService, WebUserService>();

        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<AdminViewModel>();
        services.AddSingleton<WebShellViewModel>();
    }

    public object CreateShell(IServiceProvider services) => new WebShell
    {
        DataContext = services.GetRequiredService<WebShellViewModel>()
    };

    public void OnStarted(IServiceProvider services) => _ = StartAsync(services);

    private static async Task StartAsync(IServiceProvider services)
    {
        try
        {
            // Restore theme/settings and prepare the (empty-until-login) library shell.
            _ = services.GetRequiredService<MainViewModel>().InitializeAsync();

            // Import the JS interop (origin + audio element) before any network/audio use. If this fails the
            // shell still resolves below (falling back to the sign-in form) rather than hanging.
            try { await services.GetRequiredService<BrowserAudioPlaybackService>().InitializeAsync(); }
            catch { /* audio unavailable; the UI still loads */ }

            // Resolve the host policy: auto-enter as guest if allowed, else show the sign-in form.
            await services.GetRequiredService<WebShellViewModel>().InitializeAsync();
        }
        catch
        {
            // Never let startup throw into the browser bootstrap.
        }
    }
}
