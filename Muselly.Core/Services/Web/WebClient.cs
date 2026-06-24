using System.Net.Http.Json;
using System.Text.Json;

namespace Muselly.Core.Services.Web;

/// <summary>
/// A tiny shared HTTP helper for the metadata-enrichment services (album art, artist info, lyrics). It
/// owns one pooled <see cref="HttpClient"/> with a descriptive User-Agent (required by MusicBrainz and the
/// Wikimedia APIs) and exposes resilient "never throw" helpers that return <c>null</c> on any failure so
/// callers can treat the network as best-effort. Only public no-key web sources are used.
/// </summary>
public static class WebClient
{
    /// <summary>A descriptive UA, per MusicBrainz/Wikimedia etiquette. Update the contact if forked.</summary>
    public const string UserAgent = "Muselly/1.0 (https://github.com/muselly; music player)";

    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>GETs a URL and deserialises the JSON body to <typeparamref name="T"/>; null on any error.</summary>
    public static async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct = default) =>
        await GetJsonAsync<T>(url, null, ct).ConfigureAwait(false);

    /// <summary>As <see cref="GetJsonAsync{T}(string,CancellationToken)"/>, with extra request headers.</summary>
    public static async Task<T?> GetJsonAsync<T>(string url,
        IReadOnlyList<KeyValuePair<string, string>>? headers, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, headers);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>GETs a URL and returns the raw string body; null on any error or non-success status.</summary>
    public static async Task<string?> GetStringAsync(string url, CancellationToken ct = default) =>
        await GetStringAsync(url, null, ct).ConfigureAwait(false);

    /// <summary>As <see cref="GetStringAsync(string,CancellationToken)"/>, with extra request headers.</summary>
    public static async Task<string?> GetStringAsync(string url,
        IReadOnlyList<KeyValuePair<string, string>>? headers, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(request, headers);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyList<KeyValuePair<string, string>>? headers)
    {
        if (headers is null) return;
        foreach (var h in headers)
            request.Headers.TryAddWithoutValidation(h.Key, h.Value);
    }

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationPath"/> (atomically via a temp file).
    /// Returns the saved path on success, otherwise null. Existing files are left untouched.
    /// </summary>
    public static async Task<string?> DownloadFileAsync(string url, string destinationPath, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(destinationPath)) return destinationPath;

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            var tmp = destinationPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            if (File.Exists(destinationPath)) File.Delete(tmp);
            else File.Move(tmp, destinationPath);
            return destinationPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>URL-encodes a query-string component.</summary>
    public static string Encode(string value) => Uri.EscapeDataString(value);
}
