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

    /// <summary>
    /// Atomically approves a ReviewReady JobApplication: moves both sibling tasks to Done and the
    /// application to Approved, in one DB transaction. ExecuteUpdateAsync commits immediately per call
    /// (unlike the deferred-write SaveChangesAsync model), so an explicit transaction - not just two
    /// independent guarded updates - is what makes this all-or-nothing across the JobApplications and
    /// Tasks tables. The application-level guard (Id == applicationId && OwnerId == ownerId &&
    /// State == ReviewReady) carries both the ownership check and the race guard in the same WHERE
    /// clause: a losing, wrong-owner, or wrong-state caller's update matches zero rows and the whole
    /// transaction rolls back before Tasks is ever touched.
    /// </summary>
    Task<bool> TryApprovePairAsync(int applicationId, int ownerId, CancellationToken ct = default);

    /// <summary>
    /// Atomically rejects a ReviewReady JobApplication: returns both sibling tasks to Todo (clearing
    /// ClaimedBy so they're reclaimable for rework) and the application back to Building, in one
    /// transaction. Same guard shape as TryApprovePairAsync.
    /// </summary>
    Task<bool> TryRejectPairAsync(int applicationId, int ownerId, CancellationToken ct = default);

    /// <summary>
    /// Bulk repair sweep: promotes every JobApplication not already Approved where both required
    /// sibling tasks are already Done, in one guarded UPDATE. Exists to heal applications left stuck
    /// below Approved by the individual per-task Approve/Reject/UpdateStatus endpoints before they
    /// gained their pair-invariant guard (TaskService's RequiresPairApproval check) - those endpoints
    /// could previously move one Epic-3 sibling to Done without the JobApplication-level promotion
    /// TryApprovePairAsync performs, permanently hiding the Board's export-download controls for
    /// real, already-generated content. <see cref="TaskFlow.Api.Agents.JobApplicationApprovalReconcilerService"/>
    /// calls this periodically, mirroring PromotePendingReviewReadyApplicationsAsync's shape and
    /// purpose one stage later in the pipeline. Returns the number of applications promoted.
    /// </summary>
    Task<int> PromotePendingApprovedApplicationsAsync(CancellationToken ct = default);

    Task AddAsync(JobApplication application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
