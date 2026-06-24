namespace Muselly.Core.Util;

/// <summary>
/// Helpers for the <c>muselly://</c> URI scheme used to reference resources that live on a connected remote
/// server (tracks, album art, artist images). Format: <c>muselly://&lt;serverId&gt;/&lt;kind&gt;/&lt;key&gt;</c>.
/// A <see cref="Models.Track"/> whose <c>Source</c>/<c>ArtworkPath</c> uses this scheme is streamed from the
/// owning server rather than read from the local disk.
/// </summary>
public static class RemoteSource
{
    public const string Scheme = "muselly://";

    public const string KindTrack = "track";
    public const string KindAlbumArt = "albumart";
    public const string KindArtistImage = "artistimg";

    public static bool IsRemote(string? source) =>
        !string.IsNullOrEmpty(source) && source.StartsWith(Scheme, StringComparison.Ordinal);

    public static string Build(string serverId, string kind, string key) =>
        $"{Scheme}{serverId}/{kind}/{Uri.EscapeDataString(key)}";

    public static bool TryParse(string? source, out string serverId, out string kind, out string key)
    {
        serverId = kind = key = string.Empty;
        if (!IsRemote(source)) return false;

        var rest = source!.Substring(Scheme.Length);
        var parts = rest.Split('/', 3);
        if (parts.Length < 3) return false;

        serverId = parts[0];
        kind = parts[1];
        key = Uri.UnescapeDataString(parts[2]);
        return !string.IsNullOrEmpty(serverId) && !string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(key);
    }
}
