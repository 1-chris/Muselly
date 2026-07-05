using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Util;

namespace Muselly.Web.Services;

/// <summary>
/// Fetches the host library over the web API and merges it into the shared <see cref="ILibraryService"/>
/// (reusing the desktop's "remote tracks" mechanism, so the same album/artist organisation is built). Track
/// sources and artwork become <c>muselly://host/...</c> URIs the browser playback and artwork code resolve.
/// </summary>
public sealed class WebLibraryLoader
{
    private readonly WebHostClient _client;
    private readonly ILibraryService _library;
    private string? _etag;

    public WebLibraryLoader(WebHostClient client, ILibraryService library)
    {
        _client = client;
        _library = library;
    }

    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        var dto = await _client.GetLibraryAsync(_etag, ct).ConfigureAwait(false);
        if (dto is null) return 0;
        if (dto.Unchanged) return _library.TrackCount;

        _etag = dto.Etag;
        var tracks = new List<Track>(dto.Tracks.Count);
        foreach (var t in dto.Tracks) tracks.Add(Map(t));
        _library.AddRemoteTracks(WebSession.HostId, tracks);
        return tracks.Count;
    }

    private static Track Map(TrackDto d) => new()
    {
        Id = d.Id,
        Source = RemoteSource.Build(WebSession.HostId, RemoteSource.KindTrack, d.Id),
        Title = d.Title,
        Artist = d.Artist,
        AlbumArtist = d.AlbumArtist,
        Album = d.Album,
        Composer = d.Composer,
        Genres = d.Genres,
        Label = d.Label,
        Year = d.Year,
        TrackNumber = d.TrackNumber,
        TrackCount = d.TrackCount,
        DiscNumber = d.DiscNumber,
        DiscCount = d.DiscCount,
        Duration = System.TimeSpan.FromSeconds(d.DurationSeconds),
        Bitrate = d.Bitrate,
        SampleRate = d.SampleRate,
        Channels = d.Channels,
        Codec = d.Codec,
        Extension = d.Extension,
        FileSizeBytes = d.FileSizeBytes,
        ArtworkPath = d.HasArtwork ? RemoteSource.Build(WebSession.HostId, RemoteSource.KindAlbumArt, d.AlbumKey) : null,
        AlbumKey = d.AlbumKey,
        ArtistKey = d.ArtistKey,
        Directory = string.Empty
    };
}
