using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services;

/// <summary>Broadcasts agent activity to connected dashboards (implemented over SignalR).</summary>
public interface IAgentNotifier
{
    Task AgentActionAsync(AgentLog log, CancellationToken cancellationToken = default);
    Task AgentCycleAsync(string agentName, string phase, CancellationToken cancellationToken = default);

    /// <summary>Broadcasts that a task moved to a new status, so boards can update that one card live.</summary>
    Task TaskMovedAsync(int taskId, WorkflowStatus status, CancellationToken cancellationToken = default);
}