using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// Metadata reader backed by the pure-managed ATL library (z440.atl.core), which reads tags and embedded
/// pictures from MP3, FLAC, WAV, MP4/M4A (AAC/ALAC), Opus and Ogg without any native dependency — so it
/// works identically on every platform. Embedded cover art is written once per album into the artwork
/// cache and referenced by path, keeping the in-memory library light.
/// </summary>
public sealed class AtlMetadataReader : IMetadataReader
{
    private readonly ILogger<AtlMetadataReader> _logger;

    /// <summary>Album keys whose artwork has already been extracted this process, to avoid rework.</summary>
    private readonly ConcurrentDictionary<string, string?> _artworkByAlbum = new();

    public AtlMetadataReader(ILogger<AtlMetadataReader> logger) => _logger = logger;

    public Track? Read(string absolutePath)
    {
        try
        {
            var atl = new ATL.Track(absolutePath);
            var fileInfo = new FileInfo(absolutePath);

            var albumArtist = FirstNonEmpty(atl.AlbumArtist, atl.Artist) ?? "Unknown Artist";
            var album = string.IsNullOrWhiteSpace(atl.Album) ? "Unknown Album" : atl.Album!;
            var artist = FirstNonEmpty(atl.Artist, atl.AlbumArtist) ?? "Unknown Artist";
            var albumKey = Identifiers.AlbumKey(albumArtist, album);

            var artworkPath = _artworkByAlbum.GetOrAdd(albumKey, _ => ExtractArtwork(atl, albumKey));

            return new Track
            {
                Id = Identifiers.TrackId(absolutePath),
                Source = absolutePath,
                Title = FirstNonEmpty(atl.Title, Path.GetFileNameWithoutExtension(absolutePath)) ?? "Untitled",
                Artist = artist,
                AlbumArtist = albumArtist,
                Album = album,
                Composer = NullIfEmpty(atl.Composer),
                Genres = SplitGenres(atl.Genre),
                Label = NullIfEmpty(atl.Publisher),
                Year = YearOf(atl),
                TrackNumber = UIntOf(atl.TrackNumber),
                TrackCount = UIntOf(atl.TrackTotal),
                DiscNumber = UIntOf(atl.DiscNumber),
                DiscCount = UIntOf(atl.DiscTotal),
                Duration = TimeSpan.FromMilliseconds(atl.DurationMs),
                Bitrate = atl.Bitrate,
                SampleRate = (int)atl.SampleRate,
                Channels = atl.ChannelsArrangement?.NbChannels ?? 0,
                Codec = atl.AudioFormat?.Name,
                Extension = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                ArtworkPath = artworkPath,
                AlbumKey = albumKey,
                ArtistKey = Identifiers.ArtistKey(albumArtist),
                Directory = Path.GetDirectoryName(absolutePath) ?? string.Empty,
                FileModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTimeOffset.Now,
                DateAdded = DateTimeOffset.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read metadata for {Path}", absolutePath);
            return null;
        }
    }

    private string? ExtractArtwork(ATL.Track atl, string albumKey)
    {
        try
        {
            if (atl.EmbeddedPictures is null || atl.EmbeddedPictures.Count == 0)
                return null;

            // Prefer an explicit front-cover picture, else take the first.
            var picture = atl.EmbeddedPictures[0];
            foreach (var p in atl.EmbeddedPictures)
            {
                if (p.PicType == ATL.PictureInfo.PIC_TYPE.Front)
                {
                    picture = p;
                    break;
                }
            }

            var data = picture.PictureData;
            if (data is null || data.Length == 0) return null;

            var ext = MimeToExt(picture.MimeType);
            var dest = Path.Combine(StoragePaths.ArtworkCacheDirectory(), albumKey + "." + ext);
            if (!File.Exists(dest))
                File.WriteAllBytes(dest, data);
            return dest;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract artwork for album {Album}", albumKey);
            return null;
        }
    }

    private static string MimeToExt(string? mime) => mime switch
    {
        "image/png" => "png",
        "image/webp" => "webp",
        "image/gif" => "gif",
        "image/bmp" => "bmp",
        _ => "jpg"
    };

    private static uint UIntOf(int? value) => value is int v && v > 0 ? (uint)v : 0u;

    private static uint YearOf(ATL.Track atl)
    {
        if (atl.Year is int y && y > 0) return (uint)y;
        if (atl.Date is DateTime d && d.Year > 1) return (uint)d.Year;
        return 0;
    }

    private static IReadOnlyList<string> SplitGenres(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre)) return Array.Empty<string>();
        var parts = genre.Split(new[] { ';', '/', ',', '\u0000' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();
        foreach (var p in parts)
            if (!result.Contains(p)) result.Add(p);
        return result.Count > 0 ? result : new List<string> { genre.Trim() };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
