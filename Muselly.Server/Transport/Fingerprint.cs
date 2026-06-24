using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Muselly.Server.Transport;

/// <summary>Computes and formats SHA-256 certificate fingerprints used for trust-on-first-use pinning.</summary>
public static class Fingerprint
{
    /// <summary>SHA-256 over the certificate's DER encoding, formatted as colon-separated uppercase hex.</summary>
    public static string Of(X509Certificate certificate)
    {
        var hash = SHA256.HashData(certificate.GetRawCertData());
        return Convert.ToHexString(hash) is var hex
            ? string.Join(':', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)))
            : string.Empty;
    }

    /// <summary>Constant-time, case/format-insensitive comparison of two fingerprints.</summary>
    public static bool Equal(string a, string b)
    {
        static string Norm(string s) => s.Replace(":", string.Empty).Trim().ToUpperInvariant();
        var na = Norm(a);
        var nb = Norm(b);
        if (na.Length != nb.Length || na.Length == 0) return false;
        var diff = 0;
        for (var i = 0; i < na.Length; i++) diff |= na[i] ^ nb[i];
        return diff == 0;
    }
}
