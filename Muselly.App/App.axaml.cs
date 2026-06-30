using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Muselly.Core.DependencyInjection;
using Muselly.Core.Services.Interfaces;
using Muselly.App.Platform;
using Muselly.App.ViewModels;

namespace Muselly.App
{
    /// <summary>
    /// The shared application class. Owns the portable composition root (core services, view-models,
    /// theming) and defers everything host-specific to <see cref="Platform"/>: the services that back
    /// playback and storage, and the shell to display. It supports both a classic-desktop lifetime (a
    /// top-level <see cref="Window"/>) and a single-view lifetime (an in-canvas <see cref="Control"/>,
    /// used by the browser head).
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider? ServiceProvider { get; private set; }

        /// <summary>
        /// The host integration. Each head sets this before starting Avalonia (it cannot be a constructor
        /// argument because Avalonia instantiates <see cref="App"/> itself).
        /// </summary>
        public static IPlatformServices? Platform { get; set; }

        /// <inheritdoc/>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <inheritdoc/>
        public override void OnFrameworkInitializationCompleted()
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

            // Portable core services.
            services.AddMusellyCore();

            // Live theming (Catppuccin variants + custom themes).
            services.AddSingleton<Theming.IThemeService, Theming.ThemeService>();

            // UI-layer services: native folder picker, the transport coordinator that bridges the queue
            // and the audio backend, and the navigation/history service.
            services.AddSingleton<Services.IClientContext, Services.LocalClientContext>();
            services.AddSingleton<Services.IShareLinkService, Services.ShareLinkService>();
            services.AddSingleton<Services.IFolderPicker, Services.StorageFolderPicker>();
            services.AddSingleton<Services.IJapaneseTextService, Services.JapaneseTextService>();
            services.AddSingleton<Services.PlaybackCoordinator>();
            services.AddSingleton<Services.ScrobbleCoordinator>();
            services.AddSingleton<Services.INavigationService, Services.NavigationService>();
            services.AddSingleton<Services.ISessionStateService, Services.SessionStateService>();

            // Server mode: shared no-op defaults so the Connect panel resolves on every head; the desktop
            // head overrides these (and the audio source resolver) with the real Muselly.Server services.
            services.AddSingleton<IServerHost, Services.NullServerHost>();
            services.AddSingleton<IRemoteServerManager, Services.NullRemoteServerManager>();
            services.AddSingleton<IWebServerHost, Services.NullWebServerHost>();
            services.AddSingleton<IShareService, Services.NullShareService>();

            // View models. Panel view models are singletons: they share state for the lifetime of the
            // single main view.
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<LibraryViewModel>();
            services.AddSingleton<PlaylistsViewModel>();
            services.AddSingleton<FavoritesViewModel>();
            services.AddSingleton<UsersViewModel>();
            services.AddTransient<UserProfileViewModel>(sp => new UserProfileViewModel(
                sp.GetRequiredService<IUserService>(),
                sp.GetRequiredService<IFavoritesService>(),
                sp.GetRequiredService<IListeningHistoryService>(),
                sp.GetRequiredService<ILibraryService>(),
                sp.GetRequiredService<IPlaybackService>(),
                sp.GetService<IServerUserStore>()));
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<ConnectViewModel>();
            services.AddSingleton<PlayerBarViewModel>();
            services.AddSingleton<QueueViewModel>();
            services.AddSingleton<LyricsViewModel>();
            services.AddSingleton<NowPlayingViewModel>();

            // Platform-specific services last, so a head can override a shared default (e.g. a
            // browser-safe storage service) and contribute its own backends.
            Platform?.RegisterServices(services);

            ServiceProvider = services.BuildServiceProvider();

            // Let cover-art controls stream remote artwork in via the connected servers.
            Services.ArtworkCache.RemoteManager = ServiceProvider.GetService<IRemoteServerManager>();

            // Activate the scrobble coordinator so it starts observing playback (it subscribes in its ctor).
            ServiceProvider.GetRequiredService<Services.ScrobbleCoordinator>();

            // Establish the font-size resources used across the app.
            ApplyFontScale(1.0);

            // Capture the palette brushes and apply the default theme.
            ServiceProvider.GetRequiredService<Theming.IThemeService>().Initialize();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = (Window)Platform!.CreateShell(ServiceProvider);
                // Flush the play session (queue + current track/position) as the app closes.
                desktop.ShutdownRequested += (_, _) =>
                    ServiceProvider?.GetService<Services.ISessionStateService>()?.SaveNow();
                Platform.OnStarted(ServiceProvider);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                singleView.MainView = (Control)Platform!.CreateShell(ServiceProvider);
                Platform.OnStarted(ServiceProvider);
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Applies the font scale to the application by modifying the ControlContentThemeFontSize resource
        /// and a family of explicit FontSizeN resources.
        /// </summary>
        /// <param name="scale">The scale factor (1.0 = 100%, 0.5 = 50%, 2.0 = 200%, etc.)</param>
        public static void ApplyFontScale(double scale)
        {
            if (Current?.Resources == null) return;

            // Used by the Fluent theme.
            Current.Resources["ControlContentThemeFontSize"] = 14.0 * scale;

            Current.Resources["FontSize8"] = 8.0 * scale;
            Current.Resources["FontSize9"] = 9.0 * scale;
            Current.Resources["FontSize10"] = 10.0 * scale;
            Current.Resources["FontSize11"] = 11.0 * scale;
            Current.Resources["FontSize12"] = 12.0 * scale;
            Current.Resources["FontSize13"] = 13.0 * scale;
            Current.Resources["FontSize14"] = 14.0 * scale;
            Current.Resources["FontSize15"] = 15.0 * scale;
            Current.Resources["FontSize16"] = 16.0 * scale;
            Current.Resources["FontSize18"] = 18.0 * scale;
            Current.Resources["FontSize20"] = 20.0 * scale;
            Current.Resources["FontSize22"] = 22.0 * scale;
            Current.Resources["FontSize24"] = 24.0 * scale;
            Current.Resources["FontSize32"] = 32.0 * scale;
        }
    }
}
