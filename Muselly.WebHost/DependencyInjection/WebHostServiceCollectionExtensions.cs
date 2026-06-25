using Microsoft.Extensions.DependencyInjection;
using Muselly.Core.Services.Interfaces;

namespace Muselly.WebHost.DependencyInjection;

/// <summary>
/// Registers the ASP.NET Core web host. Call after <c>AddMusellyServer()</c> (which provides the shared
/// engine and <c>MusellyApiService</c>) so the web host binds to the same library, users and firewall.
/// </summary>
public static class WebHostServiceCollectionExtensions
{
    public static IServiceCollection AddMusellyWebHost(this IServiceCollection services)
    {
        services.AddSingleton<IWebServerHost, MusellyWebServer>();
        return services;
    }
}
