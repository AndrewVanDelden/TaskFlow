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

    /// <summary>
    /// Bulk sibling of <see cref="TryPromoteToReviewReadyAsync"/> with no id filter: promotes every
    /// JobApplication currently stuck at Building where both required siblings are already Review, in
    /// one guarded UPDATE. Exists because the per-application join attempted right after a save is a
    /// best-effort trigger, not a guarantee — if the log write or the join call itself is interrupted
    /// (PR #43 review, round 2: found independently by both a manual review and Copilot's automated
    /// review), nothing else retries it.
    /// <see cref="TaskFlow.Api.Agents.JobApplicationPromotionReconcilerService"/> calls this
    /// periodically to close that gap. Returns the number of applications promoted.
    /// </summary>
    Task<int> PromotePendingReviewReadyApplicationsAsync(CancellationToken ct = default);

    Task AddAsync(JobApplication application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
