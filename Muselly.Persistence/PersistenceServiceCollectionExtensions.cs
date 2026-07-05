using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default JSON library store with the SQLite-backed one. Call after <c>AddMusellyCore</c>
    /// from heads that have a native SQLite library available (desktop / headless / server) — never the
    /// browser head.
    /// </summary>
    public static IServiceCollection AddSqliteLibraryStore(this IServiceCollection services)
    {
        services.RemoveAll<ILibraryStore>();
        services.AddSingleton<ILibraryStore, SqliteLibraryStore>();
        return services;
    }
}
