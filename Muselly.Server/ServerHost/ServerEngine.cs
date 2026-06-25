using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Services.Web;
using Muselly.Core.Storage;
using Muselly.Server.Auth;
using Muselly.Server.Transcoding;

namespace Muselly.Server.ServerHost;

/// <summary>
/// The transport-agnostic core of the integrated server. Owns the shared, long-lived state — user store,
/// session manager, Opus transcode pipeline, persisted <see cref="ServerSettings"/>, the library "etag" and
/// the activity log — and the operations that act on them. Both the TLS <see cref="MusellyServerHost"/> and
/// the HTTP web host build on this, and a future headless host can reuse it verbatim: it has no UI and no
/// transport dependency.
/// </summary>
public sealed class ServerEngine
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ServerEngine> _logger;
    private readonly object _gate = new();

    private const int MaxLogEntries = 300;
    private readonly object _logGate = new();
    private readonly LinkedList<ServerLogEntry> _logs = new();

    private ServerSettings _config;
    private string _libraryEtag = Guid.NewGuid().ToString("N");

    public ServerEngine(
        ILibraryService library,
        ISettingsService settings,
        ILoggerFactory loggerFactory,
        IShareService shares,
        ILyricsService? lyrics = null,
        IArtistInfoService? artistInfo = null,
        IPlaylistService? playlists = null)
    {
        Library = library;
        AppSettingsService = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ServerEngine>();
        Shares = shares;
        Lyrics = lyrics;
        ArtistInfo = artistInfo;
        Playlists = playlists;

        _config = JsonStore.Load(StoragePaths.ServerConfigFile(), () => new ServerSettings());
        Users = new UserStore();
        Sessions = new SessionManager();
        Transcoder = new OpusTranscoder();
        TranscodeCache = new TranscodeCache(StoragePaths.TranscodeCacheDirectory(), Transcoder,
            loggerFactory.CreateLogger<TranscodeCache>());
        TranscodeCache.MaxBytes = _config.TranscodeCacheMaxBytes;

        Library.LibraryChanged += (_, _) => _libraryEtag = Guid.NewGuid().ToString("N");
    }

    public ILibraryService Library { get; }
    public ISettingsService AppSettingsService { get; }
    public UserStore Users { get; }
    public SessionManager Sessions { get; }
    public OpusTranscoder Transcoder { get; }
    public TranscodeCache TranscodeCache { get; }
    public ILyricsService? Lyrics { get; }
    public IArtistInfoService? ArtistInfo { get; }
    public IShareService Shares { get; }
    public IPlaylistService? Playlists { get; }

    public ServerSettings Config => _config;
    public string LibraryEtag => _libraryEtag;

    public ILogger Logger => _logger;

    /// <summary>Raised whenever a setting that callers care about changes (so the UI can refresh).</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised (possibly off the UI thread) when a new activity line is logged.</summary>
    public event EventHandler<ServerLogEntry>? Logged;

    public IReadOnlyList<ServerLogEntry> RecentLogs
    {
        get { lock (_logGate) return _logs.ToList(); }
    }

    /// <summary>Records an activity log line in the bounded ring buffer and notifies listeners.</summary>
    public void Log(string message)
    {
        var entry = new ServerLogEntry { Message = message };
        lock (_logGate)
        {
            _logs.AddLast(entry);
            while (_logs.Count > MaxLogEntries) _logs.RemoveFirst();
        }
        Logged?.Invoke(this, entry);
    }

    /// <summary>Builds a per-connection context for the framed TLS protocol sessions.</summary>
    public ServerContext BuildContext() => new()
    {
        Logger = _logger,
        Library = Library,
        Settings = AppSettingsService,
        Users = Users,
        Sessions = Sessions,
        TranscodeCache = TranscodeCache,
        ServerSettings = () => _config,
        LibraryEtag = () => _libraryEtag,
        Lyrics = Lyrics,
        ArtistInfo = ArtistInfo,
        ActivityLog = Log,
        Rescan = ct => Library.ScanAsync(ct),
        UpdateSettings = async mutate =>
        {
            mutate();
            TranscodeCache.MaxBytes = _config.TranscodeCacheMaxBytes;
            Persist();
            RaiseSettingsChanged();
            await Task.CompletedTask;
        }
    };

    /// <summary>Mutates and persists the server settings, keeping derived state (cache cap) in sync.</summary>
    public void UpdateConfig(Action<ServerSettings> mutate)
    {
        lock (_gate)
        {
            mutate(_config);
            _config.OpusBitrateKbps = OpusTranscoder.ClampBitrate(_config.OpusBitrateKbps);
            TranscodeCache.MaxBytes = _config.TranscodeCacheMaxBytes;
            Persist();
        }
        RaiseSettingsChanged();
    }

    public void Persist() => JsonStore.Save(StoragePaths.ServerConfigFile(), _config);

    public void RaiseSettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);
}
