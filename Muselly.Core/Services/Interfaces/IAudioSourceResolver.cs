using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Resolves a <see cref="Track"/> to a readable local file path the audio backend can decode. For local
/// tracks this is just the track's source path; for remote (<c>muselly://</c>) tracks the implementation
/// streams the server-transcoded Opus to a temporary file and returns that path. This is the seam that lets
/// the playback engine stay oblivious to where a track physically lives.
/// </summary>
public interface IAudioSourceResolver
{
    /// <summary>True if this resolver knows how to fetch the given track's source (e.g. it is remote).</summary>
    bool CanResolve(Track track);

    /// <summary>
    /// Returns a local file path that can be decoded. May download/transcode first. Throws on failure.
    /// </summary>
    Task<string> ResolveLocalPathAsync(Track track, CancellationToken cancellationToken = default);
}
