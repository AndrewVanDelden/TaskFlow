using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for agent activity logs. The only code that queries logs via EF Core.</summary>
public interface IAgentLogRepository
{
    Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, CancellationToken ct = default);
    Task<List<AgentLog>> GetTaskScopedSinceAsync(string agentName, DateTime since, int limit, CancellationToken ct = default);
    Task AddAsync(AgentLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}