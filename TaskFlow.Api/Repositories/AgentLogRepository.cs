using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

public class AgentLogRepository : IAgentLogRepository
{
    private readonly AppDbContext _db;
    public AgentLogRepository(AppDbContext db) => _db = db;

    public async Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, CancellationToken ct = default)
    {
        var query = _db.AgentLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(agentName))
            query = query.Where(l => l.AgentName == agentName);
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