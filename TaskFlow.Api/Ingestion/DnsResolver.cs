using System.Net;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Production <see cref="IDnsResolver"/>: a thin pass-through to the BCL <see cref="Dns"/> API.
/// </summary>
public sealed class DnsResolver : IDnsResolver
{
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default)
        => await Dns.GetHostAddressesAsync(host, cancellationToken);
}
