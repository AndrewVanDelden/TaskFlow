namespace TaskFlow.Api.Hubs;

/// <summary>
/// SignalR event names. These strings are a contract with the React client
/// (see useAgentFeed's connection.on handlers) — a typo on either side silently
/// stops the live feed, so keep the two ends in sync.
/// </summary>
public static class HubEvents
{
    public const string AgentAction = "AgentAction";
    public const string AgentCycle = "AgentCycle";
}
