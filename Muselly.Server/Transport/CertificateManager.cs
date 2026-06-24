using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Muselly.Server.Transport;

/// <summary>
/// Generates and persists the server's self-signed TLS certificate. We use an ECDSA P-256 key and a long
/// validity (the certificate identity is verified by fingerprint pinning, not a CA chain), exported to a PFX
/// in the config directory so the fingerprint is stable across restarts.
/// </summary>
public static class CertificateManager
{
    private const string Password = "muselly"; // protects only the on-disk PFX at rest; identity is the key/fingerprint.

    // macOS does not support EphemeralKeySet (keys cannot live purely in memory); keep keys exportable and
    // let the platform persist them temporarily as needed. Exportable works for SslStream on all platforms.
    private const X509KeyStorageFlags StorageFlags = X509KeyStorageFlags.Exportable;

    /// <summary>Loads the certificate at <paramref name="pfxPath"/>, creating and saving one if absent.</summary>
    public static X509Certificate2 LoadOrCreate(string pfxPath)
    {
        if (File.Exists(pfxPath))
        {
            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, Password, StorageFlags);
            }
            catch
            {
                // Corrupt/incompatible — regenerate below.
            }
        }

        var cert = Create();
        try
        {
            var bytes = cert.Export(X509ContentType.Pfx, Password);
            File.WriteAllBytes(pfxPath, bytes);
        }
        catch { /* best-effort persistence; the in-memory cert still works for this run */ }

        // Reload from the exported bytes so the private key is in a form SslStream accepts on all platforms.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, Password), Password, StorageFlags);
    }

    private static X509Certificate2 Create()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Muselly Server", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") /* server auth */ }, false));

        var now = DateTimeOffset.UtcNow.AddDays(-1);
        return request.CreateSelfSigned(now, now.AddYears(50));
    }
}
