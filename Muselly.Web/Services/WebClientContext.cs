using Muselly.App.Services;

namespace Muselly.Web.Services;

/// <summary>
/// Browser client context: the web app connects to a host it doesn't own, so it can't manage that host's
/// server settings from the shared Connect page. Host administration is exposed through the admin overlay
/// (folders/rescans) instead, gated on the Admin role.
/// </summary>
public sealed class WebClientContext : IClientContext
{
    public bool CanManageHost => false;
}
