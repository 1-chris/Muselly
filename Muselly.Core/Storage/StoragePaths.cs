namespace Muselly.Core.Storage;

/// <summary>
/// Resolves the per-user data locations using each OS's standard directory:
/// Windows <c>%AppData%\Muselly</c>, macOS <c>~/Library/Application Support/Muselly</c>,
/// Linux <c>$XDG_CONFIG_HOME/Muselly</c> (falling back to <c>~/.config/Muselly</c>). Caches such as
/// extracted artwork live under a <c>cache</c> sub-directory.
/// </summary>
public static class StoragePaths
{
    private const string AppFolder = "Muselly";

    public static string ConfigDirectory()
    {
        string dir;
        if (OperatingSystem.IsWindows())
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);
        }
        else if (OperatingSystem.IsMacOS())
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", AppFolder);
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(xdg))
                xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            dir = Path.Combine(xdg, AppFolder);
        }

        try { Directory.CreateDirectory(dir); }
        catch { /* read-only / sandboxed environments (e.g. the browser demo) — callers tolerate failures */ }
        return dir;
    }

    public static string SettingsFile() => Path.Combine(ConfigDirectory(), "settings.json");

    public static string LibraryCacheFile() => Path.Combine(ConfigDirectory(), "library.json");

    public static string PlaylistsFile() => Path.Combine(ConfigDirectory(), "playlists.json");

    public static string ArtworkCacheDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "artwork");
        try { Directory.CreateDirectory(dir); }
        catch { /* tolerated in sandboxed environments */ }
        return dir;
    }

    /// <summary>Cache directory for fetched artist images (separate from album artwork).</summary>
    public static string ArtistImageCacheDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "artists");
        try { Directory.CreateDirectory(dir); }
        catch { /* tolerated in sandboxed environments */ }
        return dir;
    }

    /// <summary>Cache directory for fetched/exported lyrics (.lrc files keyed by track id).</summary>
    public static string LyricsCacheDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "lyrics");
        try { Directory.CreateDirectory(dir); }
        catch { /* tolerated in sandboxed environments */ }
        return dir;
    }

    /// <summary>Persisted per-artist metadata (biographies, image paths) fetched from the web.</summary>
    public static string ArtistInfoFile() => Path.Combine(ConfigDirectory(), "artist-info.json");

    // --- Server mode ---------------------------------------------------------------------------------

    /// <summary>Persisted built-in server configuration.</summary>
    public static string ServerConfigFile() => Path.Combine(ConfigDirectory(), "server.json");

    /// <summary>Persisted server user accounts (password hashes only).</summary>
    public static string ServerUsersFile() => Path.Combine(ConfigDirectory(), "server-users.json");

    /// <summary>The server's self-signed TLS certificate (PFX).</summary>
    public static string ServerCertificateFile() => Path.Combine(ConfigDirectory(), "server-cert.pfx");

    /// <summary>Persisted list of saved remote servers (with pinned fingerprints).</summary>
    public static string RemotesFile() => Path.Combine(ConfigDirectory(), "remotes.json");

    /// <summary>Persisted guest share links.</summary>
    public static string SharesFile() => Path.Combine(ConfigDirectory(), "shares.json");

    /// <summary>On-disk cache of server-transcoded Opus files (size-bounded).</summary>
    public static string TranscodeCacheDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "transcode-cache");
        try { Directory.CreateDirectory(dir); }
        catch { /* tolerated in sandboxed environments */ }
        return dir;
    }

    /// <summary>Cache of media (art/lyrics) streamed in from connected remote servers.</summary>
    public static string RemoteCacheDirectory()
    {
        var dir = Path.Combine(ConfigDirectory(), "remote-cache");
        try { Directory.CreateDirectory(dir); }
        catch { /* tolerated in sandboxed environments */ }
        return dir;
    }
}
