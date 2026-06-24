namespace Muselly.Core.Models;

/// <summary>
/// The access level a connecting client has on a server. Higher values grant strictly more capability:
/// <see cref="Guest"/> and <see cref="User"/> can browse and play; <see cref="Admin"/> can additionally
/// manage folders, trigger rescans, change server settings and manage users.
/// </summary>
public enum UserRole
{
    Guest = 0,
    User = 1,
    Admin = 2
}
