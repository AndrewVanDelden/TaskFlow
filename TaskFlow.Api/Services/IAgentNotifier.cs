using TaskFlow.Api.Models;

namespace TaskFlow.Api.Services;

/// <summary>Broadcasts agent activity to connected dashboards (implemented over SignalR).</summary>
public interface IAgentNotifier
{
    /// <param name="ownerId">
    /// The user this event belongs to, or null for the shared generic board. Required (not
    /// defaulted) so every call site makes an explicit ownership decision instead of silently
    /// falling back to a broadcast-to-everyone that could leak an Epic 3 sibling task's activity
    /// (see Epic 3 Pre-Merge Code Review, finding 1.1). Pass <see cref="TaskItem.OwnerId"/>.
    /// </param>
    Task AgentActionAsync(AgentLog log, int? ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts an agent cycle start/complete event. Always sent to every connected client -
    /// unlike <see cref="AgentActionAsync"/> and <see cref="TaskMovedAsync"/>, this event carries
    /// no per-task or per-user content, only the agent's name and phase.
    /// </summary>
    Task AgentCycleAsync(string agentName, string phase, CancellationToken cancellationToken = default);

    /// <summary>Broadcasts that a task moved to a new status, so boards can update that one card live.</summary>
    /// <param name="ownerId">See <see cref="AgentActionAsync"/>.</param>
    Task TaskMovedAsync(int taskId, WorkflowStatus status, int? ownerId, CancellationToken cancellationToken = default);
}
