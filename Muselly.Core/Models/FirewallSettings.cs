namespace Muselly.Core.Models;

/// <summary>How the firewall treats connecting clients.</summary>
public enum FirewallMode
{
    /// <summary>The firewall is off: every client is allowed.</summary>
    Off = 0,

    /// <summary>Only clients whose address matches a rule are allowed; everyone else is rejected.</summary>
    Allow = 1,

    /// <summary>Clients whose address matches a rule are rejected; everyone else is allowed.</summary>
    Block = 2
}

/// <summary>Which server(s) a firewall rule applies to.</summary>
public enum FirewallScope
{
    /// <summary>Applies to both the built-in (desktop-to-desktop) server and the web server.</summary>
    Both = 0,

    /// <summary>Applies only to the built-in TLS server.</summary>
    Server = 1,

    /// <summary>Applies only to the web server.</summary>
    Web = 2
}

/// <summary>
/// A single firewall rule: a textual address spec plus the server(s) it applies to. The spec accepts a
/// single IPv4 address (<c>1.1.1.1</c>), a CIDR block (<c>1.1.1.1/16</c>) or an inclusive range
/// (<c>1.1.0.0-1.1.255.255</c>).
/// </summary>
public sealed class FirewallRule
{
    public string Value { get; set; } = string.Empty;

    public FirewallScope Scope { get; set; } = FirewallScope.Both;
}

/// <summary>
/// Optional IP firewall for the servers. In <see cref="FirewallMode.Allow"/> only addresses matching a rule
/// (for the relevant scope) may connect; in <see cref="FirewallMode.Block"/> matching addresses are refused.
/// Persisted as part of <see cref="ServerSettings"/>.
/// </summary>
public sealed class FirewallSettings
{
    public FirewallMode Mode { get; set; } = FirewallMode.Off;

    public List<FirewallRule> Rules { get; set; } = new();
}
