using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for resume contexts. The only code that queries resume contexts via EF Core.</summary>
public interface IResumeContextRepository
{
    Task<ResumeContext?> GetForOwnerAsync(string ingestionSessionId, int ownerId, CancellationToken ct = default);

    /// <summary>
    /// Sprint 6: the caller's own most recently saved resume, from any session — no session-id
    /// dimension exists for this query shape, so ownership scoping rests entirely on ownerId.
    /// </summary>
    Task<ResumeContext?> GetMostRecentForOwnerAsync(int ownerId, CancellationToken ct = default);

    Task AddAsync(ResumeContext context, CancellationToken ct = default);
    Task<bool> DeleteForOwnerAsync(string ingestionSessionId, int ownerId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
