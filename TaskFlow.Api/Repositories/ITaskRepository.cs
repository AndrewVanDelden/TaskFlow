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

    /// <summary>Returns the sibling tasks (e.g. resume + cover letter) belonging to a JobApplication, ordered by Id.</summary>
    Task<List<TaskItem>> GetByApplicationIdAsync(int applicationId, CancellationToken ct = default);

    /// <summary>Counts tasks that are not Done (Todo + InProgress + Review) — the board's open work.</summary>
    Task<int> CountOpenAsync(CancellationToken ct = default);

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

    /// <summary>
    /// Rolls back a claim: moves a task from <see cref="WorkflowStatus.InProgress"/> back to
    /// <see cref="WorkflowStatus.Todo"/> and clears the owner. Returns true if a row moved. Used when
    /// an executor cycle fails or is cancelled, so the task is never orphaned InProgress.
    /// </summary>
    Task<bool> ReleaseClaimAsync(int taskId, CancellationToken ct = default);

    /// <summary>
    /// Returns every task stuck in InProgress for longer than <paramref name="staleAfter"/> (based on
    /// UpdatedAt, which is stamped at claim time) back to Todo, clearing ClaimedBy. Recovers work
    /// orphaned by a process crash or kill mid-cycle, which the agent's own try/catch cannot handle
    /// since it never runs. Returns the number of tasks recovered.
    /// </summary>
    Task<int> RecoverStaleInProgressAsync(TimeSpan staleAfter, CancellationToken ct = default);

    Task AddAsync(TaskItem task, CancellationToken ct = default);
    void Remove(TaskItem task);
    Task SaveChangesAsync(CancellationToken ct = default);
}