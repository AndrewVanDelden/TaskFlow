using System.Net;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Abstraction over DNS resolution so the connect-time SSRF check (mitigation 6) can be unit-tested
/// with a fake resolver instead of depending on real network lookups.
/// </summary>
public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default);
}
