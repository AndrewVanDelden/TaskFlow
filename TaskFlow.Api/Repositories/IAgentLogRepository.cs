using TaskFlow.Api.Models;

namespace TaskFlow.Api.Repositories;

/// <summary>Data access for agent activity logs. The only code that queries logs via EF Core.</summary>
public interface IAgentLogRepository
{
    /// <param name="callerId">
    /// Scopes the result the same way <c>ITaskRepository.GetAllAsync</c> scopes tasks: a
    /// cycle-summary log (no <c>TaskId</c>) or a generic task's log is visible to everyone; an
    /// Epic 3 sibling-task log is visible only to its <c>JobApplication</c>'s owner; a log whose
    /// task no longer exists is excluded (fails closed). Required, not defaulted, so a caller
    /// cannot fetch every log by omitting it (Epic 3 Pre-Merge Code Review, PR #50: Copilot found
    /// the SignalR ownership fix left this REST path unscoped).
    /// </param>
    Task<List<AgentLog>> GetRecentAsync(string? agentName, int limit, int callerId, CancellationToken ct = default);
    Task<List<AgentLog>> GetTaskScopedSinceAsync(string agentName, DateTime since, int limit, CancellationToken ct = default);

    /// <summary>Counts logs for an agent+action at or after <paramref name="since"/> (used by the spend guard).</summary>
    Task<int> CountByAgentActionSinceAsync(string agentName, string action, DateTime since, CancellationToken ct = default);

    /// <summary>Logs for a specific task and action, newest first (e.g. a task's rejection reasons).</summary>
    Task<List<AgentLog>> GetByTaskAndActionAsync(int taskId, string action, int limit, CancellationToken ct = default);
    Task AddAsync(AgentLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}