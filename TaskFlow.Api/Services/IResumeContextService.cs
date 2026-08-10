namespace TaskFlow.Api.Services;

using TaskFlow.Api.Common;

/// <summary>
/// Captures a user's base resume server-side for one ingestion session (never localStorage — a
/// server-side agent cannot read browser storage). Thin wrapper over IResumeContextRepository that
/// applies the shared save-content guardrail before anything touches storage.
/// </summary>
public interface IResumeContextService
{
    Task<Result<bool>> SaveAsync(string ingestionSessionId, int ownerId, string content, string? contentFormat, CancellationToken ct = default);
}
