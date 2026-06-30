using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Core.Services.Implementation;

/// <summary>
/// App-level configuration for scrobbling services. Last.fm validates the API key/secret, so to enable it you
/// must register a free API account (https://www.last.fm/api/account/create) and set these. Libre.fm (GNU FM)
/// doesn't validate the key, so any non-empty value works; ListenBrainz needs no app key at all.
/// </summary>
public static class ScrobbleConfig
{
    public static string LastFmApiKey { get; set; } = "";
    public static string LastFmApiSecret { get; set; } = "";

    public static string LibreFmApiKey { get; set; } = "muselly";
    public static string LibreFmApiSecret { get; set; } = "muselly";
}

/// <summary>
/// Default <see cref="IScrobbleService"/>. Speaks the AudioScrobbler 2.0 API (Last.fm + Libre.fm, with MD5
/// request signing and session keys) and the ListenBrainz submission API (bearer token). Accounts are stored
/// per user; submissions are best-effort (a failed scrobble is retried once).
/// </summary>
public sealed class ScrobblerService : IScrobbleService
{
    private readonly ILogger<ScrobblerService> _logger;
    private readonly HttpClient _http = new();
    private readonly object _gate = new();
    // username -> (provider -> account)
    private readonly Dictionary<string, Dictionary<ScrobbleProvider, ScrobbleAccount>> _byUser = new(StringComparer.OrdinalIgnoreCase);

    private const string LastFmEndpoint = "https://ws.audioscrobbler.com/2.0/";
    private const string LibreFmEndpoint = "https://libre.fm/2.0/";
    private const string ListenBrainzEndpoint = "https://api.listenbrainz.org";

    public ScrobblerService(ILogger<ScrobblerService> logger) => _logger = logger;

    public event EventHandler? Changed;

    public IReadOnlyList<ScrobbleAccount> GetAccounts(string username)
    {
        lock (_gate)
            return _byUser.TryGetValue(username, out var m) ? m.Values.ToList() : new List<ScrobbleAccount>();
    }

    public bool IsProviderAvailable(ScrobbleProvider provider) => provider switch
    {
        ScrobbleProvider.LastFm => !string.IsNullOrEmpty(ScrobbleConfig.LastFmApiKey) &&
                                   !string.IsNullOrEmpty(ScrobbleConfig.LastFmApiSecret),
        _ => true
    };

    public async Task<ScrobbleConnectResult> ConnectAsync(string username, ScrobbleProvider provider,
        string secret1, string? secret2 = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return ScrobbleConnectResult.Fail("No user.");
        if (!IsProviderAvailable(provider)) return ScrobbleConnectResult.Fail("This service isn't configured in this build.");

        try
        {
            ScrobbleAccount account;
            if (provider == ScrobbleProvider.ListenBrainz)
            {
                var token = secret1.Trim();
                var name = await ValidateListenBrainzAsync(token, ct).ConfigureAwait(false);
                if (name is null) return ScrobbleConnectResult.Fail("That ListenBrainz token wasn't accepted.");
                account = new ScrobbleAccount { Provider = provider, Credential = token, AccountName = name };
            }
            else
            {
                // Last.fm / Libre.fm: exchange username + password for a permanent session key over HTTPS.
                var (key, secret, endpoint) = Endpoint(provider);
                var session = await GetMobileSessionAsync(endpoint, key, secret, secret1.Trim(), secret2 ?? "", ct)
                    .ConfigureAwait(false);
                if (session is null) return ScrobbleConnectResult.Fail("Sign-in failed — check your username and password.");
                account = new ScrobbleAccount { Provider = provider, Credential = session.Value.Key, AccountName = session.Value.Name };
            }

            lock (_gate)
            {
                if (!_byUser.TryGetValue(username, out var m)) _byUser[username] = m = new();
                m[provider] = account;
            }
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
            return ScrobbleConnectResult.Ok(account.AccountName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Scrobble connect failed for {Provider}", provider);
            return ScrobbleConnectResult.Fail("Couldn't reach the service. Check your connection and try again.");
        }
    }

    public void Disconnect(string username, ScrobbleProvider provider)
    {
        lock (_gate)
        {
            if (_byUser.TryGetValue(username, out var m) && m.Remove(provider)) { }
            else return;
        }
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetEnabled(string username, ScrobbleProvider provider, bool enabled)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(username, out var m) || !m.TryGetValue(provider, out var acc)) return;
            if (acc.Enabled == enabled) return;
            acc.Enabled = enabled;
        }
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateNowPlayingAsync(string username, Track track, CancellationToken ct = default)
    {
        foreach (var acc in EnabledAccounts(username))
        {
            try { await SubmitAsync(acc, track, nowPlaying: true, startedAt: default, ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "Now-playing update failed for {Provider}", acc.Provider); }
        }
    }

