using System.Collections.Generic;
using Muselly.Core.Models;

namespace Muselly.Web.Services;

// Client-side DTOs matching the web host's JSON (ASP.NET "web" defaults: camelCase, case-insensitive on read).

public sealed class HelloDto
{
    public string ServerName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public bool GuestEnabled { get; set; }
    public string LibraryEtag { get; set; } = string.Empty;
}

public sealed class LoginDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public UserRole Role { get; set; }
    public string SessionToken { get; set; } = string.Empty;
}

public sealed class LibraryDto
{
    public string Etag { get; set; } = string.Empty;
    public bool Unchanged { get; set; }
    public List<TrackDto> Tracks { get; set; } = new();
}

public sealed class TrackDto
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

public sealed class TextDto
{
    public string? Text { get; set; }
}

public sealed class ShareDto
{
    public string Id { get; set; } = string.Empty;
    public ShareKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class ShareLoginDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public UserRole Role { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public ShareKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class ServerSettingsClientDto
{
    public string ServerName { get; set; } = string.Empty;
    public int Port { get; set; }
    public int OpusBitrateKbps { get; set; }
    public long TranscodeCacheMaxBytes { get; set; }
    public bool GuestEnabled { get; set; }
    public bool UpnpEnabled { get; set; }
    public List<string> MusicFolders { get; set; } = new();
}

public sealed class UserClientDto
{
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public sealed class UserListClientDto
{
    public List<UserClientDto> Users { get; set; } = new();
}

public sealed class UserProfileClientDto
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
    public bool CanEdit { get; set; }
    public bool CanViewFavorites { get; set; }
    public bool CanViewNowPlaying { get; set; }
    public bool CanViewHistory { get; set; }
}

public sealed class UserProfileListClientDto
{
    public List<UserProfileClientDto> Users { get; set; } = new();
}

public sealed class FavoriteClientDto
{
    public FavoriteKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
}

public sealed class FavoritesClientDto
{
    public List<FavoriteClientDto> Favorites { get; set; } = new();
}

public sealed class ToggleFavoriteClientDto
{
    public bool Favorited { get; set; }
}

public sealed class HistoryEntryClientDto
{
    public string TrackId { get; set; } = string.Empty;
}

public sealed class HistoryClientDto
{
    public List<HistoryEntryClientDto> Entries { get; set; } = new();
}
