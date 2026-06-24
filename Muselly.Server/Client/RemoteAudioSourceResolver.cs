using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;

namespace Muselly.Server.Client;

/// <summary>
/// Remote-aware <see cref="IAudioSourceResolver"/>: local tracks resolve to their own path; remote
/// (<c>muselly://</c>) tracks are streamed from the owning server (server-side transcoded to Opus) into a
/// local cache file that the normal ffmpeg decode path then turns into PCM for playback.
/// </summary>
public sealed class RemoteAudioSourceResolver : IAudioSourceResolver
{
    private readonly IRemoteServerManager _remotes;
    private readonly ILogger<RemoteAudioSourceResolver> _logger;

    public RemoteAudioSourceResolver(IRemoteServerManager remotes, ILogger<RemoteAudioSourceResolver> logger)
    {
        _remotes = remotes;
        _logger = logger;
    }

    public bool CanResolve(Track track) => true;

    public async Task<string> ResolveLocalPathAsync(Track track, CancellationToken cancellationToken = default)
    {
        if (!RemoteSource.IsRemote(track.Source)) return track.Source;

        if (!RemoteSource.TryParse(track.Source, out var serverId, out _, out var trackId))
            throw new InvalidOperationException($"Unparseable remote source '{track.Source}'.");

        var dir = Path.Combine(StoragePaths.RemoteCacheDirectory(), "audio");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, $"{serverId}_{trackId}.opus");

        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            return dest;

        var result = await _remotes.DownloadTrackAsync(serverId, trackId, dest, cancellationToken).ConfigureAwait(false);
        if (result is null)
            throw new InvalidOperationException($"Failed to stream remote track '{track.Title}'.");

        return result;
    }
}
