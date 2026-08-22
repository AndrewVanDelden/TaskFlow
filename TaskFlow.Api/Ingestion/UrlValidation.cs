using System.Net;
using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// SSRF-safe URL validation (Epic 3.2 security design, mitigations 1-5): scheme allowlist, no
/// embedded credentials, port allowlist, hostname denylist, IP-literal denylist. Pure and
/// side-effect-free so it is independently unit-testable and reusable by the later connect-time
/// (DNS-rebinding) check without either caller needing to construct the other's input shape.
/// </summary>
public static class UrlValidation
{
    private static readonly string[] DenylistedDnsHostnameSuffixes = [".local", ".internal", ".test", ".localhost"];

    private static readonly IPNetwork[] DenylistedIpv4Ranges =
    [
        IPNetwork.Parse("169.254.0.0/16"), // link-local - includes cloud metadata (169.254.169.254)
        IPNetwork.Parse("10.0.0.0/8"),     // RFC1918 private
        IPNetwork.Parse("172.16.0.0/12"),  // RFC1918 private
        IPNetwork.Parse("192.168.0.0/16"), // RFC1918 private
        IPNetwork.Parse("224.0.0.0/4"),    // multicast
    ];

    private static readonly IPNetwork[] DenylistedIpv6Ranges =
    [
        IPNetwork.Parse("fe80::/10"), // link-local
        IPNetwork.Parse("fc00::/7"),  // unique-local
    ];

    public static Result<Uri> Validate(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Result<Uri>.Invalid($"URL scheme '{uri.Scheme}' is not allowed; only http and https are permitted.");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return Result<Uri>.Invalid("URL must not contain embedded credentials.");

        if (!uri.IsDefaultPort)
            return Result<Uri>.Invalid($"URL port {uri.Port} is not allowed; only the scheme's default port is permitted.");

        return uri.HostNameType switch
        {
            UriHostNameType.Dns => ValidateDnsHost(uri),
            UriHostNameType.IPv4 or UriHostNameType.IPv6 => ValidateIpLiteralHost(uri),
            _ => Result<Uri>.Invalid($"URL host '{uri.Host}' could not be validated."),
        };
    }

    /// <summary>
    /// Callable independently of <see cref="Validate"/> so the later connect-time (DNS-rebinding)
    /// check can validate a resolved <see cref="IPAddress"/> directly, without constructing a
    /// <see cref="Uri"/> just to reuse this logic.
    /// </summary>
    public static bool IsDenylistedIpAddress(IPAddress address)
    {
        // PR #63 review finding: an IPv4-mapped IPv6 literal (RFC 4291, e.g. ::ffff:169.254.169.254)
        // has AddressFamily.InterNetworkV6, so without this unwrap it took the IPv6-range branch
        // below and was never checked against the IPv4 ranges (cloud metadata, RFC1918, etc.) at
        // all - a complete denylist bypass under a different notation for the same address.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        if (address.IsIPv6Multicast) return true;

        IPNetwork[] denylistedRanges = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? DenylistedIpv4Ranges
            : DenylistedIpv6Ranges;

        foreach (IPNetwork network in denylistedRanges)
        {
            if (network.Contains(address)) return true;
        }

        return false;
    }

    private static Result<Uri> ValidateDnsHost(Uri uri)
        => IsDenylistedDnsHostname(uri.Host) switch
        {
            true => Result<Uri>.Invalid($"Hostname '{uri.Host}' is not allowed."),
            false => Result<Uri>.Ok(uri),
        };

    private static bool IsDenylistedDnsHostname(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!host.Contains('.')) return true;

        foreach (string suffix in DenylistedDnsHostnameSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static Result<Uri> ValidateIpLiteralHost(Uri uri)
    {
        // Uri.Host is expected to already exclude the [ ] brackets .NET requires when parsing an
        // IPv6 literal out of a URL string, but that is stripped defensively here rather than
        // assumed, since IPAddress.Parse rejects a bracketed literal.
        IPAddress address = IPAddress.Parse(uri.Host.Trim('[', ']'));

        return IsDenylistedIpAddress(address) switch
        {
            true => Result<Uri>.Invalid($"IP address '{address}' is not allowed."),
            false => Result<Uri>.Ok(uri),
        };
    }
}
