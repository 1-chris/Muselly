namespace Muselly.Server.Protocol;

/// <summary>The set of request types understood by the server.</summary>
public static class MessageType
{
    public const string Hello = "hello";
    public const string Login = "login";
    public const string GetLibrary = "get-library";
    public const string GetAlbumArt = "get-album-art";
    public const string GetArtistImage = "get-artist-image";
    public const string GetArtistBio = "get-artist-bio";
    public const string GetLyrics = "get-lyrics";
    public const string StreamTrack = "stream-track";

    // Admin
    public const string AddFolder = "admin-add-folder";
    public const string RemoveFolder = "admin-remove-folder";
    public const string Rescan = "admin-rescan";
    public const string GetServerSettings = "admin-get-settings";
    public const string UpdateServerSettings = "admin-update-settings";
    public const string ListUsers = "admin-list-users";
    public const string AddUser = "admin-add-user";
    public const string RemoveUser = "admin-remove-user";
    public const string SetUserRole = "admin-set-user-role";
}
