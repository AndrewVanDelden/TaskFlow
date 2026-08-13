using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>EF Core implementation of <see cref="IResumeContextRepository"/>.</summary>
public class ResumeContextRepository : IResumeContextRepository
{
    private readonly AppDbContext _db;
    public ResumeContextRepository(AppDbContext db) => _db = db;

    // Ownership-scoped by construction: every read queries by BOTH the ingestion session id AND
    // the owner id together, so a caller who supplies the right session id but the wrong owner id
    // gets null — never the data, never a distinguishable error revealing the session id exists.
    public async Task<ResumeContext?> GetForOwnerAsync(string ingestionSessionId, int ownerId, CancellationToken ct = default) =>
        await _db.ResumeContexts.FirstOrDefaultAsync(
            r => r.IngestionSessionId == ingestionSessionId && r.OwnerId == ownerId, ct);

    // ownerId here is never a client-supplied value in the real call path (the controller resolves
    // it from the JWT) — that's what makes this query ownership-safe despite having no session-id
    // dimension to pair it with, unlike GetForOwnerAsync above.
    public async Task<ResumeContext?> GetMostRecentForOwnerAsync(int ownerId, CancellationToken ct = default) =>
        await _db.ResumeContexts
            .Where(r => r.OwnerId == ownerId)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(ResumeContext context, CancellationToken ct = default) =>
        await _db.ResumeContexts.AddAsync(context, ct);

    // Same ownership-scoped query shape as GetForOwnerAsync — a wrong-owner delete attempt
    // matches zero rows and returns false, never touching another owner's data.
    public async Task<bool> DeleteForOwnerAsync(string ingestionSessionId, int ownerId, CancellationToken ct = default)
    {
        var deletedRows = await _db.ResumeContexts
            .Where(r => r.IngestionSessionId == ingestionSessionId && r.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);

        return deletedRows > 0;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
