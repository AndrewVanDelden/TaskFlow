using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>EF Core implementation of <see cref="IJobApplicationRepository"/>.</summary>
public class JobApplicationRepository : IJobApplicationRepository
{
    private readonly AppDbContext _db;
    public JobApplicationRepository(AppDbContext db) => _db = db;

    public async Task<JobApplication?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _db.JobApplications.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<bool> TryPromoteToReviewReadyAsync(int applicationId, CancellationToken ct = default)
    {
        // Guarded UPDATE (Building -> ReviewReady) whose WHERE clause carries the sibling check as
        // two correlated subqueries (Any(kind, Review) for each required kind), not a bare
        // Count(Review) == 2 — a count alone would be satisfied by two Review tasks of the same
        // kind, which isn't reachable today (JobApplicationAssemblyService always creates exactly
        // one of each kind) but the guard shouldn't rely on that being the only way an application
        // is ever built (PR #43 review: Copilot's automated review). Confirmed against SQLite that
        // EF Core 10 translates the original Count(...) == 2 form to a single UPDATE with a
        // correlated subquery, not client evaluation — see Sprint 3R notes; Any(predicate) is the
        // same class of translation and equally standard, but this specific two-Any query has not
        // been independently re-verified with query logging the same way. The State == Building
        // guard is what makes a second near-simultaneous caller's UPDATE match zero rows once the
        // first has flipped the row: no separate SELECT to race against.
        var promoted = await _db.JobApplications
            .Where(a => a.Id == applicationId
                     && a.State == ApplicationState.Building
                     && a.Tasks.Any(t => t.Kind == TaskKind.ResumeTailoring && t.Status == WorkflowStatus.Review)
                     && a.Tasks.Any(t => t.Kind == TaskKind.CoverLetterTailoring && t.Status == WorkflowStatus.Review))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.ReviewReady), ct);

        return promoted == 1;
    }

    public async Task AddAsync(JobApplication application, CancellationToken ct = default) =>
        await _db.JobApplications.AddAsync(application, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
