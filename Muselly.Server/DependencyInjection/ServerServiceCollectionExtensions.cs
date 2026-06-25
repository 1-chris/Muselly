using Microsoft.Extensions.DependencyInjection;
using Muselly.Core.Services.Interfaces;
using Muselly.Server.Api;
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
        // The transport-agnostic engine: shared by the TLS host and the HTTP web host.
        services.AddSingleton<IShareService, Auth.ShareService>();
        services.AddSingleton<ServerEngine>();
        services.AddSingleton<MusellyApiService>();

        services.AddSingleton<IServerHost, MusellyServerHost>();
        services.AddSingleton<IServerUserStore>(sp => sp.GetRequiredService<ServerEngine>().Users);
        services.AddSingleton<IRemoteServerManager, RemoteServerManager>();

        // Override the local-only resolver so playback can stream remote tracks.
        services.AddSingleton<IAudioSourceResolver, RemoteAudioSourceResolver>();
        return services;
    }
}
