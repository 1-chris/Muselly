using System.Net.Security;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Util;
using Muselly.Server.Auth;
using Muselly.Server.Protocol;

namespace Muselly.Server.ServerHost;

/// <summary>
/// Handles one connected client over an established TLS stream: reads request envelopes, dispatches them to
/// handlers (concurrently, so a long track stream doesn't block art/lyrics requests), and enforces roles.
/// </summary>
public sealed class ServerSession
{
    private readonly ProtocolConnection _connection;
    private readonly ServerContext _ctx;
    private readonly CancellationToken _ct;
    private Session? _session;

    public ServerSession(SslStream stream, ServerContext ctx, CancellationToken ct)
    {
        _connection = new ProtocolConnection(stream);
        _ctx = ctx;
        _ct = ct;
    }

    public async Task RunAsync()
    {
        try
        {
            while (!_ct.IsCancellationRequested)
            {
                var frame = await _connection.ReadFrameAsync(_ct).ConfigureAwait(false);
                if (frame is null) break; // client disconnected
                if (frame.Value.Kind != FrameKind.Json || frame.Value.Message is not { } env) continue;
                if (env.Kind != EnvelopeKind.Request) continue;

                // Fire-and-forget per request; the write lock serialises frames so streams interleave safely.
                _ = Task.Run(() => DispatchAsync(env), _ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ProtocolException ex) { _ctx.Logger.LogDebug(ex, "Protocol error; closing session."); }
        catch (Exception ex) { _ctx.Logger.LogDebug(ex, "Session ended with error."); }
        finally
        {
            _connection.Dispose();
        }
    }

    private async Task DispatchAsync(Envelope env)
    {
        try
        {
            // Login logs itself (once the username/role is known); the rest log here, before handling.
            if (env.Type != MessageType.Login) LogActivity(env);

            switch (env.Type)
            {
                case MessageType.Hello: await HandleHelloAsync(env); break;
                case MessageType.Login: await HandleLoginAsync(env); break;

                case MessageType.GetLibrary: await RequireAuth(env, _ => HandleGetLibraryAsync(env)); break;
                case MessageType.GetAlbumArt: await RequireAuth(env, _ => HandleGetArtAsync(env, RemoteSource.KindAlbumArt)); break;
                case MessageType.GetArtistImage: await RequireAuth(env, _ => HandleGetArtAsync(env, RemoteSource.KindArtistImage)); break;
                case MessageType.GetArtistBio: await RequireAuth(env, _ => HandleGetBioAsync(env)); break;
                case MessageType.GetLyrics: await RequireAuth(env, _ => HandleGetLyricsAsync(env)); break;
                case MessageType.StreamTrack: await RequireAuth(env, _ => HandleStreamTrackAsync(env)); break;

                case MessageType.AddFolder: await RequireAdmin(env, () => HandleAddFolderAsync(env)); break;
                case MessageType.RemoveFolder: await RequireAdmin(env, () => HandleRemoveFolderAsync(env)); break;
                case MessageType.Rescan: await RequireAdmin(env, () => HandleRescanAsync(env)); break;
                case MessageType.GetServerSettings: await RequireAdmin(env, () => HandleGetServerSettingsAsync(env)); break;
                case MessageType.UpdateServerSettings: await RequireAdmin(env, () => HandleUpdateServerSettingsAsync(env)); break;
                case MessageType.ListUsers: await RequireAdmin(env, () => HandleListUsersAsync(env)); break;
                case MessageType.AddUser: await RequireAdmin(env, () => HandleAddUserAsync(env)); break;
                case MessageType.RemoveUser: await RequireAdmin(env, () => HandleRemoveUserAsync(env)); break;
                case MessageType.SetUserRole: await RequireAdmin(env, () => HandleAddUserAsync(env, roleOnly: true)); break;

                default: await SendErrorAsync(env.Id, $"Unknown request type '{env.Type}'."); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _ctx.Logger.LogDebug(ex, "Handler for {Type} failed.", env.Type);
            await SendErrorAsync(env.Id, "Internal server error.");
        }
    }

    // --- Handshake / auth ----------------------------------------------------------------------------

    private Task HandleHelloAsync(Envelope env)
    {
        var s = _ctx.ServerSettings();
        return SendResponseAsync(env.Id, MessageType.Hello, new HelloResponse
        {
            ServerName = string.IsNullOrWhiteSpace(s.ServerName) ? Environment.MachineName : s.ServerName,
            AppVersion = AppVersionString,
            GuestEnabled = s.GuestEnabled,
            LibraryEtag = _ctx.LibraryEtag()
        });
    }

    private async Task HandleLoginAsync(Envelope env)
    {
        var req = env.GetPayload<LoginRequest>() ?? new LoginRequest();
        var s = _ctx.ServerSettings();

        if (req.Guest)
        {
            if (!s.GuestEnabled)
            {
                await SendResponseAsync(env.Id, MessageType.Login,
                    new LoginResponse { Success = false, Error = "Guest access is disabled." });
                return;
            }
            _session = _ctx.Sessions.Create("guest", UserRole.Guest);
        }
        else
        {
            var user = _ctx.Users.Validate(req.Username, req.Password);
            if (user is null)
            {
                await SendResponseAsync(env.Id, MessageType.Login,
                    new LoginResponse { Success = false, Error = "Invalid username or password." });
                return;
            }
            _session = _ctx.Sessions.Create(user.Username, user.Role);
        }

        _ctx.ActivityLog?.Invoke($"{_session.Username} signed in ({_session.Role})");

        await SendResponseAsync(env.Id, MessageType.Login, new LoginResponse
        {
            Success = true,
            Role = _session.Role,
            SessionToken = _session.Token
        });
    }

    /// <summary>Writes a concise activity line describing the request to the host's server log.</summary>
    private void LogActivity(Envelope env)
    {
        var log = _ctx.ActivityLog;
        if (log is null) return;

        var who = _session?.Username ?? "client";
        string? detail = env.Type switch
        {
            MessageType.Hello => null, // covered by the connect line; skip the noise
            MessageType.GetLibrary => "requested the library",
            MessageType.GetAlbumArt => "requested album art",
            MessageType.GetArtistImage => "requested an artist image",
            MessageType.GetArtistBio => "requested an artist biography",
            MessageType.GetLyrics => "requested lyrics",
            MessageType.StreamTrack => $"streamed {DescribeTrack(env)}",
            MessageType.AddFolder => "added a music folder",
            MessageType.RemoveFolder => "removed a music folder",
            MessageType.Rescan => "triggered a library rescan",
            MessageType.GetServerSettings => "viewed server settings",
            MessageType.UpdateServerSettings => "updated server settings",
            MessageType.ListUsers => "listed users",
            MessageType.AddUser => "added or updated a user",
            MessageType.RemoveUser => "removed a user",
            MessageType.SetUserRole => "changed a user's role",
            _ => null
        };

        if (detail is not null) log($"{who} {detail}");
    }

    private string DescribeTrack(Envelope env)
    {
        var req = env.GetPayload<StreamTrackRequest>();
        var track = req is null ? null : _ctx.Library.FindTrack(req.TrackId);
        if (track is null) return "a track";
        var artist = string.IsNullOrWhiteSpace(track.Artist) ? track.DisplayArtist : track.Artist!;
        return $"\"{track.Title}\" by {artist}";
    }

    private async Task RequireAuth(Envelope env, Func<Session, Task> handler)
    {
        if (_session is null) { await SendErrorAsync(env.Id, "Not authenticated."); return; }
        await handler(_session);
    }

    private async Task RequireAdmin(Envelope env, Func<Task> handler)
    {
        if (_session is null) { await SendErrorAsync(env.Id, "Not authenticated."); return; }
        if (_session.Role != UserRole.Admin) { await SendErrorAsync(env.Id, "Administrator role required."); return; }
        await handler();
    }

    // --- Library -------------------------------------------------------------------------------------

    private Task HandleGetLibraryAsync(Envelope env)
    {
        var req = env.GetPayload<GetLibraryRequest>() ?? new GetLibraryRequest();
        var etag = _ctx.LibraryEtag();
        if (!string.IsNullOrEmpty(req.KnownEtag) && req.KnownEtag == etag)
            return SendResponseAsync(env.Id, MessageType.GetLibrary, new GetLibraryResponse { Etag = etag, Unchanged = true });

        var response = new GetLibraryResponse { Etag = etag };
        foreach (var t in _ctx.Library.Tracks)
        {
            // Only share this server's own (local) tracks, never tracks merged in from elsewhere.
            if (RemoteSource.IsRemote(t.Source)) continue;
            response.Tracks.Add(ToDto(t));
        }
        return SendResponseAsync(env.Id, MessageType.GetLibrary, response);
    }

    private async Task HandleGetArtAsync(Envelope env, string kind)
    {
        var req = env.GetPayload<KeyRequest>() ?? new KeyRequest();
        string? path = kind == RemoteSource.KindAlbumArt
            ? _ctx.Library.FindAlbum(req.Key)?.ArtworkPath
            : _ctx.Library.FindArtist(req.Key)?.ArtworkPath;

        await SendFileOrEmptyAsync(env.Id, path, "image/jpeg");
    }

    private async Task HandleGetBioAsync(Envelope env)
    {
        var req = env.GetPayload<KeyRequest>() ?? new KeyRequest();
        string? bio = null;
        if (_ctx.ArtistInfo is { } info)
        {
            var artist = _ctx.Library.FindArtist(req.Key);
            if (artist is not null)
            {
                var data = await _ctx.ArtistInfo.EnsureAsync(artist.Key, artist.Name, _ct).ConfigureAwait(false);
                bio = data?.Biography;
            }
        }
        await SendResponseAsync(env.Id, MessageType.GetArtistBio, new TextResponse { Text = bio });
    }

    private async Task HandleGetLyricsAsync(Envelope env)
    {
        var req = env.GetPayload<KeyRequest>() ?? new KeyRequest();
        string? text = null;
        var track = _ctx.Library.FindTrack(req.Key);
        if (track is not null && _ctx.Lyrics is { } lyrics)
        {
            var doc = await lyrics.GetAsync(track, _ct).ConfigureAwait(false);
            text = BuildLrc(doc);
        }
        await SendResponseAsync(env.Id, MessageType.GetLyrics, new TextResponse { Text = text });
    }

    private async Task HandleStreamTrackAsync(Envelope env)
    {
        var req = env.GetPayload<StreamTrackRequest>() ?? new StreamTrackRequest();
        var track = _ctx.Library.FindTrack(req.TrackId);
        if (track is null || RemoteSource.IsRemote(track.Source) || !File.Exists(track.Source))
        {
            await SendErrorAsync(env.Id, "Track not found.");
            return;
        }

        var bitrate = req.BitrateKbps > 0 ? req.BitrateKbps : _ctx.ServerSettings().OpusBitrateKbps;
        var opusPath = await _ctx.TranscodeCache.GetOrCreateAsync(track.Id, bitrate, track.Source, _ct).ConfigureAwait(false);
        if (opusPath is null || !File.Exists(opusPath))
        {
            await SendErrorAsync(env.Id, "Transcoding failed.");
            return;
        }

        await using var fs = new FileStream(opusPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await _connection.SendStreamAsync(env.Id, fs, "audio/ogg", fs.Length, _ct).ConfigureAwait(false);
    }

    // --- Admin ---------------------------------------------------------------------------------------

    private async Task HandleAddFolderAsync(Envelope env)
    {
        var req = env.GetPayload<FolderRequest>() ?? new FolderRequest();
        if (!string.IsNullOrWhiteSpace(req.Path))
            _ctx.Settings.AddMusicFolder(req.Path);
        await SendResponseAsync(env.Id, MessageType.AddFolder, new OkResponse());
    }

    private async Task HandleRemoveFolderAsync(Envelope env)
    {
        var req = env.GetPayload<FolderRequest>() ?? new FolderRequest();
        if (!string.IsNullOrWhiteSpace(req.Path))
            _ctx.Settings.RemoveMusicFolder(req.Path);
        await SendResponseAsync(env.Id, MessageType.RemoveFolder, new OkResponse());
    }

    private async Task HandleRescanAsync(Envelope env)
    {
        _ = _ctx.Rescan(_ct); // run in background; don't hold the response
        await SendResponseAsync(env.Id, MessageType.Rescan, new OkResponse());
    }

    private Task HandleGetServerSettingsAsync(Envelope env)
    {
        var s = _ctx.ServerSettings();
        return SendResponseAsync(env.Id, MessageType.GetServerSettings, new ServerSettingsDto
        {
            ServerName = s.ServerName,
            Port = s.Port,
            OpusBitrateKbps = s.OpusBitrateKbps,
            TranscodeCacheMaxBytes = s.TranscodeCacheMaxBytes,
            GuestEnabled = s.GuestEnabled,
            UpnpEnabled = s.UpnpEnabled,
            MusicFolders = _ctx.Settings.Current.MusicFolders.ToList()
        });
    }

    private async Task HandleUpdateServerSettingsAsync(Envelope env)
    {
        var dto = env.GetPayload<ServerSettingsDto>();
        if (dto is not null)
        {
            await _ctx.UpdateSettings(() =>
            {
                var s = _ctx.ServerSettings();
                s.ServerName = dto.ServerName;
                s.OpusBitrateKbps = dto.OpusBitrateKbps;
                s.TranscodeCacheMaxBytes = dto.TranscodeCacheMaxBytes;
                s.GuestEnabled = dto.GuestEnabled;
                s.UpnpEnabled = dto.UpnpEnabled;
            }).ConfigureAwait(false);
        }
        await SendResponseAsync(env.Id, MessageType.UpdateServerSettings, new OkResponse());
    }

    private Task HandleListUsersAsync(Envelope env)
    {
        var list = new UserListResponse
        {
            Users = _ctx.Users.Users.Select(u => new UserDto { Username = u.Username, Role = u.Role }).ToList()
        };
        return SendResponseAsync(env.Id, MessageType.ListUsers, list);
    }

    private async Task HandleAddUserAsync(Envelope env, bool roleOnly = false)
    {
        var req = env.GetPayload<AddUserRequest>() ?? new AddUserRequest();
        if (string.IsNullOrWhiteSpace(req.Username))
        {
            await SendResponseAsync(env.Id, env.Type, new OkResponse { Ok = false, Error = "Username required." });
            return;
        }

        if (roleOnly)
            _ctx.Users.SetRole(req.Username, req.Role);
        else
            _ctx.Users.AddOrUpdate(req.Username, req.Password, req.Role);

        await SendResponseAsync(env.Id, env.Type, new OkResponse());
    }

    private async Task HandleRemoveUserAsync(Envelope env)
    {
        var req = env.GetPayload<AddUserRequest>() ?? new AddUserRequest();
        if (!string.IsNullOrWhiteSpace(req.Username))
            _ctx.Users.Remove(req.Username);
        await SendResponseAsync(env.Id, MessageType.RemoveUser, new OkResponse());
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private async Task SendFileOrEmptyAsync(int id, string? path, string contentType)
    {
        if (string.IsNullOrEmpty(path) || RemoteSource.IsRemote(path) || !File.Exists(path))
        {
            await _connection.SendStreamAsync(id, Stream.Null, contentType, 0, _ct).ConfigureAwait(false);
            return;
        }
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        await _connection.SendStreamAsync(id, fs, contentType, fs.Length, _ct).ConfigureAwait(false);
    }

    private Task SendResponseAsync<T>(int id, string type, T payload) =>
        _connection.SendMessageAsync(new Envelope
        {
            Id = id,
            Kind = EnvelopeKind.Response,
            Type = type,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)
        }, _ct);

    private Task SendErrorAsync(int id, string error) =>
        _connection.SendMessageAsync(new Envelope { Id = id, Kind = EnvelopeKind.Error, Error = error }, _ct);

    private static string AppVersionString =>
        typeof(ServerSession).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>Serialises a resolved lyrics document back to LRC text (timestamped when synced) for the client.</summary>
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
