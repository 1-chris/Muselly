namespace Muselly.App.Services;

/// <summary>
/// Describes the capabilities of the current client so shared UI can adapt. The desktop/headless heads can
/// manage the local host (server settings, users, firewall, etc.); the browser head cannot (those controls
/// act on the host it connects to, surfaced through the admin API instead), so it hides those sections.
/// </summary>
public interface IClientContext
{
    /// <summary>True when this client owns/manages the local host (desktop &amp; headless), false in the browser.</summary>
    bool CanManageHost { get; }
}

/// <summary>Default context for heads that run the engine in-process (desktop, headless).</summary>
public sealed class LocalClientContext : IClientContext
{
    public bool CanManageHost => true;
}