    public async Task ScrobbleAsync(string username, Track track, DateTimeOffset startedAt, CancellationToken ct = default)
    {
        foreach (var acc in EnabledAccounts(username))
        {
            try
            {
                if (!await SubmitAsync(acc, track, nowPlaying: false, startedAt, ct).ConfigureAwait(false))
                {
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                    await SubmitAsync(acc, track, nowPlaying: false, startedAt, ct).ConfigureAwait(false); // one retry
                }
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Scrobble failed for {Provider}", acc.Provider); }
        }
    }

    public Task LoadAsync() => Task.Run(() =>
    {
        var file = JsonStore.Load(StoragePaths.ScrobbleAccountsFile(), () => new AccountsFile());
        lock (_gate)
        {
            _byUser.Clear();
            foreach (var (user, accounts) in file.Users)
            {
                var map = new Dictionary<ScrobbleProvider, ScrobbleAccount>();
                foreach (var a in accounts) map[a.Provider] = a;
                _byUser[user] = map;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    });

    private List<ScrobbleAccount> EnabledAccounts(string username)
    {
        lock (_gate)
            return _byUser.TryGetValue(username, out var m)
                ? m.Values.Where(a => a.Enabled).ToList()
                : new List<ScrobbleAccount>();
    }

    // --- Provider protocols --------------------------------------------------------------------------

    private static (string Key, string Secret, string Endpoint) Endpoint(ScrobbleProvider provider) => provider switch
    {
        ScrobbleProvider.LibreFm => (ScrobbleConfig.LibreFmApiKey, ScrobbleConfig.LibreFmApiSecret, LibreFmEndpoint),
        _ => (ScrobbleConfig.LastFmApiKey, ScrobbleConfig.LastFmApiSecret, LastFmEndpoint)
    };

    private async Task<bool> SubmitAsync(ScrobbleAccount acc, Track track, bool nowPlaying,
        DateTimeOffset startedAt, CancellationToken ct)
    {
        var artist = string.IsNullOrWhiteSpace(track.Artist) ? track.DisplayArtist : track.Artist!;
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track.Title)) return true; // nothing to submit

        return acc.Provider == ScrobbleProvider.ListenBrainz
            ? await SubmitListenBrainzAsync(acc.Credential, track, artist, nowPlaying, startedAt, ct).ConfigureAwait(false)
            : await SubmitAudioScrobblerAsync(acc, track, artist, nowPlaying, startedAt, ct).ConfigureAwait(false);
    }

    // ----- AudioScrobbler 2.0 (Last.fm / Libre.fm) -----

    private async Task<(string Key, string Name)?> GetMobileSessionAsync(string endpoint, string apiKey,
        string apiSecret, string user, string password, CancellationToken ct)
    {
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getMobileSession",
            ["username"] = user,
            ["password"] = password,
            ["api_key"] = apiKey
        };
        p["api_sig"] = Sign(p, apiSecret);
        p["format"] = "json";

        using var resp = await _http.PostAsync(endpoint, new FormUrlEncodedContent(p), ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("session", out var session)) return null;
        var key = session.TryGetProperty("key", out var k) ? k.GetString() : null;
        var name = session.TryGetProperty("name", out var n) ? n.GetString() : user;
        return key is null ? null : (key, name ?? user);
    }

    private async Task<bool> SubmitAudioScrobblerAsync(ScrobbleAccount acc, Track track, string artist,
        bool nowPlaying, DateTimeOffset startedAt, CancellationToken ct)
    {
        var (apiKey, apiSecret, endpoint) = Endpoint(acc.Provider);
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = nowPlaying ? "track.updateNowPlaying" : "track.scrobble",
            ["artist"] = artist,
            ["track"] = track.Title,
            ["api_key"] = apiKey,
            ["sk"] = acc.Credential
        };
        if (!string.IsNullOrWhiteSpace(track.Album)) p["album"] = track.Album!;
        if (!nowPlaying) p["timestamp"] = startedAt.ToUnixTimeSeconds().ToString();
        p["api_sig"] = Sign(p, apiSecret);
        p["format"] = "json";

        using var resp = await _http.PostAsync(endpoint, new FormUrlEncodedContent(p), ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>api_sig = md5( concat(sorted "name"+"value" for all params except format/callback) + secret ).</summary>
    private static string Sign(SortedDictionary<string, string> p, string secret)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in p)
        {
            if (k is "format" or "callback") continue;
            sb.Append(k).Append(v);
        }
        sb.Append(secret);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    // ----- ListenBrainz -----

    private async Task<string?> ValidateListenBrainzAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ListenBrainzEndpoint + "/1/validate-token");
        req.Headers.TryAddWithoutValidation("Authorization", "Token " + token);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True)
            return root.TryGetProperty("user_name", out var u) ? u.GetString() ?? "ListenBrainz" : "ListenBrainz";
        return null;
    }

    private async Task<bool> SubmitListenBrainzAsync(string token, Track track, string artist,
        bool nowPlaying, DateTimeOffset startedAt, CancellationToken ct)
    {
        var metadata = new Dictionary<string, object> { ["artist_name"] = artist, ["track_name"] = track.Title };
        if (!string.IsNullOrWhiteSpace(track.Album)) metadata["release_name"] = track.Album!;

        var listen = new Dictionary<string, object> { ["track_metadata"] = metadata };
        if (!nowPlaying) listen["listened_at"] = startedAt.ToUnixTimeSeconds();

        var body = new Dictionary<string, object>
        {
            ["listen_type"] = nowPlaying ? "playing_now" : "single",
            ["payload"] = new[] { listen }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ListenBrainzEndpoint + "/1/submit-listens")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Token " + token);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    // --- Persistence ---------------------------------------------------------------------------------

    private void Persist()
    {
        AccountsFile file;
        lock (_gate)
        {
            file = new AccountsFile();
            foreach (var (user, m) in _byUser)
                file.Users[user] = m.Values.ToList();
        }
        _ = Task.Run(() =>
        {
            try { JsonStore.Save(StoragePaths.ScrobbleAccountsFile(), file); }
            catch { /* best-effort */ }
        });
    }

    private sealed class AccountsFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, List<ScrobbleAccount>> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
