using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Persists approved drafts as real board tasks in the To Do column, stamping kind and
/// provenance. Returns the number of tasks committed.
/// </summary>
public interface IDraftCommitService
{
    Task<Result<int>> CommitAsync(string? sourceName, IReadOnlyList<TaskDraft> drafts, CancellationToken cancellationToken = default);
}
