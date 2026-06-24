using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Muselly.App.Platform;
using Muselly.App.ViewModels;
using Muselly.App.Views;

namespace Muselly.Web;

/// <summary>
/// Browser host integration: shows the in-canvas <see cref="MainView"/> instead of a desktop window, and
/// is where browser-safe service stubs (sandbox storage, Web Audio backend) are registered. Registrations
/// here run after the shared defaults, so they win.
/// </summary>
public sealed class WebPlatform : IPlatformServices
{
    public void RegisterServices(IServiceCollection services)
    {
        // Override filesystem/native-backed shared defaults with sandbox-safe versions as they are added
        // (e.g. a browser storage-backed settings service, a Web Audio playback backend).
    }

    public object CreateShell(IServiceProvider services) => new MainView
    {
        DataContext = services.GetRequiredService<MainViewModel>()
    };

    public void OnStarted(IServiceProvider services)
    {
        // Demo head: load any cached state and restore the theme, but there is no native folder scanning
        // in the browser sandbox — the playback simulator drives the UI so the player still "works".
        _ = services.GetRequiredService<MainViewModel>().InitializeAsync();
    }
}
