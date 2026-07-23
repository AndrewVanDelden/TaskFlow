using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services;

public interface IAgentNotifier
{
    Task AgentActionAsync(AgentLog log, CancellationToken cancellationToken = default);
    Task AgentCycleAsync(string agentName, string phase, CancellationToken cancellationToken = default);
}