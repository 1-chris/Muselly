using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;
using Muselly.Server.Auth;
using Muselly.Server.Transcoding;

namespace Muselly.Server.ServerHost;

/// <summary>
/// Shared, read-mostly state handed to every per-connection session: the local library, settings, user
/// store, session manager and the transcode pipeline. Keeping this in one object makes a future headless
/// host trivial to construct.
/// </summary>
public sealed class ServerContext
{
    public required ILogger Logger { get; init; }
    public required ILibraryService Library { get; init; }
    public required ISettingsService Settings { get; init; }
    public required IServerUserStore Users { get; init; }
    public required IUserService UserService { get; init; }
    public required IUserDataStore UserData { get; init; }
    public required SessionManager Sessions { get; init; }
    public required TranscodeCache TranscodeCache { get; init; }
    public required Func<ServerSettings> ServerSettings { get; init; }
    public required Func<string> LibraryEtag { get; init; }

    /// <summary>Optional lyrics resolver (present when the host is wired with one).</summary>
    public ILyricsService? Lyrics { get; init; }

    /// <summary>Optional artist-info resolver (for bios/images).</summary>
    public IArtistInfoService? ArtistInfo { get; init; }

    /// <summary>Records a human-readable activity line (connections, requests, commands) for the server log.</summary>
    public Action<string>? ActivityLog { get; init; }

    /// <summary>Triggers a library rescan (admin action).</summary>
    public required Func<CancellationToken, Task> Rescan { get; init; }

    /// <summary>Applies an admin settings change (server + folder list).</summary>
    public required Func<Action, Task> UpdateSettings { get; init; }
}
