using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Fetches a job posting URL's response body over HTTP, applying the ingestion sprint's
/// SSRF-safe redirect handling (Epic 3.2 Sprint 1, mitigation 7). Abstracted so callers can be
/// unit-tested against a fake without a real network call.
/// </summary>
public interface IJobPostingUrlFetcher
{
    Task<Result<string>> FetchAsync(Uri uri, CancellationToken cancellationToken = default);
}
