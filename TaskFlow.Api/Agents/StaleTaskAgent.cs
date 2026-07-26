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
/// Detects stale tasks and takes corrective action via Claude, choosing between three tools
/// per task and using its own AgentLog history as memory. Policy only — the loop lives in
/// ClaudeAgentBase.
/// </summary>
public class StaleTaskAgent : ClaudeAgentBase
{
    private const string EscalateTool = "escalate_task";
    private const string ReassignTool = "reassign_task";
    private const string FlagTool = "flag_for_review";
    private const int OverloadedTaskCount = 5;

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
        var thresholdHours = Config.GetValue("Agents:StaleTaskThresholdHours", 48);
        var cutoff = DateTime.UtcNow.AddHours(-thresholdHours);

        var staleTasks = await _tasks.GetStaleAsync(cutoff, cancellationToken);
        if (staleTasks.Count == 0)
        {
            Logger.LogInformation("[{Agent}] No stale tasks found. Skipping cycle.", Name);
            return;
        }

        Logger.LogInformation("[{Agent}] Found {Count} stale task(s).", Name, staleTasks.Count);
        await NotifyCycleStartedAsync(cancellationToken);

        if (!ClaudeConfigured)
        {
            Logger.LogWarning("[{Agent}] Claude not configured. Skipping cycle.", Name);
            return;
        }

        var recentActions = await Logs.GetTaskScopedSinceAsync(Name, DateTime.UtcNow.AddDays(-7), 50, cancellationToken);
        var contextJson = await BuildContextJsonAsync(cancellationToken);

        var actionsApplied = await RunToolConversationAsync(
            BuildPrompt(staleTasks, recentActions, contextJson, thresholdHours),
            BuildTools(), ExecuteToolAsync, cancellationToken);

        Logger.LogInformation("[{Agent}] Cycle complete. {Count} action(s) taken.", Name, actionsApplied);

        await Logs.AddAsync(new AgentLog
        {
            AgentName = Name,
            Action = actionsApplied > 0 ? AgentActions.CycleActions : AgentActions.NoActionNeeded,
            TaskId = null,
            Details = $"Reviewed {staleTasks.Count} stale task(s). Took {actionsApplied} action(s).",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        await Logs.SaveChangesAsync(cancellationToken);

        await NotifyCycleCompletedAsync(cancellationToken);
    }

    private async Task<string> BuildContextJsonAsync(CancellationToken ct)
    {
        var workload = await _tasks.GetOpenCountsByUserAsync(ct);
        var users = (await _users.GetAllAsync(ct)).Select(u => new { u.Id, u.Name });
        return JsonSerializer.Serialize(new { users, workload });
    }

    private static List<Tool> BuildTools() =>
    [
        DefineTool(EscalateTool,
            "Escalate a stale but important task by setting its priority to High. " +
            "Do NOT use if already High — flag it instead.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The ID of the task." },
                    ["reason"] = new { type = "string", description = "One sentence explaining why." }
                },
                required = new[] { "task_id", "reason" }
            }),
        DefineTool(ReassignTool,
            "Reassign a task to another user, or unassign it. Use when unassigned, or when the " +
            $"owner has {OverloadedTaskCount}+ open tasks.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The ID of the task." },
                    ["new_user_id"] = new { type = "integer", description = "Target user ID. Omit to unassign." },
                    ["reason"] = new { type = "string", description = "One sentence explaining the reassignment." }
                },
                required = new[] { "task_id", "reason" }
            }),
        DefineTool(FlagTool,
            "Flag a task for human review without modifying it. Prefer this when uncertain.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The ID of the task." },
                    ["concern"] = new { type = "string", description = "What a human should look at." }
                },
                required = new[] { "task_id", "concern" }
            })
    ];

    private async Task<ContentBase> ExecuteToolAsync(ToolUseContent toolUse, CancellationToken cancellationToken)
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

    private async Task<ContentBase> EscalateAsync(ToolUseContent toolUse, CancellationToken ct)
    {
        var args = toolUse.Input.Deserialize<EscalateArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize escalate_task arguments.");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: ct);
        if (task is null) return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        var previous = task.Priority;
        task.Priority = TaskPriority.High;
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name, Action = AgentActions.Escalated, TaskId = task.Id,
            Details = $"Priority {previous} -> High. {args.Reason}", Success = true, CreatedAt = DateTime.UtcNow
        }, ct);

        return ToolResult(toolUse, $"Escalated Task {task.Id} ('{task.Title}') from {previous} to High.");
    }

    private async Task<ContentBase> ReassignAsync(ToolUseContent toolUse, CancellationToken ct)
    {
        var args = toolUse.Input.Deserialize<ReassignArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize reassign_task arguments.");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: ct);
        if (task is null) return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        if (args.NewUserId.HasValue && !await _users.ExistsAsync(args.NewUserId.Value, ct))
            return ToolResult(toolUse, $"User {args.NewUserId} does not exist.");

        var previousOwner = task.AssignedToId;
        task.AssignedToId = args.NewUserId;
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name, Action = AgentActions.Reassigned, TaskId = task.Id,
            Details = $"Owner {previousOwner?.ToString() ?? "none"} -> {args.NewUserId?.ToString() ?? "unassigned"}. {args.Reason}",
            Success = true, CreatedAt = DateTime.UtcNow
        }, ct);

        return ToolResult(toolUse, $"Reassigned Task {task.Id} ('{task.Title}') to {(args.NewUserId?.ToString() ?? "unassigned")}.");
    }

    private async Task<ContentBase> FlagAsync(ToolUseContent toolUse, CancellationToken ct)
    {
        var args = toolUse.Input.Deserialize<FlagArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize flag_for_review arguments.");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: ct);
        if (task is null) return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name, Action = AgentActions.FlaggedForReview, TaskId = task.Id,
            Details = args.Concern, Success = true, CreatedAt = DateTime.UtcNow
        }, ct);

        return ToolResult(toolUse, $"Flagged Task {task.Id} ('{task.Title}') for human review.");
    }

    private static string BuildPrompt(List<TaskItem> staleTasks, List<AgentLog> recentActions, string contextJson, int thresholdHours)
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
