using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using Tool = Anthropic.SDK.Common.Tool;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Detects tasks that have gone stale and takes corrective action via Claude.
/// Unlike the prioritizer, this agent chooses between three tools per task and
/// uses its own <see cref="AgentLog"/> history as memory to avoid repeating
/// actions on the same task across cycles.
///
/// The Claude conversation mechanics live in <see cref="ClaudeAgentBase"/>; this
/// class only supplies the tools, the prompt, and the per-tool handlers, reading
/// and writing through repositories rather than a DbContext.
/// </summary>
public class StaleTaskAgent : ClaudeAgentBase
{
    private const string EscalateTool = "escalate_task";
    private const string ReassignTool = "reassign_task";
    private const string FlagTool = "flag_for_review";

    /// <summary>A user with at least this many open tasks is considered overloaded.</summary>
    private const int OverloadedTaskCount = 5;

    /// <summary>How far back the agent looks at its own actions when building memory.</summary>
    private static readonly TimeSpan MemoryWindow = TimeSpan.FromDays(7);

    private readonly ITaskRepository _tasks;
    private readonly IUserRepository _users;

    public StaleTaskAgent(
        IClaudeClient claude,
        ITaskRepository tasks,
        IUserRepository users,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger<StaleTaskAgent> logger)
        : base(claude, logs, notifier, config, logger)
    {
        _tasks = tasks;
        _users = users;
    }

    public override string Name => "StaleTaskDetector";

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:StaleTaskIntervalMinutes", 60));

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        // ── OBSERVE ──────────────────────────────────────────────────────────────
        var thresholdHours = Config.GetValue("Agents:StaleTaskThresholdHours", 48);
        var cutoff = DateTime.UtcNow.AddHours(-thresholdHours);

        var staleTasks = await _tasks.GetStaleAsync(cutoff, cancellationToken);

        if (staleTasks.Count == 0)
        {
            Logger.LogInformation("[{Agent}] No stale tasks found. Skipping cycle.", Name);
            return;
        }

        Logger.LogInformation(
            "[{Agent}] Found {Count} stale task(s) (>{Hours}h without update).",
            Name, staleTasks.Count, thresholdHours);

        await NotifyCycleStartedAsync(cancellationToken);

        var contextJson = await BuildContextJsonAsync(cancellationToken);
        var recentActions = await Logs.GetTaskScopedSinceAsync(
            Name, DateTime.UtcNow - MemoryWindow, limit: 50, cancellationToken);

        // ── REASON + ACT ─────────────────────────────────────────────────────────
        if (!ClaudeConfigured)
        {
            Logger.LogWarning("[{Agent}] Anthropic API key not configured. Skipping cycle.", Name);
            return;
        }

        var actionsApplied = await RunToolConversationAsync(
            prompt: BuildPrompt(staleTasks, recentActions, contextJson, thresholdHours),
            tools: BuildTools(),
            dispatch: ExecuteToolAsync,
            cancellationToken);

        // ── CYCLE SUMMARY ────────────────────────────────────────────────────────
        Logger.LogInformation(
            "[{Agent}] Cycle complete. {Count} action(s) taken.", Name, actionsApplied);

