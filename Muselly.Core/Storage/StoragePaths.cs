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

    /// <summary>SQLite database holding the local library (desktop/headless/server heads).</summary>
    public static string LibraryDatabaseFile() => Path.Combine(ConfigDirectory(), "library.db");

    /// <summary>Marker written while a folder-wide scan is in progress and removed on success, so an
    /// interrupted (e.g. very long) scan can be resumed automatically on the next launch.</summary>
    public static string LibraryScanMarkerFile() => Path.Combine(ConfigDirectory(), "library.scanning");

    public static string PlaylistsFile() => Path.Combine(ConfigDirectory(), "playlists.json");

    /// <summary>Persisted play queue + current track/position, so a session resumes where it left off.</summary>
    public static string SessionStateFile() => Path.Combine(ConfigDirectory(), "session.json");

    /// <summary>Persisted favourited songs/albums/artists for the local user.</summary>
    public static string FavoritesFile() => Path.Combine(ConfigDirectory(), "favorites.json");

    /// <summary>Persisted per-user scrobbling connections (session keys / tokens).</summary>
    public static string ScrobbleAccountsFile() => Path.Combine(ConfigDirectory(), "scrobble-accounts.json");

    /// <summary>Persisted user profiles (built-in local user + privacy settings, bios, pictures).</summary>
    public static string UserProfilesFile() => Path.Combine(ConfigDirectory(), "user-profiles.json");

    /// <summary>Persisted local listening history (track id + timestamp).</summary>
    public static string ListeningHistoryFile() => Path.Combine(ConfigDirectory(), "listening-history.json");

    /// <summary>Per-remote-user server data (favourites + history), one file per account.</summary>
    public static string UserDataFile(string username)
    {
        var dir = Path.Combine(ConfigDirectory(), "userdata");
        try { Directory.CreateDirectory(dir); }
        catch { /* tolerated */ }
        // Hash the username so arbitrary names map to a safe, stable filename.
        var safe = Util.Identifiers.Hash(username.Trim().ToLowerInvariant());
        return Path.Combine(dir, safe + ".json");
    }

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
