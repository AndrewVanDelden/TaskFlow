using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Security;

namespace TaskFlow.Api.Services;

/// <summary>EF-backed implementation of <see cref="IResumeContextService"/>.</summary>
public sealed class ResumeContextService : IResumeContextService
{
    private const int MaxContentLength = 20000;

    private readonly IResumeContextRepository _resumeContexts;

    public ResumeContextService(IResumeContextRepository resumeContexts) => _resumeContexts = resumeContexts;

    public async Task<Result<bool>> SaveAsync(string ingestionSessionId, int ownerId, string content, string? contentFormat, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ingestionSessionId))
        {
            return Result<bool>.Invalid("Ingestion session id must not be null, empty, or whitespace-only.");
        }

        var validated = ToolOutputValidator.Validate(content, MaxContentLength);
        if (!validated.IsSuccess)
        {
            return Result<bool>.Invalid(validated.Error!);
        }

        // Update in place when a ResumeContext already exists for this (session, owner) rather
        // than always inserting - the frontend deliberately reuses one session id across saves
        // (T2.3), so always-insert would leave duplicate rows and GetForOwnerAsync's
        // FirstOrDefaultAsync could return either one.
        var existing = await _resumeContexts.GetForOwnerAsync(ingestionSessionId, ownerId, ct);
        if (existing is not null)
        {
            existing.Content = validated.Value!;
            existing.ContentFormat = contentFormat ?? "text";
            existing.UpdatedAt = DateTime.UtcNow;

            await _resumeContexts.SaveChangesAsync(ct);
            return Result<bool>.Ok(true);
        }

        await _resumeContexts.AddAsync(new ResumeContext
        {
            IngestionSessionId = ingestionSessionId,
            OwnerId = ownerId,
            Content = validated.Value!,
            ContentFormat = contentFormat ?? "text"
        }, ct);

        try
        {
            await _resumeContexts.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Check-then-act can still lose a race: a concurrent request for this same
            // (session, owner) inserted first, and the unique index (PR #40 review, round 1)
            // rejected ours. But DbUpdateException also covers unrelated persistence failures
            // (DB unavailable, some other constraint) - catching it unconditionally and always
            // reporting Conflict would misreport those as a race and hide the real error (PR #40
            // review, round 3). Re-check business state rather than introspect provider-specific
            // exception internals (SQLite error codes), so this stays correct regardless of the
            // underlying ADO provider: only a race actually leaves a row for this exact pair.
            var raceWinner = await _resumeContexts.GetForOwnerAsync(ingestionSessionId, ownerId, ct);
            if (raceWinner is null)
                throw;

            return Result<bool>.Conflict("Another save for this session is already in progress. Please retry.");
        }

        return Result<bool>.Ok(true);
    }
}