        await RecordCycleSummaryAsync(new AgentLog
        {
            AgentName = Name,
            Action = actionsApplied > 0 ? AgentActions.CycleActions : AgentActions.NoActionNeeded,
            TaskId = null,   // cycle-level summary, not tied to one task
            Details = $"Reviewed {staleTasks.Count} stale task(s). Took {actionsApplied} action(s).",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        await NotifyCycleCompletedAsync(cancellationToken);
    }

    // ── CONTEXT GATHERING ────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes team roster and per-user open-task counts so Claude can judge
    /// whether an owner is overloaded when deciding to reassign.
    /// </summary>
    private async Task<string> BuildContextJsonAsync(CancellationToken cancellationToken)
    {
        var counts = await _tasks.GetOpenCountsByUserAsync(cancellationToken);
        var workload = counts.Select(kv => new { UserId = kv.Key, OpenCount = kv.Value }).ToList();

        var allUsers = await _users.GetAllAsync(cancellationToken);
        var users = allUsers.Select(u => new { u.Id, u.Name }).ToList();

        return JsonSerializer.Serialize(new { users, workload });
    }

    // ── TOOL DEFINITIONS ─────────────────────────────────────────────────────────
    private static List<Tool> BuildTools() =>
    [
        DefineTool(
            EscalateTool,
            "Escalate a stale but important task by setting its priority to High. " +
            "Use when the task is clearly still needed and is overdue. " +
            "Do NOT use if the task is already High priority - flag it for review instead.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The ID of the task to escalate." },
                    ["reason"] = new { type = "string", description = "One sentence explaining why this needs escalation." }
                },
                required = new[] { "task_id", "reason" }
            }),

        DefineTool(
            ReassignTool,
            "Reassign a task to a different user, or unassign it so it returns to the pool. " +
            $"Use when the task is unassigned and needs an owner, or when the current owner " +
            $"has {OverloadedTaskCount} or more open tasks and is overloaded.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The ID of the task to reassign." },
                    ["new_user_id"] = new { type = "integer", description = "Target user ID. Omit to unassign the task." },
                    ["reason"] = new { type = "string", description = "One sentence explaining the reassignment." }
                },
                required = new[] { "task_id", "reason" }
            }),

        DefineTool(
            FlagTool,
            "Flag a task for human review without modifying it. " +
            "Use when the right action is ambiguous, the task may no longer be relevant, " +
            "or the decision requires context you do not have. Prefer this over guessing.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The ID of the task to flag." },
                    ["concern"] = new { type = "string", description = "What specifically a human should look at." }
                },
                required = new[] { "task_id", "concern" }
            })
    ];

    // ── TOOL DISPATCH ────────────────────────────────────────────────────────────
    private async Task<ContentBase> ExecuteToolAsync(
        ToolUseContent toolUse,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolUse.Name switch
            {
                EscalateTool => await EscalateAsync(toolUse, cancellationToken),
                ReassignTool => await ReassignAsync(toolUse, cancellationToken),
                FlagTool     => await FlagAsync(toolUse, cancellationToken),
                _            => ToolResult(toolUse, $"Error: unknown tool {toolUse.Name}")
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Agent}] Tool execution failed for {Tool}", Name, toolUse.Name);
            return ToolResult(toolUse, $"Error: {ex.Message}");
        }
    }

    // ── ESCALATE ─────────────────────────────────────────────────────────────────
    private async Task<ContentBase> EscalateAsync(ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<EscalateArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize escalate_task arguments.");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        var previous = task.Priority;
        task.Priority = TaskPriority.High;
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.Escalated,
            TaskId = task.Id,
            Details = $"Priority {previous} -> High. {args.Reason}",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        Logger.LogInformation(
            "[{Agent}] Escalated Task {Id} '{Title}': {From} -> High. {Reason}",
            Name, task.Id, task.Title, previous, args.Reason);

        return ToolResult(toolUse, $"Escalated Task {task.Id} ('{task.Title}') from {previous} to High.");
    }

    // ── REASSIGN ─────────────────────────────────────────────────────────────────
    private async Task<ContentBase> ReassignAsync(ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<ReassignArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize reassign_task arguments.");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        if (args.NewUserId.HasValue)
        {
            var exists = await _users.ExistsAsync(args.NewUserId.Value, cancellationToken);
            if (!exists)
                return ToolResult(toolUse, $"User {args.NewUserId} does not exist.");
        }

        var previousOwner = task.AssignedToId;
        task.AssignedToId = args.NewUserId;
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.Reassigned,
            TaskId = task.Id,
            Details = $"Owner {previousOwner?.ToString() ?? "none"} -> " +
                      $"{args.NewUserId?.ToString() ?? "unassigned"}. {args.Reason}",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        Logger.LogInformation(
            "[{Agent}] Reassigned Task {Id} '{Title}': owner {From} -> {To}. {Reason}",
            Name, task.Id, task.Title,
            previousOwner?.ToString() ?? "none",
            args.NewUserId?.ToString() ?? "unassigned",
            args.Reason);

        return ToolResult(toolUse,
            $"Reassigned Task {task.Id} ('{task.Title}') to {(args.NewUserId?.ToString() ?? "unassigned")}.");
    }

    // ── FLAG FOR REVIEW ──────────────────────────────────────────────────────────
    // Note: does NOT modify the task. Log only. That is intentional.
    private async Task<ContentBase> FlagAsync(ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<FlagArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize flag_for_review arguments.");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.FlaggedForReview,
            TaskId = task.Id,
            Details = args.Concern,
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        Logger.LogInformation(
            "[{Agent}] Flagged Task {Id} '{Title}' for review: {Concern}",
            Name, task.Id, task.Title, args.Concern);

        return ToolResult(toolUse, $"Flagged Task {task.Id} ('{task.Title}') for human review.");
    }

    // ── PROMPT BUILDER ───────────────────────────────────────────────────────────
    private static string BuildPrompt(
        List<TaskItem> staleTasks,
        List<AgentLog> recentActions,
        string contextJson,
        int thresholdHours)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a stale task detection agent for a software development team.");
        sb.AppendLine($"A task is stale if not Done and not updated in {thresholdHours}+ hours.");
        sb.AppendLine("For each stale task, choose AT MOST ONE action:");
        sb.AppendLine("  - escalate_task    : still needed and overdue -> raise priority to High");
        sb.AppendLine($"  - reassign_task    : unassigned, or the owner has {OverloadedTaskCount}+ open tasks");
        sb.AppendLine("  - flag_for_review  : ambiguous, possibly obsolete, or needs a human decision");
        sb.AppendLine("Rules: do NOT act on a task you already acted on recently; do NOT escalate an already-High task (flag it); prefer flag_for_review when uncertain; no action is acceptable.");
        sb.AppendLine($"Current date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("=== STALE TASKS ===");
        foreach (var t in staleTasks)
        {
            var daysStale = (DateTime.UtcNow - t.UpdatedAt).TotalDays;
            sb.AppendLine($"  ID {t.Id}: {t.Title} | {t.Status}/{t.Priority} | " +
                          $"Assignee {t.AssignedTo?.Name ?? "UNASSIGNED"} (id {t.AssignedToId?.ToString() ?? "null"}) | stale {daysStale:F1}d");
        }
        sb.AppendLine();
        sb.AppendLine("=== TEAM WORKLOAD ===");
        sb.AppendLine(contextJson);
        sb.AppendLine();
        sb.AppendLine("=== YOUR RECENT ACTIONS (last 7 days) ===");
        if (recentActions.Count == 0) sb.AppendLine("  (none)");
        else foreach (var l in recentActions) sb.AppendLine($"  {l.CreatedAt:yyyy-MM-dd HH:mm} | Task {l.TaskId} | {l.Action} | {l.Details}");
        sb.AppendLine();
        sb.AppendLine("Call the appropriate tool for each task that needs action, then finish.");
        return sb.ToString();
    }

    // ── ARGUMENT RECORDS ─────────────────────────────────────────────────────────
    // Each maps directly to the JSON arguments Claude sends for the matching tool.
    private sealed record EscalateArgs(
        [property: JsonPropertyName("task_id")] int TaskId,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record ReassignArgs(
        [property: JsonPropertyName("task_id")] int TaskId,
        [property: JsonPropertyName("new_user_id")] int? NewUserId,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record FlagArgs(
        [property: JsonPropertyName("task_id")] int TaskId,
        [property: JsonPropertyName("concern")] string Concern);
}
