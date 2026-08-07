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

    public async Task AddAsync(JobApplication application, CancellationToken ct = default) =>
        await _db.JobApplications.AddAsync(application, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
