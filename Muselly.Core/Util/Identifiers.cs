using System.Security.Cryptography;
using System.Text;

namespace Muselly.Core.Util;

/// <summary>
/// Stable identifier and grouping-key helpers. Ids/keys are deterministic so a rescan of the same files
/// reproduces identical references (keeping queue/playlist links intact across scans).
/// </summary>
public static class Identifiers
{
    /// <summary>A short, stable hash of a string (hex, 16 chars).</summary>
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    public static string TrackId(string absolutePath) => "t_" + Hash(Normalize(absolutePath));

    /// <summary>Album grouping key: album-artist + album title, case/space-insensitive.</summary>
    public static string AlbumKey(string? albumArtist, string? album)
    {
        var a = Normalize(string.IsNullOrWhiteSpace(albumArtist) ? "Unknown Artist" : albumArtist!);
        var b = Normalize(string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album!);
        return "al_" + Hash(a + "\u0001" + b);
    }

    /// <summary>Artist grouping key, case/space-insensitive.</summary>
    public static string ArtistKey(string? artist)
    {
        var a = Normalize(string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist!);
        return "ar_" + Hash(a);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
