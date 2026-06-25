using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.Services;

/// <summary>The outcome of creating (or reusing) a share link.</summary>
public sealed record ShareCreateResult(string Url, string Token);

/// <summary>
/// Creates guest share links for library items and builds the URL a recipient opens in a browser. Reuses an
/// existing, unexpired link for the same item so repeated "Copy share link" clicks don't pile up. Only the
/// host (desktop/headless) can mint links; the browser head can't (it has no local share store).
/// </summary>
public interface IShareLinkService
{
    /// <summary>True where the current client may create share links (host owner, or a signed-in User/Admin).</summary>
    bool CanShare { get; }

    /// <summary>Creates or reuses a link to an item and returns its shareable URL, or null if unavailable.</summary>
    Task<ShareCreateResult?> CreateAsync(ShareKind kind, string key, string label);
}

public sealed class ShareLinkService : IShareLinkService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    private readonly IShareService _shares;
    private readonly IServerHost _host;
    private readonly IClientContext _clientContext;

    public ShareLinkService(IShareService shares, IServerHost host, IClientContext clientContext)
    {
        _shares = shares;
        _host = host;
        _clientContext = clientContext;
    }

    public bool CanShare => _clientContext.CanManageHost;

    public Task<ShareCreateResult?> CreateAsync(ShareKind kind, string key, string label)
    {
        if (!CanShare || string.IsNullOrEmpty(key)) return Task.FromResult<ShareCreateResult?>(null);

        var existing = _shares.List().FirstOrDefault(s => s.Kind == kind && s.Key == key && !s.IsExpired);
        var link = existing ?? _shares.Create(kind, key, label, DefaultLifetime);
        return Task.FromResult<ShareCreateResult?>(new ShareCreateResult(BuildUrl(link.Id), link.Id));
    }

    private string BuildUrl(string token) => BuildShareUrl(_host.Settings, token, DetectLocalIp());

    /// <summary>
    /// Builds the URL a recipient opens for a share token. Uses the configured external base URL when set,
    /// otherwise derives one from the web ports and the supplied host (a detected LAN IP).
    /// </summary>
    public static string BuildShareUrl(ServerSettings settings, string token, string fallbackHost)
    {
        var external = settings.WebExternalUrl?.Trim();
        if (!string.IsNullOrEmpty(external))
        {
            if (!external.EndsWith('/')) external += "/";
            return external + "?share=" + token;
        }

        var host = string.IsNullOrEmpty(fallbackHost) ? "localhost" : fallbackHost;
        return settings.WebHttpsPort > 0
            ? $"https://{host}:{settings.WebHttpsPort}/?share={token}"
            : $"http://{host}:{(settings.WebHttpPort > 0 ? settings.WebHttpPort : 80)}/?share={token}";
    }

    private static string DetectLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "localhost";
        }
        catch
        {
            return "localhost";
        }
    }
}

/// <summary>
/// Holds the current top-level clipboard so view models can copy text without a visual reference. The shell
/// view registers it once it's attached to the visual tree.
/// </summary>
public static class AppClipboard
{
    public static IClipboard? Current { get; set; }

    public static async Task<bool> SetTextAsync(string text)
    {
        if (Current is null) return false;
        try { await Current.SetTextAsync(text); return true; }
        catch { return false; }
    }
}
