using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// The default <see cref="IAudioSourceResolver"/>: returns the track's own on-disk path. It declines remote
/// (<c>muselly://</c>) sources so a platform head can layer a remote-aware resolver on top.
/// </summary>
public sealed class LocalAudioSourceResolver : IAudioSourceResolver
{
    public bool CanResolve(Track track) => !RemoteSource.IsRemote(track.Source);

    public Task<string> ResolveLocalPathAsync(Track track, CancellationToken cancellationToken = default) =>
        Task.FromResult(track.Source);
}
