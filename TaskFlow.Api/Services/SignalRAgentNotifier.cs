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

    public Task AgentActionAsync(AgentLog log, int? ownerId, CancellationToken cancellationToken = default) =>
        BroadcastAsync(ownerId, HubEvents.AgentAction, new
        {
            id = log.Id,
            taskId = log.TaskId,
            agentName = log.AgentName,
            action = log.Action,
            details = log.Details,
            success = log.Success,
            createdAt = log.CreatedAt,
        }, "Failed to broadcast agent action.", cancellationToken);

    public Task AgentCycleAsync(string agentName, string phase, CancellationToken cancellationToken = default) =>
        BroadcastAsync(ownerId: null, HubEvents.AgentCycle, new
        {
            agentName,
            phase,
            at = DateTime.UtcNow,
        }, "Failed to broadcast agent cycle.", cancellationToken);

    public Task TaskMovedAsync(int taskId, WorkflowStatus status, int? ownerId, CancellationToken cancellationToken = default) =>
        // status sent as its string name to match the frontend TaskStatus union.
        BroadcastAsync(ownerId, HubEvents.TaskMoved, new
        {
            id = taskId,
            status = status.ToString(),
        }, "Failed to broadcast task move.", cancellationToken);

    /// <summary>
    /// Sends <paramref name="eventName"/>/<paramref name="payload"/> to every connected client when
    /// <paramref name="ownerId"/> is null (the shared generic board), or scopes delivery to just
    /// that user's connections otherwise via <see cref="AgentHub.GroupForUser"/> - Epic 3 sibling
    /// task events can carry personal job-application content and must not reach other users (Epic
    /// 3 Pre-Merge Code Review, finding 1.1). A broadcast failure must never break an agent cycle,
    /// so failures are logged, not thrown (also collapses the three near-identical try/catch
    /// wrappers this replaced - finding 3.5).
    /// </summary>
    private async Task BroadcastAsync(
        int? ownerId, string eventName, object payload, string failureMessage, CancellationToken cancellationToken)
    {
        try
        {
            var clients = ownerId is int id ? _hub.Clients.Group(AgentHub.GroupForUser(id)) : _hub.Clients.All;
            await clients.SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, failureMessage);
        }
    }
}
