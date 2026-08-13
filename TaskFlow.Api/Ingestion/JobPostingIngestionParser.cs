using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Composes the free <see cref="JobPostingParser"/> and paid <see cref="ClaudeJobPostingParser"/>
/// behind <see cref="IJobPostingIngestionParser"/> by delegating to the existing
/// <see cref="TieredIngestionParser"/> rather than reimplementing the free-first escalation logic
/// (DRY - tiering behavior lives in exactly one place).
/// </summary>
public sealed class JobPostingIngestionParser : IJobPostingIngestionParser
{
    private readonly IIngestionParser _tiered;

    public JobPostingIngestionParser(IIngestionParser free, IIngestionParser paid)
        => _tiered = new TieredIngestionParser(free, paid);

    public Task<Result<IReadOnlyList<TaskDraft>>> ParseAsync(string documentText, CancellationToken cancellationToken = default)
        => _tiered.ParseAsync(documentText, cancellationToken);
}
