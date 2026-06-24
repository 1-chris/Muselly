using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Server-side account store: persists <see cref="ServerUser"/> records (password hashes only) and validates
/// credentials. Implemented in Muselly.Server; consumed by the server host and the desktop admin UI.
/// </summary>
public interface IServerUserStore
{
    IReadOnlyList<ServerUser> Users { get; }

    event EventHandler? Changed;

    /// <summary>Returns the user when the password is correct, otherwise null.</summary>
    ServerUser? Validate(string username, string password);

    ServerUser? Find(string username);

    /// <summary>Creates the user, or updates the password/role of an existing one.</summary>
    void AddOrUpdate(string username, string password, UserRole role);

    void SetRole(string username, UserRole role);

    void SetPassword(string username, string password);

    void Remove(string username);
}
