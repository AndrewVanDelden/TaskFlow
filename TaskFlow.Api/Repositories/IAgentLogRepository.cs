using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for agent activity logs. The only code that queries logs via EF Core.</summary>
public interface IAgentLogRepository
{
    Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, CancellationToken ct = default);
    Task<List<AgentLog>> GetTaskScopedSinceAsync(string agentName, DateTime since, int limit, CancellationToken ct = default);

    /// <summary>Counts logs for an agent+action at or after <paramref name="since"/> (used by the spend guard).</summary>
    Task<int> CountByAgentActionSinceAsync(string agentName, string action, DateTime since, CancellationToken ct = default);
    Task AddAsync(AgentLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}