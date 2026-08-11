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

    /// <summary>
    /// Sprint 4R: reads a caller's base resume back for a given JobApplication, resolving the
    /// application's (IngestionSessionId, OwnerId) first. Same status (NotFound) whether the
    /// application doesn't exist, isn't owned by the caller, or has no ResumeContext saved yet -
    /// this project's IDOR-safe convention.
    /// </summary>
    Task<Result<string>> GetForApplicationAsync(int applicationId, int callerId, CancellationToken ct = default);
}
