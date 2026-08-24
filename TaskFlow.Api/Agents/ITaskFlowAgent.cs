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

    /// <summary>
    /// Awaited alongside the interval delay between cycles, racing it via Task.WhenAny: whichever
    /// completes first ends the wait. Lets an agent shorten the wait when something makes it worth
    /// running sooner, instead of always sitting out the full Interval. ClaudeAgentBase's default
    /// never completes early (only on shutdown) - see GenericExecutorAgent for the one agent that
    /// overrides this, so re-enabling the executor runs a cycle immediately.
    /// </summary>
    Task WaitForWakeSignalAsync(CancellationToken cancellationToken);
}