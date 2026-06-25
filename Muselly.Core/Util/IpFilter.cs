using System.Net;
using System.Net.Sockets;
using Muselly.Core.Models;

namespace Muselly.Core.Util;

/// <summary>
/// Parses and evaluates firewall address specs. Three forms are supported (IPv4 and IPv6):
/// a single address (<c>1.1.1.1</c>), a CIDR block (<c>1.1.1.1/16</c>) and an inclusive range
/// (<c>1.1.0.0-1.1.255.255</c>). Used both to validate user input and to decide whether a connecting
/// client is permitted under the configured <see cref="FirewallSettings"/>.
/// </summary>
public static class IpFilter
{
    /// <summary>
    /// Decides whether <paramref name="address"/> may connect to the given <paramref name="scope"/> under
    /// <paramref name="settings"/>. Returns true (allow) when the firewall is off or settings are null.
    /// </summary>
    public static bool IsAllowed(IPAddress? address, FirewallSettings? settings, FirewallScope scope)
    {
        if (settings is null || settings.Mode == FirewallMode.Off) return true;
        if (address is null) return settings.Mode != FirewallMode.Allow;

        // Normalise IPv4-mapped IPv6 (e.g. ::ffff:1.2.3.4) to plain IPv4 so v4 rules match loopback/LAN.
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        var matched = false;
        foreach (var rule in settings.Rules)
        {
            if (rule.Scope != FirewallScope.Both && rule.Scope != scope) continue;
            if (TryParse(rule.Value, out var matcher) && matcher!.Matches(address))
            {
                matched = true;
                break;
            }
        }

        return settings.Mode == FirewallMode.Allow ? matched : !matched;
    }

    /// <summary>True if <paramref name="spec"/> is a syntactically valid address / CIDR / range.</summary>
    public static bool IsValid(string? spec) => TryParse(spec, out _);

    /// <summary>Parses an address spec into a reusable matcher. Returns false for malformed input.</summary>
    public static bool TryParse(string? spec, out Matcher? matcher)
    {
        matcher = null;
        if (string.IsNullOrWhiteSpace(spec)) return false;
        spec = spec.Trim();

        // Range: "a - b"
        var dash = spec.IndexOf('-');
        if (dash > 0)
        {
            var lo = spec[..dash].Trim();
            var hi = spec[(dash + 1)..].Trim();
            if (!IPAddress.TryParse(lo, out var a) || !IPAddress.TryParse(hi, out var b)) return false;
            if (a.AddressFamily != b.AddressFamily) return false;
            var loBytes = a.GetAddressBytes();
            var hiBytes = b.GetAddressBytes();
            if (Compare(loBytes, hiBytes) > 0) (loBytes, hiBytes) = (hiBytes, loBytes);
            matcher = new Matcher(loBytes, hiBytes);
            return true;
        }

        // CIDR: "addr/prefix"
        var slash = spec.IndexOf('/');
        if (slash > 0)
        {
            var addrPart = spec[..slash].Trim();
            var prefixPart = spec[(slash + 1)..].Trim();
            if (!IPAddress.TryParse(addrPart, out var addr)) return false;
            if (!int.TryParse(prefixPart, out var prefix)) return false;
            var bits = addr.GetAddressBytes().Length * 8;
            if (prefix < 0 || prefix > bits) return false;
            var (lo, hi) = CidrBounds(addr.GetAddressBytes(), prefix);
            matcher = new Matcher(lo, hi);
            return true;
        }

        // Single address.
        if (!IPAddress.TryParse(spec, out var single)) return false;
        var bytes = single.GetAddressBytes();
        matcher = new Matcher(bytes, (byte[])bytes.Clone());
        return true;
    }

    private static (byte[] Lo, byte[] Hi) CidrBounds(byte[] addr, int prefix)
    {
        var lo = (byte[])addr.Clone();
        var hi = (byte[])addr.Clone();
        for (var i = 0; i < addr.Length; i++)
        {
            int maskBits = prefix - i * 8;
            byte mask = maskBits >= 8 ? (byte)0xFF : maskBits <= 0 ? (byte)0x00 : (byte)(0xFF << (8 - maskBits));
            lo[i] = (byte)(addr[i] & mask);
            hi[i] = (byte)(addr[i] | (byte)~mask);
        }
        return (lo, hi);
    }

    private static int Compare(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }
        return 0;
    }

    /// <summary>A compiled address matcher: an inclusive byte-wise range covering one family (v4 or v6).</summary>
    public sealed class Matcher
    {
        private readonly byte[] _lo;
        private readonly byte[] _hi;

        internal Matcher(byte[] lo, byte[] hi)
        {
            _lo = lo;
            _hi = hi;
        }

        public bool Matches(IPAddress address)
        {
            var addr = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            var bytes = addr.GetAddressBytes();
            if (bytes.Length != _lo.Length) return false; // different family
            return Compare(_lo, bytes) <= 0 && Compare(bytes, _hi) <= 0;
        }
    }
}
