using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for job applications. The only code that queries job applications via EF Core.</summary>
public interface IJobApplicationRepository
{
    Task<JobApplication?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Atomically promotes a JobApplication from Building to ReviewReady, but only when both sibling
    /// tasks are already in Review. A single guarded UPDATE with the sibling-status check baked into the
    /// WHERE clause (a correlated subquery over the Tasks navigation), not a separate SELECT-then-UPDATE
    /// — this is what makes concurrent callers (one per sibling agent finishing near-simultaneously)
    /// unable to double-promote or race past a lost update: only one caller's guarded update can ever
    /// affect a row. Returns true only for the caller whose update actually flips the row.
    /// </summary>
    Task<bool> TryPromoteToReviewReadyAsync(int applicationId, CancellationToken ct = default);

    Task AddAsync(JobApplication application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
