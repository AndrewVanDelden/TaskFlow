using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using Tool = Anthropic.SDK.Common.Tool;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Autonomously re-prioritizes open tasks using Claude. Each cycle it reads the
/// open tasks, asks Claude to re-rank them via a single <c>update_task_priority</c>
/// tool, applies each decision, and logs a summary.
///
/// The Claude conversation mechanics live in <see cref="ClaudeAgentBase"/>; this
/// class only supplies the prompt, the tool, and the per-tool handler.
/// </summary>
public class TaskPrioritizerAgent : ClaudeAgentBase
{
    private const string UpdatePriorityTool = "update_task_priority";

    public TaskPrioritizerAgent(
        AppDbContext db,
        IConfiguration config,
        ILogger<TaskPrioritizerAgent> logger,
        IAgentNotifier notifier)
        : base(db, config, logger, notifier)
    {
    }

    public override string Name => "TaskPrioritizer";

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:PrioritizerIntervalMinutes", 30));

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        // ── OBSERVE ──────────────────────────────────────────────────────────────
        var tasks = await Db.Tasks
            .Include(t => t.AssignedTo)
            .Where(t => t.Status != Models.TaskStatus.Done)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            Logger.LogInformation("[{Agent}] No open tasks. Skipping cycle.", Name);
            return;
        }

        Logger.LogInformation("[{Agent}] Analyzing {Count} open task(s)...", Name, tasks.Count);
        await NotifyCycleStartedAsync(cancellationToken);

        // ── REASON + ACT ─────────────────────────────────────────────────────────
        if (!TryCreateClaudeClient(out var client, out var model, out var maxTokens))
            return;

        var updatesApplied = await RunToolConversationAsync(
            client, model, maxTokens,
            prompt: BuildPrompt(tasks),
            tools: BuildTools(),
            dispatch: ExecuteToolAsync,
            cancellationToken);

        // ── CYCLE SUMMARY ────────────────────────────────────────────────────────
        Logger.LogInformation(
            "[{Agent}] Cycle complete. {Updates} priority update(s) applied.", Name, updatesApplied);

        Db.AgentLogs.Add(new AgentLog
        {
            AgentName = Name,
            Action = updatesApplied > 0 ? AgentActions.PrioritiesUpdated : AgentActions.NoChangesNeeded,
            Details = $"Analyzed {tasks.Count} task(s). Applied {updatesApplied} priority update(s).",
            Success = true,
            CreatedAt = DateTime.UtcNow
        });

        await NotifyCycleCompletedAsync(cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
    }

    // ── TOOL DEFINITION ──────────────────────────────────────────────────────────
    private static List<Tool> BuildTools() =>
    [
        DefineTool(
            UpdatePriorityTool,
            "Updates the priority of a task. Call this once per task that needs a priority change.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The numeric ID of the task to update." },
                    ["priority"] = new
                    {
                        type = "string",
                        @enum = new[] { "Low", "Medium", "High" },
                        description = "The new priority level."
                    },
                    ["reasoning"] = new { type = "string", description = "One sentence explaining why this priority was chosen." }
                },
                required = new[] { "task_id", "priority", "reasoning" }
            })
    ];

    // ── TOOL HANDLER ─────────────────────────────────────────────────────────────
    private async Task<ContentBase> ExecuteToolAsync(
        ToolUseContent toolUse,
        CancellationToken cancellationToken)
    {
        if (toolUse.Name != UpdatePriorityTool)
            return ToolResult(toolUse, $"Error: unknown tool {toolUse.Name}");

        var args = toolUse.Input.Deserialize<UpdatePriorityArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize update_task_priority arguments.");

        if (!Enum.TryParse<TaskPriority>(args.Priority, ignoreCase: true, out var priority))
            return ToolResult(toolUse, $"Invalid priority value: {args.Priority}");

        var task = await Db.Tasks.FindAsync([args.TaskId], cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        var previousPriority = task.Priority;
        task.Priority = priority;
        task.UpdatedAt = DateTime.UtcNow;

        // RecordActionAsync also persists the task change, since it calls SaveChangesAsync.
        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.PriorityUpdated,
            TaskId = task.Id,
            Details = $"Priority {previousPriority} -> {priority}. {args.Reasoning}",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        Logger.LogInformation(
            "[{Agent}] Updated Task {Id} '{Title}': {From} -> {To}. Reason: {Reason}",
            Name, task.Id, task.Title, previousPriority, priority, args.Reasoning);

        return ToolResult(toolUse,
            $"Updated Task {task.Id} ('{task.Title}') priority: {previousPriority} -> {priority}.");
    }

    // ── PROMPT BUILDER ───────────────────────────────────────────────────────────
    private static string BuildPrompt(List<TaskItem> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a task prioritization agent for a software development team.");
        sb.AppendLine("Analyze the following open tasks and update their priorities using the update_task_priority tool.");
        sb.AppendLine();
        sb.AppendLine("Prioritization rules:");
        sb.AppendLine("- High: overdue or due within 2 days, or blocking other work");
        sb.AppendLine("- Medium: due within 1 week, or important but not urgent");
        sb.AppendLine("- Low: no due date, or due more than 1 week away");
        sb.AppendLine("- Only call the tool for tasks whose priority should CHANGE. Leave correct priorities alone.");
        sb.AppendLine();
        sb.AppendLine($"Current date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("Open tasks:");

        foreach (var task in tasks)
        {
            sb.AppendLine($"  ID: {task.Id}");
            sb.AppendLine($"  Title: {task.Title}");
            sb.AppendLine($"  Current Priority: {task.Priority}");
            sb.AppendLine($"  Status: {task.Status}");
            sb.AppendLine($"  Due Date: {(task.DueDate.HasValue ? task.DueDate.Value.ToString("yyyy-MM-dd") : "none")}");
            sb.AppendLine($"  Assigned To: {task.AssignedTo?.Name ?? "unassigned"}");
            sb.AppendLine();
        }

        sb.AppendLine("Use the update_task_priority tool for each task that needs a priority change, then finish.");
        return sb.ToString();
    }

    // ── ARGUMENT RECORD ──────────────────────────────────────────────────────────
    // Maps directly to the JSON arguments Claude sends for the update_task_priority tool.
    private sealed record UpdatePriorityArgs(
        [property: JsonPropertyName("task_id")] int TaskId,
        [property: JsonPropertyName("priority")] string Priority,
        [property: JsonPropertyName("reasoning")] string Reasoning);
}
