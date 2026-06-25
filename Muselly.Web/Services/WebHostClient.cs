using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Muselly.Core.Models;
using Muselly.Web.Audio;

namespace Muselly.Web.Services;

/// <summary>
/// Talks to the same-origin web host's JSON API. Holds the auth token (via <see cref="WebSession"/>) and adds
/// it as a Bearer header; the media/stream URLs instead carry the token as a query parameter, since browser
/// media elements can't set headers. This is the browser's equivalent of the desktop's remote-server client.
/// </summary>
public sealed class WebHostClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WebSession _session;
    private readonly HttpClient _http = new();
    private string? _origin;

    public WebHostClient(WebSession session) => _session = session;

    private string Origin => _origin ??= Interop.GetOrigin();

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Origin + path);
        if (!string.IsNullOrEmpty(_session.Token))
            req.Headers.Add("Authorization", "Bearer " + _session.Token);
        return req;
    }

    public async Task<HelloDto?> HelloAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Get, "/api/hello");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<HelloDto>(Json, ct) : null;
        }
        catch { return null; }
    }

    public async Task<LoginDto?> LoginAsync(string username, string password, bool guest, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Post, "/api/login");
            req.Content = JsonContent.Create(new { username, password, guest }, options: Json);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync<LoginDto>(Json, ct);
        }
        catch (Exception ex) { return new LoginDto { Success = false, Error = ex.Message }; }
    }

    public async Task<ShareLoginDto?> ShareLoginAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Post, "/api/share-login");
            req.Content = JsonContent.Create(new { token }, options: Json);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return await resp.Content.ReadFromJsonAsync<ShareLoginDto>(Json, ct);
        }
        catch (Exception ex) { return new ShareLoginDto { Success = false, Error = ex.Message }; }
    }

    public async Task<LibraryDto?> GetLibraryAsync(string? etag, CancellationToken ct = default)
    {
        try
        {
            var path = "/api/library" + (string.IsNullOrEmpty(etag) ? "" : "?etag=" + Uri.EscapeDataString(etag));
            using var req = Request(HttpMethod.Get, path);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<LibraryDto>(Json, ct) : null;
        }
        catch { return null; }
    }

    public async Task<byte[]?> GetBytesAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Get, path);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch { return null; }
    }

    public async Task<string?> GetTextAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Get, path);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var dto = await resp.Content.ReadFromJsonAsync<TextDto>(Json, ct);
            return dto?.Text;
        }
        catch { return null; }
    }

    public async Task<bool> PostAsync(string path, object? body, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Post, path);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(string path, object? body, CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Delete, path);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<ServerSettingsClientDto?> GetAdminSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Get, "/api/admin/settings");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ServerSettingsClientDto>(Json, ct) : null;
        }
        catch { return null; }
    }

    public Task<bool> AddFolderAsync(string path, CancellationToken ct = default) =>
        PostAsync("/api/admin/folders", new { path }, ct);

    public Task<bool> RemoveFolderAsync(string path, CancellationToken ct = default) =>
        DeleteAsync("/api/admin/folders?path=" + Uri.EscapeDataString(path), null, ct);

    public Task<bool> RescanAsync(string? folder = null, CancellationToken ct = default) =>
        PostAsync("/api/admin/rescan", new { path = folder ?? string.Empty }, ct);

    public async Task<ShareDto?> CreateShareAsync(ShareKind kind, string key, string label, int days,
        CancellationToken ct = default)
    {
        try
        {
            using var req = Request(HttpMethod.Post, "/api/shares");
            req.Content = JsonContent.Create(new { kind, key, label, days }, options: Json);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ShareDto>(Json, ct) : null;
        }
        catch { return null; }
    }

    /// <summary>An absolute, token-bearing URL the browser's audio element can stream Opus from.</summary>
    public string StreamUrl(string hostTrackId, int bitrate)
    {
        var token = Uri.EscapeDataString(_session.Token ?? string.Empty);
        var id = Uri.EscapeDataString(hostTrackId);
        return $"{Origin}/api/stream?id={id}&token={token}&bitrate={bitrate}";
    }
}
