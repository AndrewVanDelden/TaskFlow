namespace TaskFlow.Api.Agents;

/// <summary>Lifecycle phases broadcast at the start/end of a cycle (dashboard status).</summary>
public static class AgentPhases
{
    public const string Started = "started";
    public const string Completed = "completed";
}

/// <summary>
/// Canonical <see cref="TaskFlow.Api.Models.AgentLog.Action"/> values.
/// These strings are part of the API contract: the React dashboard keys its
/// color map on them, so they must not change without updating the frontend.
/// </summary>
public static class AgentActions
{
    // Prioritizer
    public const string PriorityUpdated = "PriorityUpdated";     // one task re-prioritized
    public const string PrioritiesUpdated = "PrioritiesUpdated"; // cycle summary: at least one change
    public const string NoChangesNeeded = "NoChangesNeeded";     // cycle summary: nothing changed

    // Stale task detector
    public const string Escalated = "Escalated";
    public const string Reassigned = "Reassigned";
    public const string FlaggedForReview = "FlaggedForReview";
    public const string CycleActions = "CycleActions";           // cycle summary: at least one action
    public const string NoActionNeeded = "NoActionNeeded";       // cycle summary: nothing to do

    // Generic executor
    public const string Claimed = "Claimed";                     // task claimed (Todo -> InProgress)
    public const string ProgressRecorded = "ProgressRecorded";   // a progress note during execution
    public const string ReviewRequested = "ReviewRequested";     // work done -> moved to Review
    public const string AutoFinalized = "AutoFinalized";         // cycle ended without review -> moved to Review
}
