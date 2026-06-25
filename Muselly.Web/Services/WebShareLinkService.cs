using System;
using System.Threading.Tasks;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Web.Audio;

namespace Muselly.Web.Services;

/// <summary>
/// Browser implementation of <see cref="IShareLinkService"/>. Any signed-in User or Admin can mint share
/// links via the host API; guests (including share-link guests) cannot. The link URL is built from the page
/// origin the visitor is already using.
/// </summary>
public sealed class WebShareLinkService : IShareLinkService
{
    private const int DefaultLifetimeDays = 7;

    private readonly WebHostClient _client;
    private readonly WebSession _session;

    public WebShareLinkService(WebHostClient client, WebSession session)
    {
        _client = client;
        _session = session;
    }

    public bool CanShare => _session.IsAuthenticated && _session.Role != UserRole.Guest;

    public async Task<ShareCreateResult?> CreateAsync(ShareKind kind, string key, string label)
    {
        if (!CanShare || string.IsNullOrEmpty(key)) return null;

        var dto = await _client.CreateShareAsync(kind, key, label, DefaultLifetimeDays);
        if (dto is null || string.IsNullOrEmpty(dto.Id)) return null;

        var url = Interop.GetOrigin().TrimEnd('/') + "/?share=" + dto.Id;
        return new ShareCreateResult(url, dto.Id);
    }
}
