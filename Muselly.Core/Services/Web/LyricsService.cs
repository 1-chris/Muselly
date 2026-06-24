using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;
using Muselly.Core.Util;

namespace Muselly.Core.Services.Web;

/// <summary>Resolves lyrics for a track from the file's tags, the local cache, then the web.</summary>
public interface ILyricsService
{
    /// <summary>
    /// Returns the best available lyrics for the track: embedded (synced preferred), then a cached LRC,
    /// then a web lookup (lrclib.net). Returns <see cref="LyricsDocument.Empty"/> when nothing is found.
    /// </summary>
    Task<LyricsDocument> GetAsync(Track track, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lyrics service. Reads embedded lyrics (synchronised or plain) from the audio file via ATL; if absent,
/// looks for a cached <c>{trackId}.lrc</c>; if still absent, queries each registered <see cref="ILyricsProvider"/>
/// in turn (all key-free) and caches whatever it finds. Synced lyrics are preferred at every step — and a
/// synced hit from any provider wins over a plain hit from an earlier one — so the panel can highlight the
/// current line whenever possible.
/// </summary>
public sealed class LyricsService : ILyricsService
{
    private readonly ILogger<LyricsService> _logger;
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly IRemoteServerManager? _remotes;

    public LyricsService(ILogger<LyricsService> logger, IEnumerable<ILyricsProvider> providers,
        IRemoteServerManager? remotes = null)
    {
        _logger = logger;
        _providers = providers is IReadOnlyList<ILyricsProvider> list ? list : new List<ILyricsProvider>(providers);
        _remotes = remotes;
    }

    public async Task<LyricsDocument> GetAsync(Track track, CancellationToken cancellationToken = default)
    {
        try
        {
            // Remote tracks: ask the owning server (it serves from its own cache/providers) before anything else.
            if (RemoteSource.IsRemote(track.Source) && _remotes is not null &&
                RemoteSource.TryParse(track.Source, out var serverId, out _, out var trackId))
            {
                var lrc = await _remotes.GetLyricsAsync(serverId, trackId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(lrc))
                {
                    var synced = LrcParser.LooksSynced(lrc);
                    return new LyricsDocument
                    {
                        Lines = LrcParser.Parse(lrc),
                        Origin = synced ? LyricsOrigin.WebSynced : LyricsOrigin.WebPlain,
                        SourceName = "server"
                    };
                }
            }

            var embedded = ReadEmbedded(track);
            if (embedded.HasLines) return embedded;

            var cached = ReadCache(track);
            if (cached.HasLines) return cached;

            return await FetchFromWebAsync(track, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Lyrics lookup failed for {Title}", track.Title);
            return LyricsDocument.Empty;
        }
    }

    private LyricsDocument ReadEmbedded(Track track)
    {
        try
        {
            if (!File.Exists(track.Source)) return LyricsDocument.Empty;
            var atl = new ATL.Track(track.Source);
            if (atl.Lyrics is null || atl.Lyrics.Count == 0) return LyricsDocument.Empty;

            foreach (var info in atl.Lyrics)
            {
                if (info.SynchronizedLyrics is { Count: > 0 })
                {
                    var lines = new List<LyricsLine>(info.SynchronizedLyrics.Count);
                    foreach (var phrase in info.SynchronizedLyrics)
                        lines.Add(new LyricsLine { Text = (phrase.Text ?? string.Empty).Trim(), TimeMs = phrase.TimestampStart });
                    lines.Sort((a, b) => Nullable.Compare(a.TimeMs, b.TimeMs));
                    return new LyricsDocument { Lines = lines, Origin = LyricsOrigin.EmbeddedSynced, SourceName = "file" };
                }
            }

            foreach (var info in atl.Lyrics)
            {
                if (!string.IsNullOrWhiteSpace(info.UnsynchronizedLyrics))
                {
                    var text = info.UnsynchronizedLyrics!;
                    if (LrcParser.LooksSynced(text))
                        return new LyricsDocument { Lines = LrcParser.Parse(text), Origin = LyricsOrigin.EmbeddedSynced, SourceName = "file" };
                    return new LyricsDocument { Lines = LrcParser.Parse(text), Origin = LyricsOrigin.EmbeddedPlain, SourceName = "file" };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reading embedded lyrics failed for {Source}", track.Source);
        }
        return LyricsDocument.Empty;
    }

    private LyricsDocument ReadCache(Track track)
    {
        try
        {
            var path = CachePath(track);
            if (!File.Exists(path)) return LyricsDocument.Empty;
            var text = File.ReadAllText(path);
            var synced = LrcParser.LooksSynced(text);
            return new LyricsDocument
            {
                Lines = LrcParser.Parse(text),
                Origin = synced ? LyricsOrigin.WebSynced : LyricsOrigin.WebPlain,
                SourceName = "cached"
            };
        }
        catch
        {
            return LyricsDocument.Empty;
        }
    }

    private async Task<LyricsDocument> FetchFromWebAsync(Track track, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(track.Title)) return LyricsDocument.Empty;

        // Try each provider; a synced hit wins immediately, otherwise keep the first plain hit as a fallback.
        (string Text, string Provider)? plainFallback = null;

        foreach (var provider in _providers)
        {
            ct.ThrowIfCancellationRequested();

            LyricsFetchResult? result;
            try
            {
                result = await provider.FetchAsync(track, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Lyrics provider {Provider} failed for {Title}", provider.Name, track.Title);
                continue;
            }

            if (result is null || !result.HasAny) continue;

            if (result.HasSynced)
            {
                SaveCache(track, result.SyncedLrc!);
                _logger.LogDebug("Lyrics for {Title} from {Provider} (synced)", track.Title, provider.Name);
                return new LyricsDocument
                {
                    Lines = LrcParser.Parse(result.SyncedLrc),
                    Origin = LyricsOrigin.WebSynced,
                    SourceName = provider.Name
                };
            }

            plainFallback ??= (result.PlainText!, provider.Name);
        }

        if (plainFallback is { } fallback)
        {
            SaveCache(track, fallback.Text);
            _logger.LogDebug("Lyrics for {Title} from {Provider} (plain)", track.Title, fallback.Provider);
            return new LyricsDocument
            {
                Lines = LrcParser.Parse(fallback.Text),
                Origin = LyricsOrigin.WebPlain,
                SourceName = fallback.Provider
            };
        }

        return LyricsDocument.Empty;
    }

    private void SaveCache(Track track, string text)
    {
        try { File.WriteAllText(CachePath(track), text); }
        catch (Exception ex) { _logger.LogDebug(ex, "Failed to cache lyrics for {Title}", track.Title); }
    }

    private static string CachePath(Track track) =>
        Path.Combine(StoragePaths.LyricsCacheDirectory(), track.Id + ".lrc");
}
