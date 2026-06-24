using System.Security.Cryptography;
using Muselly.Core.Models;

namespace Muselly.Server.Auth;

/// <summary>PBKDF2-SHA256 password hashing for server accounts.</summary>
public static class PasswordHasher
{
    public const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string Hash, string Salt, int Iterations) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), Iterations);
    }

    public static bool Verify(string password, ServerUser user)
    {
        if (string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(user.Salt)) return false;
        try
        {
            var salt = Convert.FromBase64String(user.Salt);
            var expected = Convert.FromBase64String(user.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, user.Iterations <= 0 ? Iterations : user.Iterations,
                HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch
        {
            return false;
        }
    }
}
