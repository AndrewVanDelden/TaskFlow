using Microsoft.AspNetCore.SignalR;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services;

public class SignalRAgentNotifier : IAgentNotifier
{
    private readonly IHubContext<AgentHub> _hub;
    private readonly ILogger<SignalRAgentNotifier> _logger;

    public SignalRAgentNotifier(
        IHubContext<AgentHub> hub,
        ILogger<SignalRAgentNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task AgentActionAsync(AgentLog log, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.All.SendAsync("AgentAction", new
            {
                id = log.Id,
                taskId = log.TaskId,
                agentName = log.AgentName,
                action = log.Action,
                details = log.Details,
                success = log.Success,
                createdAt = log.CreatedAt,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // A broadcast failure must never break an agent cycle.
            _logger.LogWarning(ex, "Failed to broadcast agent action.");
        }
    }

    public async Task AgentCycleAsync(string agentName, string phase, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.All.SendAsync("AgentCycle", new
            {
                agentName,
                phase,
                at = DateTime.UtcNow,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast agent cycle.");
        }
    }
}