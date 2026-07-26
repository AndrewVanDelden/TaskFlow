namespace TaskFlow.Api.Agents;

/// <summary>Lifecycle phases broadcast at the start/end of a cycle (dashboard status).</summary>
public static class AgentPhases
{
    public const string Started = "started";
    public const string Completed = "completed";
}

/// <summary>
/// Canonical AgentLog.Action values. These strings are part of the API contract — the
/// React dashboard keys its color map on them, so do not change without updating the frontend.
/// </summary>
public static class AgentActions
{
    public const string PriorityUpdated = "PriorityUpdated";
    public const string PrioritiesUpdated = "PrioritiesUpdated";
    public const string NoChangesNeeded = "NoChangesNeeded";
    public const string Escalated = "Escalated";
    public const string Reassigned = "Reassigned";
    public const string FlaggedForReview = "FlaggedForReview";
    public const string CycleActions = "CycleActions";
    public const string NoActionNeeded = "NoActionNeeded";
}
