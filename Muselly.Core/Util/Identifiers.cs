using System.Security.Cryptography;
using System.Text;

namespace Muselly.Core.Util;

/// <summary>
/// Stable identifier and grouping-key helpers. Ids/keys are deterministic so a rescan of the same files
/// reproduces identical references (keeping queue/playlist links intact across scans).
/// </summary>
public static class Identifiers
{
    /// <summary>Canonical album-artist used for merged multi-artist compilation albums.</summary>
    public const string VariousArtists = "Various Artists";

    /// <summary>A short, stable hash of a string (hex, 16 chars).</summary>
    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    public static string TrackId(string absolutePath) => "t_" + Hash(Normalize(absolutePath));

    /// <summary>Album grouping key: album-artist + album title, normalised so trivial spelling/case/spacing
    /// differences collapse to the same album.</summary>
    public static string AlbumKey(string? albumArtist, string? album)
    {
        var a = NormalizeName(string.IsNullOrWhiteSpace(albumArtist) ? "Unknown Artist" : albumArtist!);
        var b = NormalizeName(string.IsNullOrWhiteSpace(album) ? "Unknown Album" : album!);
        return "al_" + Hash(a + "\u0001" + b);
    }

    /// <summary>Artist grouping key, normalised so trivial spelling/case/spacing differences collapse.</summary>
    public static string ArtistKey(string? artist)
    {
        var a = NormalizeName(string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist!);
        return "ar_" + Hash(a);
    }

    /// <summary>Normalises a display name (artist/album/title) for grouping and de-duplication comparisons:
    /// trims, lower-cases, applies Unicode compatibility composition, and collapses runs of whitespace to a
    /// single space. NOTE: only for grouping — not used for <see cref="TrackId"/>, whose path normalisation
    /// must stay byte-stable.</summary>
    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(s.Length);
        var prevWhitespace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWhitespace && sb.Length > 0) sb.Append(' ');
                prevWhitespace = true;
            }
            else
            {
                sb.Append(ch);
                prevWhitespace = false;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
