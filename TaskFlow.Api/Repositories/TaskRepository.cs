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
        var query = _db.Tasks.AsQueryable();
        if (includeAssignee) query = query.Include(t => t.AssignedTo);
        return await query.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<List<TaskItem>> GetAllAsync(WorkflowStatus? status, TaskPriority? priority, CancellationToken ct = default)
    {
        var query = _db.Tasks.Include(t => t.AssignedTo).AsQueryable();
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

    public async Task AddAsync(TaskItem task, CancellationToken ct = default) =>
        await _db.Tasks.AddAsync(task, ct);

    public void Remove(TaskItem task) => _db.Tasks.Remove(task);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}