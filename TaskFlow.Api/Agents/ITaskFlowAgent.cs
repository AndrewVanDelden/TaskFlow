namespace TaskFlow.Api.Agents;

/// <summary>
/// Contract that every TaskFlow agent must implement.
/// The AgentRunner discovers and executes all registered agents.
/// </summary>
public interface ITaskFlowAgent
{
    /// <summary>Human-readable name used in logs and the activity feed.</summary>
    string Name { get; }

    /// <summary>How often this agent runs its loop.</summary>
    TimeSpan Interval { get; }

    /// <summary>
    /// Execute one full observe → reason → act cycle.
    /// Called by AgentRunner on the agent's schedule.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);
}