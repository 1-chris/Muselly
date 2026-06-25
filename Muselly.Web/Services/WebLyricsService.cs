using System;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Core.Services.Web;
using Muselly.Core.Util;

namespace Muselly.Web.Services;

/// <summary>
/// Browser <see cref="ILyricsService"/> that defers entirely to the host. The desktop server resolves lyrics
/// (embedded → its cache → public providers) and caches them, so the browser never calls third-party lyric
/// APIs itself (which would be cross-origin and uncached).
/// </summary>
public sealed class WebLyricsService : ILyricsService
{
    private readonly WebHostClient _client;

    public WebLyricsService(WebHostClient client) => _client = client;

    public async Task<LyricsDocument> GetAsync(Track track, CancellationToken cancellationToken = default)
    {
        var id = RemoteSource.TryParse(track.Source, out _, out _, out var key) ? key : track.Id;
        var text = await _client.GetTextAsync("/api/lyrics?id=" + Uri.EscapeDataString(id), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return LyricsDocument.Empty;

        var synced = LrcParser.LooksSynced(text);
        return new LyricsDocument
        {
            Lines = LrcParser.Parse(text),
            Origin = synced ? LyricsOrigin.WebSynced : LyricsOrigin.WebPlain,
            SourceName = "server"
        };
    }
}
