namespace Muselly.Core.Models;

/// <summary>
/// A server-side user account. The password is never stored in the clear: only a PBKDF2 hash and the
/// per-user salt are kept (both base64). Serialised to <c>server-users.json</c> in the config directory.
/// </summary>
public sealed class ServerUser
{
    public required string Username { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>Base64 PBKDF2-SHA256 hash of the password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 per-user salt fed into the PBKDF2 derivation.</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>PBKDF2 iteration count used to derive <see cref="PasswordHash"/>.</summary>
    public int Iterations { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
