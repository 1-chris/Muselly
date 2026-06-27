using System;
using Muselly.Core.Models;

namespace Muselly.Web.Services;

/// <summary>
/// Holds the browser client's authentication state: the session token issued by the host on login and the
/// role granted. Shared (singleton) so every service — the HTTP client, the playback service, the admin
/// surface — sees the same logged-in identity. The "host id" namespaces remote resource URIs.
/// </summary>
public sealed class WebSession
{
    /// <summary>Stable id used as the prefix for this host's <c>muselly://</c> resource URIs.</summary>
    public const string HostId = "host";

    public string? Token { get; private set; }
    public UserRole Role { get; private set; } = UserRole.Guest;
    public string ServerName { get; set; } = "Muselly";

    /// <summary>The username this session signed in as ("guest" for guest sessions).</summary>
    public string Username { get; private set; } = "guest";

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);
    public bool IsAdmin => IsAuthenticated && Role == UserRole.Admin;

    /// <summary>Raised when the authentication state changes (sign-in or sign-out).</summary>
    public event EventHandler? Changed;

    public void SignIn(string token, UserRole role, string username)
    {
        Token = token;
        Role = role;
        Username = string.IsNullOrWhiteSpace(username) ? "guest" : username;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        Token = null;
        Role = UserRole.Guest;
        Username = "guest";
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
