using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Maps each approved <see cref="TaskDraft"/> to a To Do <see cref="TaskItem"/> carrying its kind
/// and provenance (source document + section), and writes them through the repository.
/// </summary>
public sealed class DraftCommitService : IDraftCommitService
{
    private readonly ITaskRepository _tasks;
    public DraftCommitService(ITaskRepository tasks) => _tasks = tasks;

    public async Task<Result<int>> CommitAsync(string? sourceName, IReadOnlyList<TaskDraft> drafts, CancellationToken cancellationToken = default)
    {
        foreach (var draft in drafts)
        {
            await _tasks.AddAsync(new TaskItem
            {
                Title = draft.Title,
                Description = draft.Description,
                Status = WorkflowStatus.Todo,
                Kind = draft.Kind,
                SourceName = sourceName,
                SourceSection = draft.Section
            }, cancellationToken);
        }

        await _tasks.SaveChangesAsync(cancellationToken);
        return Result<int>.Ok(drafts.Count);
    }
}
