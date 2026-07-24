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

    public async Task<Dictionary<int, int>> GetOpenCountsByUserAsync(CancellationToken ct = default) =>
        await _db.Tasks
            .Where(t => t.Status != WorkflowStatus.Done && t.AssignedToId != null)
            .GroupBy(t => t.AssignedToId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

    public async Task AddAsync(TaskItem task, CancellationToken ct = default) =>
        await _db.Tasks.AddAsync(task, ct);

    public void Remove(TaskItem task) => _db.Tasks.Remove(task);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}