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

        var resumeContext = new ResumeContext
        {
            IngestionSessionId = ingestionSessionId,
            OwnerId = ownerId,
            Content = validated.Value!,
            ContentFormat = contentFormat ?? "text"
        };

        await _resumeContexts.AddAsync(resumeContext, ct);
        await _resumeContexts.SaveChangesAsync(ct);

        return Result<bool>.Ok(true);
    }
}
