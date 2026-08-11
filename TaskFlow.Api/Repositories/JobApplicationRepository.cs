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
        // Guarded UPDATE (Building -> ReviewReady) whose WHERE clause carries the sibling check as a
        // correlated subquery (Count(Review) == 2). Confirmed against SQLite that EF Core 10 translates
        // this to a single UPDATE with a correlated subquery, not client evaluation — see Sprint 3R
        // notes. The State == Building guard is what makes a second near-simultaneous caller's UPDATE
        // match zero rows once the first has flipped the row: no separate SELECT to race against.
        var promoted = await _db.JobApplications
            .Where(a => a.Id == applicationId
                     && a.State == ApplicationState.Building
                     && a.Tasks.Count(t => t.Status == WorkflowStatus.Review) == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ApplicationState.ReviewReady), ct);

        return promoted == 1;
    }

    public async Task AddAsync(JobApplication application, CancellationToken ct = default) =>
        await _db.JobApplications.AddAsync(application, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
