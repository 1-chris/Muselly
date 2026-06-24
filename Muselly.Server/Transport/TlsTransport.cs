using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Muselly.Server.Transport;

/// <summary>
/// Wraps raw TCP streams in TLS (1.3 where available, 1.2 as a floor for macOS). The server presents its
/// self-signed certificate; the client verifies it
/// purely by fingerprint (no CA chain), so every byte after the handshake is encrypted and authenticated.
/// </summary>
public static class TlsTransport
{
    /// <summary>Completes the server-side TLS handshake on an accepted connection.</summary>
    public static async Task<SslStream> AuthenticateServerAsync(Stream inner, X509Certificate2 certificate,
        CancellationToken ct = default)
    {
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false);
        var options = new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            // macOS's SecureTransport backend does not support TLS 1.3, so allow 1.2 as a floor
            // and let the handshake negotiate the highest version both peers support.
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ClientCertificateRequired = false,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };
        await ssl.AuthenticateAsServerAsync(options, ct).ConfigureAwait(false);
        return ssl;
    }

    /// <summary>
    /// Completes the client-side TLS handshake, capturing the server's fingerprint. When
    /// <paramref name="expectedFingerprint"/> is provided the handshake fails unless it matches (pinning);
    /// when null the certificate is accepted and its fingerprint reported via <paramref name="onFingerprint"/>.
    /// </summary>
    public static async Task<SslStream> AuthenticateClientAsync(Stream inner, string targetHost,
        string? expectedFingerprint, Action<string> onFingerprint, CancellationToken ct = default)
    {
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false, (_, cert, _, _) =>
        {
            if (cert is null) return false;
            var fp = Fingerprint.Of(cert);
            onFingerprint(fp);
            // First use (no pin yet): accept and let the caller decide to save it.
            return string.IsNullOrEmpty(expectedFingerprint) || Fingerprint.Equal(fp, expectedFingerprint);
        });

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };
        await ssl.AuthenticateAsClientAsync(options, ct).ConfigureAwait(false);
        return ssl;
    }
}
