using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public class AgentLogRepository : IAgentLogRepository
{
    private readonly AppDbContext _db;
    public AgentLogRepository(AppDbContext db) => _db = db;

    public async Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, int callerId, CancellationToken ct = default)
    {
        var query = _db.AgentLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(agentName))
            query = query.Where(l => l.AgentName == agentName);

        // Same ownership rule TaskRepository.GetAllAsync applies to the tasks themselves: a
        // cycle-summary log (no TaskId) and a generic task's log stay visible to everyone; an
        // Epic 3 sibling-task log is visible only to its JobApplication's owner. AgentLog.TaskId
        // has no FK/navigation (a task can be deleted independently), so this is a correlated
        // EXISTS subquery rather than a navigation-based Include - and a log whose task no longer
        // resolves is excluded entirely (fails closed) rather than guessed at.
        query = query.Where(l =>
            l.TaskId == null ||
            _db.Tasks.Any(t => t.Id == l.TaskId && (t.ApplicationId == null || t.Application!.OwnerId == callerId)));

        return await query.OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
    }

    public Task<List<AgentLog>> GetTaskScopedSinceAsync(string agentName, DateTime since, int limit, CancellationToken ct = default) =>
        _db.AgentLogs
            .Where(l => l.AgentName == agentName && l.CreatedAt > since && l.TaskId != null)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountByAgentActionSinceAsync(string agentName, string action, DateTime since, CancellationToken ct = default) =>
        _db.AgentLogs.CountAsync(
            l => l.AgentName == agentName && l.Action == action && l.CreatedAt >= since, ct);

    public Task<List<AgentLog>> GetByTaskAndActionAsync(int taskId, string action, int limit, CancellationToken ct = default) =>
        _db.AgentLogs
            .Where(l => l.TaskId == taskId && l.Action == action)
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

    public async Task AddAsync(AgentLog log, CancellationToken ct = default) =>
        await _db.AgentLogs.AddAsync(log, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}