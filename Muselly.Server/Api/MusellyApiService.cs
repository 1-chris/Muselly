using Muselly.Core.Models;
using Muselly.Core.Util;
using Muselly.Server.Auth;
using Muselly.Server.Protocol;
using Muselly.Server.ServerHost;

namespace Muselly.Server.Api;

/// <summary>
/// A transport-agnostic facade over the <see cref="ServerEngine"/> exposing the operations a client needs:
/// handshake, login, library, media (art/bio/lyrics), Opus streaming and the admin actions. It returns the
/// same DTOs the framed protocol uses, so the HTTP web host can serialise them straight to JSON. No HTTP or
/// socket types appear here, which is what lets the web host and a future headless host share it.
/// </summary>
public sealed class MusellyApiService
{
    private readonly ServerEngine _engine;

    public MusellyApiService(ServerEngine engine) => _engine = engine;

    public ServerEngine Engine => _engine;

    // --- Handshake / auth ----------------------------------------------------------------------------

    public HelloResponse Hello()
    {
        var s = _engine.Config;
        return new HelloResponse
        {
            ServerName = string.IsNullOrWhiteSpace(s.ServerName) ? Environment.MachineName : s.ServerName,
            AppVersion = AppVersionString,
            GuestEnabled = s.GuestEnabled,
            LibraryEtag = _engine.LibraryEtag
        };
    }

    /// <summary>Validates credentials (or guest), issues a session token, and logs the sign-in.</summary>
    public LoginResponse Login(string? username, string? password, bool guest)
    {
        if (guest)
        {
            if (!_engine.Config.GuestEnabled)
                return new LoginResponse { Success = false, Error = "Guest access is disabled." };
            var guestSession = _engine.Sessions.Create("guest", UserRole.Guest);
            _engine.Log("guest signed in (Guest) [web]");
            return new LoginResponse { Success = true, Role = UserRole.Guest, SessionToken = guestSession.Token };
        }

        var user = _engine.Users.Validate(username ?? string.Empty, password ?? string.Empty);
        if (user is null)
            return new LoginResponse { Success = false, Error = "Invalid username or password." };

        var session = _engine.Sessions.Create(user.Username, user.Role);
        _engine.Log($"{user.Username} signed in ({user.Role}) [web]");
        return new LoginResponse { Success = true, Role = user.Role, SessionToken = session.Token };
    }

    public Session? Authenticate(string? token) => _engine.Sessions.Get(token);

    // --- Share links ---------------------------------------------------------------------------------

    /// <summary>Opens a share link: validates it and issues a guest session scoped to just the shared item.</summary>
    public ShareLoginResponse ShareLogin(string token)
    {
        var share = _engine.Shares.Find(token);
        if (share is null || share.IsExpired)
            return new ShareLoginResponse { Success = false, Error = "This share link is invalid or has expired." };

        var scope = new ShareScope { Kind = share.Kind, Key = share.Key };
        var session = _engine.Sessions.Create("guest", UserRole.Guest, scope);
        _engine.Log($"guest opened share \u201c{share.Label}\u201d [web]");
        return new ShareLoginResponse
        {
            Success = true,
            Role = UserRole.Guest,
            SessionToken = session.Token,
            Kind = share.Kind,
            Key = share.Key,
            Label = share.Label
        };
    }

    public ShareListResponse ListShares() => new()
    {
        Shares = _engine.Shares.List().Select(s => new ShareDto
        {
            Id = s.Id, Kind = s.Kind, Key = s.Key, Label = s.Label, CreatedAt = s.CreatedAt, ExpiresAt = s.ExpiresAt
        }).ToList()
    };

    public ShareDto CreateShare(CreateShareRequest req)
    {
        var lifetime = req.Days is { } d and > 0 ? TimeSpan.FromDays(d) : (TimeSpan?)null;
        var link = _engine.Shares.Create(req.Kind, req.Key, req.Label, lifetime);
        return new ShareDto
        {
            Id = link.Id, Kind = link.Kind, Key = link.Key, Label = link.Label,
            CreatedAt = link.CreatedAt, ExpiresAt = link.ExpiresAt
        };
    }

    public void RevokeShare(string id) => _engine.Shares.Revoke(id);

    // --- Library / media -----------------------------------------------------------------------------

