using Microsoft.Extensions.DependencyInjection;
using Muselly.Core.Services.Interfaces;
using Muselly.Server.Client;
using Muselly.Server.ServerHost;

namespace Muselly.Server.DependencyInjection;

/// <summary>
/// Registers the integrated server and remote-client services. A platform head (e.g. the desktop) calls this
/// after the Core registrations so the remote-aware audio resolver overrides the local-only default.
/// </summary>
public static class ServerServiceCollectionExtensions
{
    public static IServiceCollection AddMusellyServer(this IServiceCollection services)
    {
        services.AddSingleton<IServerHost, MusellyServerHost>();
        services.AddSingleton<IServerUserStore>(sp => ((MusellyServerHost)sp.GetRequiredService<IServerHost>()).Users);
        services.AddSingleton<IRemoteServerManager, RemoteServerManager>();

        // Override the local-only resolver so playback can stream remote tracks.
        services.AddSingleton<IAudioSourceResolver, RemoteAudioSourceResolver>();
        return services;
    }
}
