using System.Collections.Concurrent;
using System.Security.Cryptography;
using Muselly.Core.Models;

namespace Muselly.Server.Auth;

/// <summary>A logged-in session: the resolved username and the role it was granted.</summary>
public sealed record Session(string Token, string Username, UserRole Role);

/// <summary>Issues and validates opaque session tokens for the lifetime of the server process.</summary>
public sealed class SessionManager
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public Session Create(string username, UserRole role)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new Session(token, username, role);
        _sessions[token] = session;
        return session;
    }

    public Session? Get(string? token) =>
        token is not null && _sessions.TryGetValue(token, out var s) ? s : null;

    public void Revoke(string token) => _sessions.TryRemove(token, out _);
}
