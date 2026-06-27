using System.Collections.Concurrent;
using System.Text;
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
        // A normalization-sensitive filesystem can list a name in one Unicode form while the entry on disk is
        // in another, so the enumerated path may not open verbatim; try the standard forms and use the one
        // that exists for everything (id, source, playback). NOTE: this cannot rescue files on a macOS SMB
        // mount whose names are NFD (decomposed) — macOS's SMB client sends a normalized name on open() that
        // the server doesn't have, so the file is unopenable by any process; those are reported in the scan
        // summary and the fix is server-side (e.g. convert the names to NFC with convmv).
        var resolvedPath = ResolveReadablePath(absolutePath);
        if (resolvedPath is null)
        {
            _logger.LogDebug("Skipping file that could not be opened (not found): {Path}", absolutePath);
            return null;
        }

        try
        {
            var atl = new ATL.Track(resolvedPath);
            var fileInfo = new FileInfo(resolvedPath);

            var albumArtist = FirstNonEmpty(atl.AlbumArtist, atl.Artist) ?? "Unknown Artist";
            var album = string.IsNullOrWhiteSpace(atl.Album) ? "Unknown Album" : atl.Album!;
            var artist = FirstNonEmpty(atl.Artist, atl.AlbumArtist) ?? "Unknown Artist";
            var albumKey = Identifiers.AlbumKey(albumArtist, album);

            var artworkPath = _artworkByAlbum.GetOrAdd(albumKey, _ => ExtractArtwork(atl, albumKey));

            return new Track
            {
                Id = Identifiers.TrackId(resolvedPath),
                Source = resolvedPath,
                Title = FirstNonEmpty(atl.Title, Path.GetFileNameWithoutExtension(resolvedPath)) ?? "Untitled",
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
                IsCompilation = ReadCompilationFlag(atl),
                AlbumKey = albumKey,
                ArtistKey = Identifiers.ArtistKey(albumArtist),
                Directory = Path.GetDirectoryName(resolvedPath) ?? string.Empty,
                FileModified = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTimeOffset.Now,
                DateAdded = DateTimeOffset.Now
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            // Unopenable (e.g. an NFD-named file on a macOS SMB mount) — counted in the scan summary; keep
            // the per-file detail at debug level so it doesn't flood the log.
            _logger.LogDebug("Could not read {Path}: {Message}", resolvedPath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read metadata for {Path}", resolvedPath);
            return null;
        }
    }

    /// <summary>Returns a path that opens: the path as given, or the first Unicode normalization variant
    /// (NFC/NFD/…) that exists — handling filename-form mismatches on normalization-sensitive volumes — or
    /// null if none resolve.</summary>
    private static string? ResolveReadablePath(string path)
    {
        if (File.Exists(path)) return path;

        foreach (var form in NormalizationForms)
        {
            try
            {
                if (path.IsNormalized(form)) continue;
                var candidate = path.Normalize(form);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Path holds code points that are invalid for this normalization form — skip it.
            }
        }
        return null;
    }

    private static readonly NormalizationForm[] NormalizationForms =
        { NormalizationForm.FormC, NormalizationForm.FormD, NormalizationForm.FormKC, NormalizationForm.FormKD };

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

    // Compilation flag, spelled differently per container: MP4 'cpil', ID3v2 'TCMP', Vorbis/FLAC
    // 'COMPILATION', WMA 'WM/IsCompilation'. ATL surfaces these raw in AdditionalFields, so match any of
    // them case-insensitively and treat a truthy value as a compilation.
    private static readonly string[] CompilationKeys = { "cpil", "tcmp", "compilation", "wm/iscompilation" };

    private static bool ReadCompilationFlag(ATL.Track atl)
    {
        try
        {
            var fields = atl.AdditionalFields;
            if (fields is null || fields.Count == 0) return false;
            foreach (var kv in fields)
            {
                foreach (var key in CompilationKeys)
                {
                    if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                    var v = kv.Value?.Trim();
                    if (!string.IsNullOrEmpty(v) && v != "0" &&
                        !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(v, "no", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            // AdditionalFields access can throw on malformed tags; treat as "not a compilation".
        }
        return false;
    }

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
