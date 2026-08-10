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
        }
        else
        {
            await _resumeContexts.AddAsync(new ResumeContext
            {
                IngestionSessionId = ingestionSessionId,
                OwnerId = ownerId,
                Content = validated.Value!,
                ContentFormat = contentFormat ?? "text"
            }, ct);
        }

        await _resumeContexts.SaveChangesAsync(ct);

        return Result<bool>.Ok(true);
    }
}
