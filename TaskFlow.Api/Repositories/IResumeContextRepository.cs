using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for resume contexts. The only code that queries resume contexts via EF Core.</summary>
public interface IResumeContextRepository
{
    Task<ResumeContext?> GetForOwnerAsync(string ingestionSessionId, int ownerId, CancellationToken ct = default);
    Task AddAsync(ResumeContext context, CancellationToken ct = default);
    Task<bool> DeleteForOwnerAsync(string ingestionSessionId, int ownerId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
