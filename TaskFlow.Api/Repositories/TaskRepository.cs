using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>EF Core implementation of <see cref="ITaskRepository"/>.</summary>
public class TaskRepository : ITaskRepository
{
    private readonly AppDbContext _db;
    public TaskRepository(AppDbContext db) => _db = db;

    public async Task<TaskItem?> GetByIdAsync(int id, bool includeAssignee = false, CancellationToken ct = default)
    {
        // Application is always included (unconditionally, unlike AssignedTo) so every caller of
        // this method gets the navigation populated when ApplicationId is set - TaskService's six
        // single-item actions need task.Application!.OwnerId for the ownership check (T5.0). A LEFT
        // JOIN on a nullable FK is cheap and behavior-preserving for rows where ApplicationId is
        // null, so this is safe for existing callers (agents) that never read .Application.
        var query = _db.Tasks.Include(t => t.Application).AsQueryable();
        if (includeAssignee) query = query.Include(t => t.AssignedTo);
        return await query.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<List<TaskItem>> GetAllAsync(WorkflowStatus? status, TaskPriority? priority, int callerId, CancellationToken ct = default)
    {
        // Generic tasks (no ApplicationId) are the shared board, visible to everyone. Epic 3
        // sibling tasks carry personal document content (TailoredContent) and are visible only to
        // the JobApplication's own owner - a caller with the wrong id must not see them at all,
        // not just be blocked from acting on them (PR #45 review finding). Application is also
        // explicitly Included (not just referenced in the Where) so TaskResponseDto.ApplicationState
        // is populated for the frontend's export-download gating (PR #48 review finding).
        var query = _db.Tasks.Include(t => t.AssignedTo).Include(t => t.Application)
            .Where(t => t.ApplicationId == null || t.Application!.OwnerId == callerId);
        if (status.HasValue)   query = query.Where(t => t.Status == status.Value);
        if (priority.HasValue) query = query.Where(t => t.Priority == priority.Value);
        return await query.OrderBy(t => t.DueDate).ThenBy(t => t.Priority).ToListAsync(ct);
    }

    public Task<List<TaskItem>> GetOpenAsync(CancellationToken ct = default) =>
        _db.Tasks.Include(t => t.AssignedTo)
            .Where(t => t.Status != WorkflowStatus.Done)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetStaleAsync(DateTime cutoff, CancellationToken ct = default) =>
        _db.Tasks.Include(t => t.AssignedTo)
            .Where(t => t.Status != WorkflowStatus.Done && t.UpdatedAt < cutoff)
            .OrderBy(t => t.UpdatedAt)
            .ToListAsync(ct);

    public Task<List<TaskItem>> GetByApplicationIdAsync(int applicationId, CancellationToken ct = default) =>
        _db.Tasks
            .Where(t => t.ApplicationId == applicationId)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

    public Task<int> CountOpenAsync(CancellationToken ct = default) =>
        _db.Tasks.CountAsync(t => t.Status != WorkflowStatus.Done, ct);

    public async Task<Dictionary<int, int>> GetOpenCountsByUserAsync(CancellationToken ct = default) =>
        await _db.Tasks
            .Where(t => t.Status != WorkflowStatus.Done && t.AssignedToId != null)
            .GroupBy(t => t.AssignedToId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

    public async Task<TaskItem?> TryClaimNextAsync(TaskKind kind, string agentName, CancellationToken ct = default)
    {
        // Candidate Todo tasks of this kind, oldest first.
        var candidateIds = await _db.Tasks
            .Where(t => t.Kind == kind && t.Status == WorkflowStatus.Todo)
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var id in candidateIds)
        {
            // Guarded UPDATE: the WHERE Status == Todo makes the Todo -> InProgress claim atomic, so
            // the rows-affected count is the winner check. A loser (0 rows) tries the next candidate.
            var won = await _db.Tasks
                .Where(t => t.Id == id && t.Status == WorkflowStatus.Todo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, WorkflowStatus.InProgress)
                    .SetProperty(t => t.ClaimedBy, agentName)
                    .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

            if (won == 1)
                // AsNoTracking: ExecuteUpdate bypasses the change tracker, so a tracked read would be
                // stale. A fresh no-tracking read reflects the claim (InProgress, ClaimedBy set).
                return await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        }

        return null;
    }

    public async Task<bool> MarkForReviewAsync(int taskId, CancellationToken ct = default)
    {
        // Guarded UPDATE (InProgress -> Review). ExecuteUpdate is atomic and does not touch the
        // change tracker, which is what we want after a no-tracking claim: no stale-entity save.
        var moved = await _db.Tasks
            .Where(t => t.Id == taskId && t.Status == WorkflowStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, WorkflowStatus.Review)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

        return moved == 1;
    }

    public async Task<bool> ReleaseClaimAsync(int taskId, CancellationToken ct = default)
    {
        // Guarded UPDATE (InProgress -> Todo, owner cleared). No-ops if the task already moved on.
        var moved = await _db.Tasks
            .Where(t => t.Id == taskId && t.Status == WorkflowStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, WorkflowStatus.Todo)
                .SetProperty(t => t.ClaimedBy, (string?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

        return moved == 1;
    }

    public async Task<int> RecoverStaleInProgressAsync(TimeSpan staleAfter, CancellationToken ct = default)
    {
        // Guarded bulk UPDATE (InProgress -> Todo, owner cleared) for every row whose UpdatedAt is
        // older than the cutoff. Unlike ReleaseClaimAsync (single task, matched by id) this recovers
        // work orphaned by a crash or kill mid-cycle, where no in-process code path is left to notice.
        var cutoff = DateTime.UtcNow - staleAfter;
        return await _db.Tasks
            .Where(t => t.Status == WorkflowStatus.InProgress && t.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, WorkflowStatus.Todo)
                .SetProperty(t => t.ClaimedBy, (string?)null)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<bool> SaveTailoredContentAndMarkForReviewAsync(int taskId, string content, CancellationToken ct = default)
    {
        // Guarded UPDATE (InProgress -> Review), extended from MarkForReviewAsync by one more
        // SetProperty so the content save and the status transition happen in a single atomic
        // statement — no window where one could succeed without the other.
        var moved = await _db.Tasks
            .Where(t => t.Id == taskId && t.Status == WorkflowStatus.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.TailoredContent, content)
                .SetProperty(t => t.Status, WorkflowStatus.Review)
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

        return moved == 1;
    }

    public async Task AddAsync(TaskItem task, CancellationToken ct = default) =>
        await _db.Tasks.AddAsync(task, ct);

    public void Remove(TaskItem task) => _db.Tasks.Remove(task);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}