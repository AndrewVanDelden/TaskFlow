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
}
