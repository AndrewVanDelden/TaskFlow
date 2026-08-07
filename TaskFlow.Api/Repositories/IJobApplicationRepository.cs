using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for job applications. The only code that queries job applications via EF Core.</summary>
public interface IJobApplicationRepository
{
    Task<JobApplication?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(JobApplication application, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
