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
/// Autonomously re-prioritizes open tasks using Claude. Supplies only its policy — the tool,
/// the prompt, and the per-tool handler; the conversation loop lives in ClaudeAgentBase.
/// </summary>
public class TaskPrioritizerAgent : ClaudeAgentBase
{
    private const string UpdatePriorityTool = "update_task_priority";

    private readonly ITaskRepository _tasks;

    public TaskPrioritizerAgent(
        IClaudeClient claude,
        ITaskRepository tasks,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger<TaskPrioritizerAgent> logger)
        : base(claude, logs, notifier, config, logger)
    {
        _tasks = tasks;
    }

    public override string Name => "TaskPrioritizer";

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:PrioritizerIntervalMinutes", 30));

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        var tasks = await _tasks.GetOpenAsync(cancellationToken);
        if (tasks.Count == 0)
        {
            Logger.LogInformation("[{Agent}] No open tasks. Skipping cycle.", Name);
            return;
        }

        Logger.LogInformation("[{Agent}] Analyzing {Count} open task(s)...", Name, tasks.Count);
        await NotifyCycleStartedAsync(cancellationToken);

        if (!ClaudeConfigured)
        {
            Logger.LogWarning("[{Agent}] Claude not configured. Skipping cycle.", Name);
            return;
        }

        var updatesApplied = await RunToolConversationAsync(
            BuildPrompt(tasks), BuildTools(), ExecuteToolAsync, cancellationToken);

        Logger.LogInformation("[{Agent}] Cycle complete. {Updates} update(s) applied.", Name, updatesApplied);

        await Logs.AddAsync(new AgentLog
        {
            AgentName = Name,
            Action = updatesApplied > 0 ? AgentActions.PrioritiesUpdated : AgentActions.NoChangesNeeded,
            Details = $"Analyzed {tasks.Count} task(s). Applied {updatesApplied} priority update(s).",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        await Logs.SaveChangesAsync(cancellationToken);

        await NotifyCycleCompletedAsync(cancellationToken);
    }

    private static List<Tool> BuildTools() =>
    [
        DefineTool(
            UpdatePriorityTool,
            "Updates the priority of a task. Call once per task that needs a priority change.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["task_id"] = new { type = "integer", description = "The numeric ID of the task." },
                    ["priority"] = new { type = "string", @enum = new[] { "Low", "Medium", "High" }, description = "The new priority." },
                    ["reasoning"] = new { type = "string", description = "One sentence explaining the choice." }
                },
                required = new[] { "task_id", "priority", "reasoning" }
            })
    ];

    private async Task<ContentBase> ExecuteToolAsync(ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        if (toolUse.Name != UpdatePriorityTool)
            return ToolResult(toolUse, $"Error: unknown tool {toolUse.Name}");

        var args = toolUse.Input.Deserialize<UpdatePriorityArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize update_task_priority arguments.");

        if (!Enum.TryParse<TaskPriority>(args.Priority, ignoreCase: true, out var priority))
            return ToolResult(toolUse, $"Invalid priority value: {args.Priority}");

        var task = await _tasks.GetByIdAsync(args.TaskId, ct: cancellationToken);
        if (task is null)
            return ToolResult(toolUse, $"Task {args.TaskId} not found.");

        var previous = task.Priority;
        task.Priority = priority;
        task.UpdatedAt = DateTime.UtcNow;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.PriorityUpdated,
            TaskId = task.Id,
            Details = $"Priority {previous} -> {priority}. {args.Reasoning}",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        return ToolResult(toolUse, $"Updated Task {task.Id} ('{task.Title}') priority: {previous} -> {priority}.");
    }

    private static string BuildPrompt(List<TaskItem> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a task prioritization agent for a software development team.");
        sb.AppendLine("Update priorities using the update_task_priority tool.");
        sb.AppendLine("- High: overdue or due within 2 days, or blocking other work");
        sb.AppendLine("- Medium: due within 1 week, or important but not urgent");
        sb.AppendLine("- Low: no due date, or due more than 1 week away");
        sb.AppendLine("- Only call the tool for tasks whose priority should CHANGE.");
        sb.AppendLine($"Current date (UTC): {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        foreach (var t in tasks)
        {
            sb.AppendLine($"  ID {t.Id}: {t.Title} | Priority {t.Priority} | Status {t.Status} | " +
                          $"Due {(t.DueDate.HasValue ? t.DueDate.Value.ToString("yyyy-MM-dd") : "none")} | " +
                          $"Assignee {t.AssignedTo?.Name ?? "unassigned"}");
        }
        sb.AppendLine();
        sb.AppendLine("Call the tool for each task that needs a change, then finish.");
        return sb.ToString();
    }

    private sealed record UpdatePriorityArgs(
        [property: JsonPropertyName("task_id")] int TaskId,
        [property: JsonPropertyName("priority")] string Priority,
        [property: JsonPropertyName("reasoning")] string Reasoning);
}
