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
