using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Detects tasks that have gone stale and takes corrective action via Claude.
/// Unlike the Prioritizer, this agent selects between three tools per task
/// and uses its own AgentLog history as memory to avoid repeating actions.
/// </summary>
public class StaleTaskAgent : ITaskFlowAgent
{
   private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<StaleTaskAgent> _logger;
    private readonly IAgentNotifier _notifier;

    // Safety cap so a misbehaving tool loop cannot run unbounded against the API.
    private const int MaxToolLoopIterations = 10;

    public string Name => "StaleTaskDetector";

   public StaleTaskAgent(
        AppDbContext db,
        IConfiguration config,
        ILogger<StaleTaskAgent> logger,
        IAgentNotifier notifier)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _notifier = notifier;
    }

    public TimeSpan Interval =>
        TimeSpan.FromMinutes(_config.GetValue<int>("Agents:StaleTaskIntervalMinutes", 60));


    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // ── OBSERVE ────────────────────────────────────────────────────────────
        var thresholdHours = _config.GetValue<int>("Agents:StaleTaskThresholdHours", 48);
        var cutoff = DateTime.UtcNow.AddHours(-thresholdHours);

        var staleTasks = await _db.Tasks
            .Include(t => t.AssignedTo)
            .Where(t => t.Status != Models.WorkflowStatus.Done && t.UpdatedAt < cutoff)
            .OrderBy(t => t.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (staleTasks.Count == 0)
        {
            _logger.LogInformation("[{Agent}] No stale tasks found. Skipping cycle.", Name);
            return;
        }

        _logger.LogInformation(
            "[{Agent}] Found {Count} stale task(s) (>{Hours}h without update).",
            Name, staleTasks.Count, thresholdHours);

        await _notifier.AgentCycleAsync(Name, "started", cancellationToken);

        // MEMORY — what has this agent already done in the last 7 days?
        var memoryCutoff = DateTime.UtcNow.AddDays(-7);
        var recentActions = await _db.AgentLogs
            .Where(l => l.AgentName == Name && l.CreatedAt > memoryCutoff && l.TaskId != null)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        // Workload context so Claude can judge "overloaded"
        var workload = await _db.Tasks
            .Where(t => t.Status != Models.WorkflowStatus.Done && t.AssignedToId != null)
            .GroupBy(t => t.AssignedToId!.Value)
            .Select(g => new { UserId = g.Key, OpenCount = g.Count() })
            .ToListAsync(cancellationToken);

        var users = await _db.Users
            .Select(u => new { u.Id, u.Name })
            .ToListAsync(cancellationToken);

        // Serialize here, where the concrete types are still known.
        var contextJson = JsonSerializer.Serialize(new { users, workload });

        // ── REASON ────────────────────────────────────────────────────────────
        var apiKey = _config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[{Agent}] Anthropic API key not configured. Skipping cycle.", Name);
            return;
        }

        var client = new AnthropicClient(apiKey);
        var model = _config["Anthropic:Model"] ?? "claude-sonnet-4-6";
        var maxTokens = _config.GetValue<int>("Anthropic:MaxTokens", 1024);

        var tools = BuildTools();
        var prompt = BuildPrompt(staleTasks, recentActions, contextJson, thresholdHours);

        var messages = new List<Message>
        {
            new Message(RoleType.User, prompt)
        };

        // ── TOOL USE LOOP ─────────────────────────────────────────────────────
        var actionsApplied = 0;
        var continueLoop = true;
        var iterations = 0;

        while (continueLoop
               && iterations < MaxToolLoopIterations
               && !cancellationToken.IsCancellationRequested)
        {
            iterations++;

            var response = await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model = model,
                    MaxTokens = maxTokens,
                    Tools = tools,
                    Messages = messages
                },
                cancellationToken);

            // Preserve the structured content blocks so tool_result blocks can
            // reference their matching tool_use block.
            messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            if (response.StopReason == "tool_use")
            {
                var toolUseBlocks = response.Content.OfType<ToolUseContent>().ToList();
                var toolResults = new List<ContentBase>();

                foreach (var toolUse in toolUseBlocks)
                {
                    var result = await ExecuteToolAsync(toolUse, cancellationToken);
                    toolResults.Add(result);

                    if (WasSuccessful(result))
                        actionsApplied++;
                }

                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = toolResults
                });
            }
            else
            {
                // StopReason is "end_turn" — Claude is done
                continueLoop = false;

                var finalText = response.Content
                    .OfType<TextContent>()
                    .FirstOrDefault()?.Text;

                if (!string.IsNullOrWhiteSpace(finalText))
                    _logger.LogInformation("[{Agent}] Claude summary: {Text}", Name, finalText);
            }
        }

        if (continueLoop && iterations >= MaxToolLoopIterations)
        {
            _logger.LogWarning(
                "[{Agent}] Hit max tool-loop iterations ({Max}). Ending cycle early.",
                Name, MaxToolLoopIterations);
        }

        // ── CYCLE SUMMARY ──────────────────────────────────────────────────────
        _logger.LogInformation(
            "[{Agent}] Cycle complete. {Count} action(s) taken.", Name, actionsApplied);

        await _db.AgentLogs.AddAsync(new AgentLog
        {
            AgentName = Name,
            Action = actionsApplied > 0 ? "CycleActions" : "NoActionNeeded",
            TaskId = null,   // cycle-level summary, not tied to one task
            Details = $"Reviewed {staleTasks.Count} stale task(s). Took {actionsApplied} action(s).",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        await _notifier.AgentCycleAsync(Name, "completed", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // ── TOOL DEFINITIONS ───────────────────────────────────────────────────────
    private static List<Anthropic.SDK.Common.Tool> BuildTools()
    {
        var escalateSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["task_id"] = new { type = "integer", description = "The ID of the task to escalate." },
                ["reason"] = new { type = "string", description = "One sentence explaining why this needs escalation." }
            },
            required = new[] { "task_id", "reason" }
        });

        var reassignSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["task_id"] = new { type = "integer", description = "The ID of the task to reassign." },
                ["new_user_id"] = new { type = "integer", description = "Target user ID. Omit to unassign the task." },
                ["reason"] = new { type = "string", description = "One sentence explaining the reassignment." }
            },
            required = new[] { "task_id", "reason" }
        });

        var flagSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["task_id"] = new { type = "integer", description = "The ID of the task to flag." },
                ["concern"] = new { type = "string", description = "What specifically a human should look at." }
            },
            required = new[] { "task_id", "concern" }
        });

        return new List<Anthropic.SDK.Common.Tool>
        {
            new Function(
                "escalate_task",
                "Escalate a stale but important task by setting its priority to High. " +
                "Use when the task is clearly still needed and is overdue. " +
                "Do NOT use if the task is already High priority - flag it for review instead.",
                escalateSchema),

            new Function(
                "reassign_task",
                "Reassign a task to a different user, or unassign it so it returns to the pool. " +
                "Use when the task is unassigned and needs an owner, or when the current owner " +
                "has 5 or more open tasks and is overloaded.",
                reassignSchema),

            new Function(
                "flag_for_review",
                "Flag a task for human review without modifying it. " +
                "Use when the right action is ambiguous, the task may no longer be relevant, " +
                "or the decision requires context you do not have. Prefer this over guessing.",
                flagSchema)
        };
    }

    // ── TOOL DISPATCH ──────────────────────────────────────────────────────────
    private async Task<ContentBase> ExecuteToolAsync(
        ToolUseContent toolUse,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolUse.Name switch
            {
                "escalate_task"   => await EscalateAsync(toolUse, cancellationToken),
                "reassign_task"   => await ReassignAsync(toolUse, cancellationToken),
                "flag_for_review" => await FlagAsync(toolUse, cancellationToken),
                _                 => ToolResult(toolUse, $"Error: unknown tool {toolUse.Name}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Agent}] Tool execution failed for {Tool}", Name, toolUse.Name);
            return ToolResult(toolUse, $"Error: {ex.Message}");
        }
    }

    // Builds the ToolResultContent shape the SDK expects.
    private static ToolResultContent ToolResult(ToolUseContent toolUse, string text) =>
        new ToolResultContent
        {
            ToolUseId = toolUse.Id,
            Content = new List<ContentBase> { new TextContent { Text = text } }
        };

    // A tool call only counts as an action if it did not report an error.
    private static bool WasSuccessful(ContentBase result)
    {
        var text = (result as ToolResultContent)?.Content?
            .OfType<TextContent>()
            .FirstOrDefault()?.Text ?? string.Empty;

        return !text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("not found", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    // Persists an agent log and broadcasts it to any connected dashboards.
    // All three tool handlers record actions this way, so the persist-and-notify
    // steps live here once. Each handler still builds its own log contents.
    private async Task RecordActionAsync(AgentLog log, CancellationToken cancellationToken)
    {
        _db.AgentLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.AgentActionAsync(log, cancellationToken);
    }

    // ── ESCALATE ───────────────────────────────────────────────────────────────
    private async Task<ContentBase> EscalateAsync(
        ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<EscalateArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize escalate_task arguments.");

        var task = await _db.Tasks.FindAsync(new object[] { args.TaskId }, cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        var previous = task.Priority;
        task.Priority = TaskPriority.High;
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = "Escalated",
            TaskId = task.Id,
            Details = $"Priority {previous} -> High. {args.Reason}",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "[{Agent}] Escalated Task {Id} '{Title}': {From} -> High. {Reason}",
            Name, task.Id, task.Title, previous, args.Reason);

        return ToolResult(toolUse,
            $"Escalated Task {task.Id} ('{task.Title}') from {previous} to High.");
    }

    // ── REASSIGN ───────────────────────────────────────────────────────────────
    private async Task<ContentBase> ReassignAsync(
        ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<ReassignArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize reassign_task arguments.");

        var task = await _db.Tasks.FindAsync(new object[] { args.TaskId }, cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        if (args.NewUserId.HasValue)
        {
            var exists = await _db.Users
                .AnyAsync(u => u.Id == args.NewUserId.Value, cancellationToken);
            if (!exists)
                return ToolResult(toolUse, $"User {args.NewUserId} does not exist.");
        }

        var previousOwner = task.AssignedToId;
        task.AssignedToId = args.NewUserId;   // null = unassign
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = "Reassigned",
            TaskId = task.Id,
            Details = $"Owner {previousOwner?.ToString() ?? "none"} -> " +
                      $"{args.NewUserId?.ToString() ?? "unassigned"}. {args.Reason}",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "[{Agent}] Reassigned Task {Id} '{Title}': owner {From} -> {To}. {Reason}",
            Name, task.Id, task.Title,
            previousOwner?.ToString() ?? "none",
            args.NewUserId?.ToString() ?? "unassigned",
            args.Reason);

        return ToolResult(toolUse,
            $"Reassigned Task {task.Id} ('{task.Title}') to " +
            $"{(args.NewUserId?.ToString() ?? "unassigned")}.");
    }

    // ── FLAG FOR REVIEW ────────────────────────────────────────────────────────
    // Note: does NOT modify the task. Log only. That is intentional.
    private async Task<ContentBase> FlagAsync(
        ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<FlagArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize flag_for_review arguments.");

        var task = await _db.Tasks.FindAsync(new object[] { args.TaskId }, cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = "FlaggedForReview",
            TaskId = task.Id,
            Details = args.Concern,
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "[{Agent}] Flagged Task {Id} '{Title}' for review: {Concern}",
            Name, task.Id, task.Title, args.Concern);

        return ToolResult(toolUse,
            $"Flagged Task {task.Id} ('{task.Title}') for human review.");
    }

    // ── PROMPT BUILDER ─────────────────────────────────────────────────────────
    private static string BuildPrompt(
        List<TaskItem> staleTasks,
        List<AgentLog> recentActions,
        string contextJson,
        int thresholdHours)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a stale task detection agent for a software development team.");
        sb.AppendLine($"A task is stale if it is not Done and has not been updated in {thresholdHours}+ hours.");
        sb.AppendLine();
        sb.AppendLine("For each stale task, choose AT MOST ONE action:");
        sb.AppendLine("  - escalate_task    : still needed and overdue -> raise priority to High");
        sb.AppendLine("  - reassign_task    : unassigned, or the owner has 5+ open tasks");
        sb.AppendLine("  - flag_for_review  : ambiguous, possibly obsolete, or needs a human decision");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("  - Do NOT act on a task you already acted on recently (see history below).");
        sb.AppendLine("  - Do NOT escalate a task that is already High priority. Flag it instead.");
        sb.AppendLine("  - Prefer flag_for_review when uncertain. Do not guess.");
        sb.AppendLine("  - Taking no action at all is acceptable.");
        sb.AppendLine();
        sb.AppendLine($"Current date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();

        sb.AppendLine("=== STALE TASKS ===");
        foreach (var t in staleTasks)
        {
            var daysStale = (DateTime.UtcNow - t.UpdatedAt).TotalDays;
            sb.AppendLine($"  ID: {t.Id}");
            sb.AppendLine($"  Title: {t.Title}");
            sb.AppendLine($"  Status: {t.Status} | Priority: {t.Priority}");
            sb.AppendLine($"  Due: {(t.DueDate.HasValue ? t.DueDate.Value.ToString("yyyy-MM-dd") : "none")}");
            sb.AppendLine($"  Assigned To: {t.AssignedTo?.Name ?? "UNASSIGNED"} " +
                          $"(id: {t.AssignedToId?.ToString() ?? "null"})");
            sb.AppendLine($"  Days since last update: {daysStale:F1}");
            sb.AppendLine();
        }

        sb.AppendLine("=== TEAM WORKLOAD ===");
        sb.AppendLine(contextJson);
        sb.AppendLine();

        sb.AppendLine("=== YOUR RECENT ACTIONS (last 7 days) ===");
        if (recentActions.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var log in recentActions)
                sb.AppendLine($"  {log.CreatedAt:yyyy-MM-dd HH:mm} | Task {log.TaskId} | " +
                              $"{log.Action} | {log.Details}");
        }
        sb.AppendLine();

        sb.AppendLine("Call the appropriate tool for each task that needs action, then finish.");
        return sb.ToString();
    }

    // ── ARGUMENT RECORDS ───────────────────────────────────────────────────────
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
