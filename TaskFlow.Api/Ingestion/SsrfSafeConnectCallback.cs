using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// DNS-rebinding-safe connect-time IP validation (Epic 3.2 security design, mitigation 6): resolves
/// the hostname itself and connects to the specific validated address, so the address that gets
/// checked is guaranteed to be the address that gets connected to.
/// </summary>
public sealed class SsrfSafeConnectCallback
{
    private readonly IDnsResolver _resolver;

    public SsrfSafeConnectCallback(IDnsResolver resolver) => _resolver = resolver;

    public ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        => ConnectAsync(context.DnsEndPoint, cancellationToken);

    public async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        IPAddress[] resolvedAddresses = await _resolver.ResolveAsync(endpoint.Host, cancellationToken);
        IPAddress? safeAddress = Array.Find(resolvedAddresses, address => !UrlValidation.IsDenylistedIpAddress(address));

        if (safeAddress is null)
            throw new HttpRequestException($"Host '{endpoint.Host}' has no safe resolved address to connect to.");

        Socket socket = new(safeAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(safeAddress, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
