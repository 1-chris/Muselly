using Muselly.Core.Models;

namespace Muselly.Server.Protocol;

// Request/response payload contracts. All are plain DTOs (de)serialised as the Envelope payload.

public sealed class HelloRequest
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;
}

public sealed class HelloResponse
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.ProtocolVersion;
    public string ServerName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public bool GuestEnabled { get; set; }
    /// <summary>Changes whenever the library content changes, so clients can avoid re-downloading.</summary>
    public string LibraryEtag { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Guest { get; set; }
}

public sealed class LoginResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public UserRole Role { get; set; }
    public string SessionToken { get; set; } = string.Empty;
}

public sealed class GetLibraryRequest
{
    public string? KnownEtag { get; set; }
}

public sealed class GetLibraryResponse
{
    public string Etag { get; set; } = string.Empty;
    public bool Unchanged { get; set; }
    public List<RemoteTrackDto> Tracks { get; set; } = new();
}

/// <summary>
/// A library track as sent to clients. Deliberately omits the on-disk path and folder ("folders are not
/// shared") — only indexed, displayable metadata plus stable keys and an artwork-availability flag.
/// </summary>
public sealed class RemoteTrackDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Album { get; set; }
    public string? Composer { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? Label { get; set; }
    public uint Year { get; set; }
    public uint TrackNumber { get; set; }
    public uint TrackCount { get; set; }
    public uint DiscNumber { get; set; }
    public uint DiscCount { get; set; }
    public double DurationSeconds { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string? Codec { get; set; }
    public string Extension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string AlbumKey { get; set; } = string.Empty;
    public string ArtistKey { get; set; } = string.Empty;
    public bool HasArtwork { get; set; }
}

public sealed class KeyRequest
{
    public string Key { get; set; } = string.Empty;
}

public sealed class StreamTrackRequest
{
    public string TrackId { get; set; } = string.Empty;
    public int BitrateKbps { get; set; }
}

public sealed class TextResponse
{
    public string? Text { get; set; }
}

public sealed class FolderRequest
{
    public string Path { get; set; } = string.Empty;
}

public sealed class ServerSettingsDto
{
    public string ServerName { get; set; } = string.Empty;
    public int Port { get; set; }
    public int OpusBitrateKbps { get; set; }
    public long TranscodeCacheMaxBytes { get; set; }
    public bool GuestEnabled { get; set; }
    public bool UpnpEnabled { get; set; }
    public List<string> MusicFolders { get; set; } = new();
}

public sealed class UserDto
{
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public sealed class UserListResponse
{
    public List<UserDto> Users { get; set; } = new();
}

public sealed class AddUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
}

public sealed class OkResponse
{
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
}

public sealed class ShareLoginRequest
{
    public string Token { get; set; } = string.Empty;
}

public sealed class ShareLoginResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public UserRole Role { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public ShareKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class ShareDto
{
    public string Id { get; set; } = string.Empty;
    public ShareKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ShareListResponse
{
    public List<ShareDto> Shares { get; set; } = new();
}

public sealed class CreateShareRequest
{
    public ShareKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int? Days { get; set; }
}

// --- Users / profiles / per-user data ----------------------------------------------------------------

/// <summary>A user profile over the wire. Facets the caller may not see are blanked/omitted by the server.</summary>
public sealed class UserProfileDto
{
    public string Username { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool IsAdmin { get; set; }
    public string? Bio { get; set; }
    public bool HasPicture { get; set; }
    public PrivacyVisibility ProfileVisibility { get; set; }
    public PrivacyVisibility FavoritesVisibility { get; set; }
    public PrivacyVisibility NowPlayingVisibility { get; set; }
    public PrivacyVisibility ListeningHistoryVisibility { get; set; }
    public bool RemoteLoginEnabled { get; set; }

    /// <summary>True when the requesting user is allowed to edit this profile (self or admin).</summary>
    public bool CanEdit { get; set; }

    public bool CanViewFavorites { get; set; }
    public bool CanViewNowPlaying { get; set; }
    public bool CanViewHistory { get; set; }
}

public sealed class UserProfileListResponse
{
    public List<UserProfileDto> Users { get; set; } = new();
}

/// <summary>Identifies a target user for a profile/favourites/history request.</summary>
public sealed class UserRequest
{
    public string Username { get; set; } = string.Empty;
}

/// <summary>Profile edits submitted by a user. Username changes are honoured only for the built-in user.</summary>
public sealed class UpdateProfileRequest
{
    public string Username { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public PrivacyVisibility ProfileVisibility { get; set; }
    public PrivacyVisibility FavoritesVisibility { get; set; }
    public PrivacyVisibility NowPlayingVisibility { get; set; }
    public PrivacyVisibility ListeningHistoryVisibility { get; set; }
}

public sealed class FavoriteDto
{
    public FavoriteKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; }
}

public sealed class FavoritesResponse
{
    public List<FavoriteDto> Favorites { get; set; } = new();
}

public sealed class ToggleFavoriteRequest
{
    public string Username { get; set; } = string.Empty;
    public FavoriteKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;

    /// <summary>When set, forces the favourite on/off; when null, toggles.</summary>
    public bool? Favorite { get; set; }
}

public sealed class ToggleFavoriteResponse
{
    public bool Favorited { get; set; }
}

public sealed class HistoryEntryDto
{
    public string TrackId { get; set; } = string.Empty;
    public DateTimeOffset PlayedAt { get; set; }
}

public sealed class HistoryResponse
{
    public List<HistoryEntryDto> Entries { get; set; } = new();
}
