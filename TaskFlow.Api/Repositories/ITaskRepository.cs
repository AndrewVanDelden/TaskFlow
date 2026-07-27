using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for tasks. The only code that queries tasks via EF Core.</summary>
public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(int id, bool includeAssignee = false, CancellationToken ct = default);
    Task<List<TaskItem>> GetAllAsync(WorkflowStatus? status, TaskPriority? priority, CancellationToken ct = default);
    Task<List<TaskItem>> GetOpenAsync(CancellationToken ct = default);
    Task<List<TaskItem>> GetStaleAsync(DateTime cutoff, CancellationToken ct = default);
    Task<Dictionary<int, int>> GetOpenCountsByUserAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically claims the oldest <see cref="WorkflowStatus.Todo"/> task of <paramref name="kind"/>
    /// for <paramref name="agentName"/>, moving it to <see cref="WorkflowStatus.InProgress"/> and
    /// stamping the owner. Returns the claimed task, or null if none is available. Safe under
    /// concurrent callers: only one wins any given task.
    /// </summary>
    Task<TaskItem?> TryClaimNextAsync(TaskKind kind, string agentName, CancellationToken ct = default);

    /// <summary>
    /// Atomically moves a task from <see cref="WorkflowStatus.InProgress"/> to
    /// <see cref="WorkflowStatus.Review"/>. Returns true if a row moved (the task was InProgress),
    /// false otherwise. Executors stop at Review; only a human sets Done.
    /// </summary>
    Task<bool> MarkForReviewAsync(int taskId, CancellationToken ct = default);

    Task AddAsync(TaskItem task, CancellationToken ct = default);
    void Remove(TaskItem task);
    Task SaveChangesAsync(CancellationToken ct = default);
}