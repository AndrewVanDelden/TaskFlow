using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for tasks. The only code that queries tasks via EF Core.</summary>
public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(int id, bool includeAssignee = false, CancellationToken ct = default);

    /// <summary>
    /// Returns every task the caller is allowed to see: generic tasks (no <c>ApplicationId</c>)
    /// unconditionally — the shared board is visible to every authenticated user by design — plus
    /// Epic 3 sibling tasks (<c>ApplicationId</c> set) only when the owning <see cref="JobApplication"/>'s
    /// <c>OwnerId</c> matches <paramref name="callerId"/>. Without this, a tailored resume/cover letter
    /// (<see cref="TaskItem.TailoredContent"/>) would be visible to any authenticated user via the
    /// shared board response (PR #45 review finding).
    /// <paramref name="archived"/> is a binary partition, not an additive filter like
    /// <paramref name="status"/>/<paramref name="priority"/>: <c>false</c> returns only tasks with
    /// <c>ArchivedAt == null</c> (the board's default view, unchanged from before archiving existed),
    /// <c>true</c> returns only tasks with <c>ArchivedAt != null</c> (the separate Archive view).
    /// Every caller must pick one explicitly — there is no "both" option.
    /// </summary>
    Task<List<TaskItem>> GetAllAsync(WorkflowStatus? status, TaskPriority? priority, bool archived, int callerId, CancellationToken ct = default);
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

    /// <summary>
    /// Atomically saves an Epic 3 agent's generated output to <see cref="TaskItem.TailoredContent"/>
    /// AND moves the task from <see cref="WorkflowStatus.InProgress"/> to <see cref="WorkflowStatus.Review"/>
    /// in one guarded UPDATE — there is no window where content is saved but the status transition could
    /// fail separately, or vice versa. Mirrors <see cref="MarkForReviewAsync"/>'s atomicity, extended by
    /// one more <c>SetProperty</c>. Returns true if a row moved (the task was InProgress).
    /// Does not itself validate content length — callers must validate before calling (see
    /// <c>ToolOutputValidator</c>); SQLite's TEXT column type does not enforce <c>[MaxLength]</c>.
    /// </summary>
    Task<bool> SaveTailoredContentAndMarkForReviewAsync(int taskId, string content, CancellationToken ct = default);

    /// <summary>
    /// Board Done-column "archive" action for a single task: a guarded UPDATE that sets
    /// <see cref="TaskItem.ArchivedAt"/> to now only when the task is <see cref="WorkflowStatus.Done"/>
    /// and not already archived (idempotent — a second call on an already-archived task is a no-op,
    /// not a re-stamp). Ownership is not checked here: the caller (<see cref="callerId"/>, kept for
    /// signature symmetry with the rest of this file's guarded single-task methods) is validated by
    /// the service layer before this is called, same division of responsibility as every other
    /// single-item action — see <see cref="MarkForReviewAsync"/>, not <c>TryApprovePairAsync</c>'s
    /// owner-in-the-WHERE-clause pattern, since this is a plain single-task action, not a cross-table
    /// pair action. Returns true if a row was affected.
    /// </summary>
    Task<bool> ArchiveAsync(int id, int callerId, CancellationToken ct = default);

    /// <summary>
    /// Restores a previously archived task: a guarded UPDATE that clears
    /// <see cref="TaskItem.ArchivedAt"/> only when it is currently set. Symmetric to
    /// <see cref="ArchiveAsync"/> — same no-ownership-check division of responsibility. Returns true
    /// if a row was affected.
    /// </summary>
    Task<bool> UnarchiveAsync(int id, int callerId, CancellationToken ct = default);

    /// <summary>
    /// Board Done-column "clear all" bulk action: archives every <see cref="WorkflowStatus.Done"/>,
    /// not-yet-archived task <paramref name="callerId"/> is allowed to see — mirrors
    /// <see cref="GetAllAsync"/>'s ownership scoping exactly (generic tasks unconditionally, Epic 3
    /// sibling tasks only when owned by the caller), so a bulk clear can never archive another user's
    /// personal tailored resume/cover-letter task. Returns the number of tasks archived.
    /// </summary>
    Task<int> ArchiveAllDoneAsync(int callerId, CancellationToken ct = default);

    Task AddAsync(TaskItem task, CancellationToken ct = default);
    void Remove(TaskItem task);
    Task SaveChangesAsync(CancellationToken ct = default);
}