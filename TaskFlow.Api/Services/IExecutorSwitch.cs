namespace TaskFlow.Api.Services;

/// <summary>
/// Runtime on/off for the autonomous executor. The executor reads it each cycle; the API toggles it.
/// A singleton so the state is shared between the request that toggles it and the background agent.
/// </summary>
public interface IExecutorSwitch
{
    bool IsEnabled { get; }
    void Enable();
    void Disable();

    /// <summary>
    /// Completes the next time <see cref="Enable"/> is called (or immediately if a wake is already
    /// pending). Lets the agent loop skip the remainder of its interval delay so re-enabling the
    /// executor runs a cycle right away instead of waiting up to a full interval.
    /// </summary>
    Task WaitForWakeAsync(CancellationToken cancellationToken);
}
