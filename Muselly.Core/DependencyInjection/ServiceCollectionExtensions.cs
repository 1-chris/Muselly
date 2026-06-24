using Microsoft.Extensions.DependencyInjection;
using Muselly.Core.Audio;
using Muselly.Core.Services.Implementation;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;

namespace Muselly.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all portable Muselly.Core services. Platform heads call this first, then override
    /// individual registrations as needed (last-wins) — e.g. a native audio backend replacing the
    /// silent <see cref="NullPlaybackService"/>.
    /// </summary>
    public static IServiceCollection AddMusellyCore(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAudioEqualizer, Equalizer>();
        services.AddSingleton<IMetadataReader, AtlMetadataReader>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<IQueueService, QueueService>();
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<IPlaybackService, NullPlaybackService>();

        // Default (local-only) audio source resolver. Desktop layers a remote-aware one on top.
        services.AddSingleton<IAudioSourceResolver, LocalAudioSourceResolver>();

        // Web metadata enrichment (key-free public sources): album art, artist biographies/images, lyrics.
        services.AddSingleton<IAlbumArtFetcher, AlbumArtFetcher>();
        services.AddSingleton<IArtworkScanService, ArtworkScanService>();
        services.AddSingleton<IArtistInfoService, ArtistInfoService>();

        // Lyrics providers, tried in registration order (synced results preferred across all of them).
        services.AddSingleton<ILyricsProvider, LrcLibLyricsProvider>();
        services.AddSingleton<ILyricsProvider, NetEaseLyricsProvider>();
        services.AddSingleton<ILyricsProvider, LyricsOvhProvider>();
        services.AddSingleton<ILyricsService, LyricsService>();
        return services;
    }
}