    public GetLibraryResponse GetLibrary(Session session, string? knownEtag)
    {
        // Share-scoped sessions only see the shared item's tracks (etag caching is skipped for them).
        if (session.Scope is { } scope)
        {
            var scoped = new GetLibraryResponse { Etag = _engine.LibraryEtag };
            foreach (var t in ScopedTracks(scope)) scoped.Tracks.Add(ToDto(t));
            return scoped;
        }

        var etag = _engine.LibraryEtag;
        if (!string.IsNullOrEmpty(knownEtag) && knownEtag == etag)
            return new GetLibraryResponse { Etag = etag, Unchanged = true };

        var response = new GetLibraryResponse { Etag = etag };
        foreach (var t in _engine.Library.Tracks)
        {
            if (RemoteSource.IsRemote(t.Source)) continue; // only share this host's own tracks
            response.Tracks.Add(ToDto(t));
        }
        return response;
    }

    /// <summary>Returns the on-disk path of an album cover, or null when there's none to serve / out of scope.</summary>
    public string? AlbumArtPath(Session session, string key)
    {
        if (!InScope(session, albumKey: key)) return null;
        var path = _engine.Library.FindAlbum(key)?.ArtworkPath;
        return Servable(path) ? path : null;
    }

    public string? ArtistImagePath(Session session, string key)
    {
        if (!InScope(session, artistKey: key)) return null;
        var path = _engine.Library.FindArtist(key)?.ArtworkPath;
        return Servable(path) ? path : null;
    }

    public async Task<string?> GetArtistBioAsync(Session session, string key, CancellationToken ct = default)
    {
        if (!InScope(session, artistKey: key)) return null;
        if (_engine.ArtistInfo is not { } info) return null;
        var artist = _engine.Library.FindArtist(key);
        if (artist is null) return null;
        var data = await info.EnsureAsync(artist.Key, artist.Name, ct).ConfigureAwait(false);
        return data?.Biography;
    }

    public async Task<string?> GetLyricsAsync(Session session, string trackId, CancellationToken ct = default)
    {
        if (!InScope(session, trackId: trackId)) return null;
        var track = _engine.Library.FindTrack(trackId);
        if (track is null || _engine.Lyrics is not { } lyrics) return null;
        var doc = await lyrics.GetAsync(track, ct).ConfigureAwait(false);
        return BuildLrc(doc);
    }

    /// <summary>
    /// Resolves a track id to a ready-to-serve Opus file path (transcoding + caching on demand). Returns null
    /// if the track is unknown, remote, missing, out of scope or the transcode failed.
    /// </summary>
    public async Task<string?> GetStreamPathAsync(Session session, string trackId, int bitrateKbps,
        CancellationToken ct = default)
    {
        if (!InScope(session, trackId: trackId)) return null;
        var track = _engine.Library.FindTrack(trackId);
        if (track is null || RemoteSource.IsRemote(track.Source) || !File.Exists(track.Source)) return null;

        var bitrate = bitrateKbps > 0 ? bitrateKbps : _engine.Config.OpusBitrateKbps;
        var opusPath = await _engine.TranscodeCache.GetOrCreateAsync(track.Id, bitrate, track.Source, ct)
            .ConfigureAwait(false);
        if (opusPath is null || !File.Exists(opusPath)) return null;

        var artist = string.IsNullOrWhiteSpace(track.Artist) ? track.DisplayArtist : track.Artist!;
        _engine.Log($"{session.Username} streamed \u201c{track.Title}\u201d by {artist} [web]");
        return opusPath;
    }

    // --- Scope enforcement ---------------------------------------------------------------------------

    /// <summary>The tracks a share scope grants access to (this host's local tracks only).</summary>
    private IReadOnlyList<Track> ScopedTracks(ShareScope scope)
    {
        bool Local(Track t) => !RemoteSource.IsRemote(t.Source);
        return scope.Kind switch
        {
            ShareKind.Album => _engine.Library.Tracks.Where(t => Local(t) && t.AlbumKey == scope.Key).ToList(),
            ShareKind.Artist => _engine.Library.Tracks.Where(t => Local(t) && t.ArtistKey == scope.Key).ToList(),
            ShareKind.Song => _engine.Library.Tracks.Where(t => Local(t) && t.Id == scope.Key).ToList(),
            ShareKind.Playlist => (_engine.Playlists?.ResolveTracks(scope.Key) ?? Array.Empty<Track>())
                .Where(Local).ToList(),
            _ => Array.Empty<Track>()
        };
    }

