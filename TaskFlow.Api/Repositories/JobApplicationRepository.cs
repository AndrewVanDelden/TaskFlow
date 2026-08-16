using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>EF Core implementation of <see cref="IJobApplicationRepository"/>.</summary>
public class JobApplicationRepository : IJobApplicationRepository
{
    private readonly AppDbContext _db;
    public JobApplicationRepository(AppDbContext db) => _db = db;

    // Shared by TryPromoteToReviewReadyAsync and PromotePendingReviewReadyApplicationsAsync so the
    // "what counts as done" definition lives in exactly one place. Two correlated Any(kind, Review)
    // subqueries, not a bare Count(Review) == 2 — a count alone would be satisfied by two Review
    // tasks of the same kind, which isn't reachable today (JobApplicationAssemblyService always
    // creates exactly one of each kind) but the guard shouldn't rely on that being the only way an
    // application is ever built (PR #43 review: Copilot's automated review). Confirmed against
    // SQLite that EF Core 10 translates the original Count(...) == 2 form to a single UPDATE with a
    // correlated subquery, not client evaluation — see Sprint 3R notes; Any(predicate) is the same
    // class of translation and equally standard, but this specific two-Any query has not been
    // independently re-verified with query logging the same way.
    private static readonly Expression<Func<JobApplication, bool>> BothRequiredSiblingsAreReview = a =>
        a.Tasks.Any(t => t.Kind == TaskKind.ResumeTailoring && t.Status == WorkflowStatus.Review)
        && a.Tasks.Any(t => t.Kind == TaskKind.CoverLetterTailoring && t.Status == WorkflowStatus.Review);

    // AsNoTracking: this repository never mutates a fetched JobApplication and calls
    // SaveChangesAsync on it - every write goes through a guarded ExecuteUpdateAsync, which
    // bypasses the change tracker entirely. Without AsNoTracking, a caller that reads before and
    // after one of those guarded updates (JobApplicationService.ApproveAsync/RejectAsync does
    // exactly this) would get back the first call's already-tracked, now-stale instance from EF's
    // identity map instead of the committed row (found via a real HTTP-level integration test,
    // then reproduced directly against SqliteInMemoryContext - see
    // JobApplicationRepositoryApprovalTests.GetByIdAsync_reflects_TryApprovePairAsync_without_a_manual_ChangeTracker_Clear).
    public async Task<JobApplication?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _db.JobApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<bool> TryPromoteToReviewReadyAsync(int applicationId, CancellationToken ct = default)
    {
        // Guarded UPDATE (Building -> ReviewReady). The State == Building guard is what makes a
        // second near-simultaneous caller's UPDATE match zero rows once the first has flipped the
        // row: no separate SELECT to race against.
        var promoted = await _db.JobApplications
            .Where(a => a.Id == applicationId && a.State == ApplicationState.Building)
            .Where(BothRequiredSiblingsAreReview)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.ReviewReady), ct);

        return promoted == 1;
    }

    public async Task<int> PromotePendingReviewReadyApplicationsAsync(CancellationToken ct = default) =>
        await _db.JobApplications
            .Where(a => a.State == ApplicationState.Building)
            .Where(BothRequiredSiblingsAreReview)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.ReviewReady), ct);

    // ReviewReady can only ever be set by TryPromoteToReviewReadyAsync/
    // PromotePendingReviewReadyApplicationsAsync, both of which already require both required
    // sibling kinds to be Review - so at the moment ReviewReady is set, this invariant holds by
    // construction. But nothing prevents a sibling being moved independently afterward: the
    // existing, unrestricted PATCH /api/Tasks/{id}/status endpoint lets any authenticated user move
    // any task to any status with no awareness of the pair flow. So by the time
    // TryApprovePairAsync/TryRejectPairAsync actually runs, the invariant is not guaranteed to
    // still hold - the Tasks-side affected-row count must be checked and rolled back on, not
    // assumed (PR #45 review: Copilot's automated review, confirmed reachable via that endpoint).
    private const int RequiredSiblingCount = 2;

    public async Task<bool> TryApprovePairAsync(int applicationId, int ownerId, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var approved = await _db.JobApplications
            .Where(a => a.Id == applicationId && a.OwnerId == ownerId && a.State == ApplicationState.ReviewReady)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.Approved), ct);

        if (approved != 1)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var movedTasks = await _db.Tasks
            .Where(t => t.ApplicationId == applicationId && t.Status == WorkflowStatus.Review)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, WorkflowStatus.Done)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

        if (movedTasks != RequiredSiblingCount)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> TryRejectPairAsync(int applicationId, int ownerId, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var rejected = await _db.JobApplications
            .Where(a => a.Id == applicationId && a.OwnerId == ownerId && a.State == ApplicationState.ReviewReady)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.Building), ct);

        if (rejected != 1)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var movedTasks = await _db.Tasks
            .Where(t => t.ApplicationId == applicationId && t.Status == WorkflowStatus.Review)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, WorkflowStatus.Todo)
                .SetProperty(t => t.ClaimedBy, (string?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

        if (movedTasks != RequiredSiblingCount)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    // Shared by PromotePendingApprovedApplicationsAsync only (no per-application TryPromote... form
    // exists here, unlike BothRequiredSiblingsAreReview above - the per-application promotion at
    // this stage is TryApprovePairAsync, which requires ReviewReady and moves the tasks itself; this
    // predicate is purely for the bulk repair sweep, matched against tasks already Done).
    private static readonly Expression<Func<JobApplication, bool>> BothRequiredSiblingsAreDone = a =>
        a.Tasks.Any(t => t.Kind == TaskKind.ResumeTailoring && t.Status == WorkflowStatus.Done)
        && a.Tasks.Any(t => t.Kind == TaskKind.CoverLetterTailoring && t.Status == WorkflowStatus.Done);

    public async Task<int> PromotePendingApprovedApplicationsAsync(CancellationToken ct = default) =>
        await _db.JobApplications
            .Where(a => a.State != ApplicationState.Approved)
            .Where(BothRequiredSiblingsAreDone)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.Approved), ct);

    public async Task AddAsync(JobApplication application, CancellationToken ct = default) =>
        await _db.JobApplications.AddAsync(application, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
