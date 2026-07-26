using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services;

/// <summary>Broadcasts agent activity to connected dashboards (implemented over SignalR).</summary>
public interface IAgentNotifier
{
    Task AgentActionAsync(AgentLog log, CancellationToken cancellationToken = default);
    Task AgentCycleAsync(string agentName, string phase, CancellationToken cancellationToken = default);
}