    /// <summary>Non-scoped (full-access) sessions allow everything; scoped sessions allow only their item.</summary>
    private bool InScope(Session session, string? trackId = null, string? albumKey = null, string? artistKey = null)
    {
        if (session.Scope is not { } scope) return true;

        var tracks = ScopedTracks(scope);
        if (trackId is not null) return tracks.Any(t => t.Id == trackId);
        if (albumKey is not null) return tracks.Any(t => t.AlbumKey == albumKey);
        if (artistKey is not null) return tracks.Any(t => t.ArtistKey == artistKey);
        return true;
    }

    // --- Admin ---------------------------------------------------------------------------------------

    public ServerSettingsDto GetSettings()
    {
        var s = _engine.Config;
        return new ServerSettingsDto
        {
            ServerName = s.ServerName,
            Port = s.Port,
            OpusBitrateKbps = s.OpusBitrateKbps,
            TranscodeCacheMaxBytes = s.TranscodeCacheMaxBytes,
            GuestEnabled = s.GuestEnabled,
            UpnpEnabled = s.UpnpEnabled,
            MusicFolders = _engine.AppSettingsService.Current.MusicFolders.ToList()
        };
    }

    public void UpdateSettings(ServerSettingsDto dto)
    {
        _engine.UpdateConfig(s =>
        {
            s.ServerName = dto.ServerName;
            s.OpusBitrateKbps = dto.OpusBitrateKbps;
            s.TranscodeCacheMaxBytes = dto.TranscodeCacheMaxBytes;
            s.GuestEnabled = dto.GuestEnabled;
            s.UpnpEnabled = dto.UpnpEnabled;
        });
        _engine.Log("server settings updated [web]");
    }

    public void AddFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _engine.AppSettingsService.AddMusicFolder(path);
        _engine.Log($"added music folder {path} [web]");
    }

    public void RemoveFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _engine.AppSettingsService.RemoveMusicFolder(path);
        _engine.Log($"removed music folder {path} [web]");
    }

    public Task RescanAsync(string? folder, CancellationToken ct = default)
    {
        _engine.Log("triggered a library rescan [web]");
        return string.IsNullOrWhiteSpace(folder)
            ? _engine.Library.ScanAsync(ct)
            : _engine.Library.ScanFolderAsync(folder, ct);
    }

    public UserListResponse ListUsers() => new()
    {
        Users = _engine.Users.Users.Select(u => new UserDto { Username = u.Username, Role = u.Role }).ToList()
    };

    public void AddUser(string username, string password, UserRole role)
    {
        if (!string.IsNullOrWhiteSpace(username)) _engine.Users.AddOrUpdate(username, password, role);
    }

    public void SetUserRole(string username, UserRole role)
    {
        if (!string.IsNullOrWhiteSpace(username)) _engine.Users.SetRole(username, role);
    }

    public void RemoveUser(string username)
    {
        if (!string.IsNullOrWhiteSpace(username)) _engine.Users.Remove(username);
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static bool Servable(string? path) =>
        !string.IsNullOrEmpty(path) && !RemoteSource.IsRemote(path) && File.Exists(path);

    private static string AppVersionString =>
        typeof(MusellyApiService).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    private static string? BuildLrc(LyricsDocument? doc)
    {
        if (doc is null || !doc.HasLines) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var line in doc.Lines)
        {
            if (doc.IsSynced && line.TimeMs is { } ms)
            {
                var t = TimeSpan.FromMilliseconds(ms);
                sb.Append('[').Append(((int)t.TotalMinutes).ToString("00")).Append(':')
                  .Append(t.Seconds.ToString("00")).Append('.')
                  .Append((t.Milliseconds / 10).ToString("00")).Append(']');
            }
            sb.AppendLine(line.Text);
        }
        return sb.ToString();
    }

    private static RemoteTrackDto ToDto(Track t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Artist = t.Artist,
        AlbumArtist = t.AlbumArtist,
        Album = t.Album,
        Composer = t.Composer,
        Genres = t.Genres.ToList(),
        Label = t.Label,
        Year = t.Year,
        TrackNumber = t.TrackNumber,
        TrackCount = t.TrackCount,
        DiscNumber = t.DiscNumber,
        DiscCount = t.DiscCount,
        DurationSeconds = t.Duration.TotalSeconds,
        Bitrate = t.Bitrate,
        SampleRate = t.SampleRate,
        Channels = t.Channels,
        Codec = t.Codec,
        Extension = t.Extension,
        FileSizeBytes = t.FileSizeBytes,
        AlbumKey = t.AlbumKey,
        ArtistKey = t.ArtistKey,
        HasArtwork = !string.IsNullOrEmpty(t.ArtworkPath) && !RemoteSource.IsRemote(t.ArtworkPath!)
    };
}
