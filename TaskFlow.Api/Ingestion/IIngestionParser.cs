using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Turns raw document text into task drafts. One implementation per input type: a free
/// rules-based parser and a paid Claude-backed parser both sit behind this seam, composed by
/// <see cref="TieredIngestionParser"/>. Async because a Claude-backed parser must await the API;
/// the rules parser simply returns a completed task.
/// </summary>
public interface IIngestionParser
{
    Task<Result<IReadOnlyList<TaskDraft>>> ParseAsync(string documentText, CancellationToken cancellationToken = default);
}
