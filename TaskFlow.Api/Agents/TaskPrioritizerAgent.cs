using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Autonomously re-prioritizes open tasks using Claude.
/// Runs on a configurable interval. Each cycle:
///   1. Reads all open tasks from the database
///   2. Sends them to Claude with an UpdateTaskPriority tool
///   3. Executes Claude's priority decisions
///   4. Logs the results
/// </summary>
public class TaskPrioritizerAgent : ITaskFlowAgent
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<TaskPrioritizerAgent> _logger;

    public string Name => "TaskPrioritizer";

    public TimeSpan Interval =>
        TimeSpan.FromMinutes(_config.GetValue<int>("Agents:PrioritizerIntervalMinutes", 30));

    public TaskPrioritizerAgent(
        AppDbContext db,
        IConfiguration config,
        ILogger<TaskPrioritizerAgent> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // ── OBSERVE ────────────────────────────────────────────────────────────
        var tasks = await _db.Tasks
            .Include(t => t.AssignedTo)
            .Where(t => t.Status != Models.TaskStatus.Done)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        if (tasks.Count == 0)
        {
            _logger.LogInformation("[{Agent}] No open tasks. Skipping cycle.", Name);
            return;
        }

        _logger.LogInformation("[{Agent}] Analyzing {Count} open task(s)...", Name, tasks.Count);

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

        // Build the tool Claude will use to update priorities.
        // MessageParameters.Tools expects Anthropic.SDK.Common.Tool, whose schema is a
        // raw JSON node/string on Function — not the Messaging.InputSchema/Property types.
        var updatePriorityInputSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["task_id"] = new
                {
                    type = "integer",
                    description = "The numeric ID of the task to update."
                },
                ["priority"] = new
                {
                    type = "string",
                    @enum = new[] { "Low", "Medium", "High" },
                    description = "The new priority level."
                },
                ["reasoning"] = new
                {
                    type = "string",
                    description = "One sentence explaining why this priority was chosen."
                }
            },
            required = new[] { "task_id", "priority", "reasoning" }
        });

        var tools = new List<Anthropic.SDK.Common.Tool>
        {
            new Function(
                "update_task_priority",
                "Updates the priority of a task. Call this once per task that needs a priority change.",
                updatePriorityInputSchema)
        };

        // Build the user prompt with current task state
        var prompt = BuildPrompt(tasks);

        var messages = new List<Message>
        {
            new Message(RoleType.User, prompt)
        };

        // ── TOOL USE LOOP ─────────────────────────────────────────────────────
        // Claude may call the tool multiple times (once per task it wants to update).
        // We keep looping until Claude signals it is finished (end_turn).
        var updatesApplied = 0;
        var continueLoop = true;

        while (continueLoop && !cancellationToken.IsCancellationRequested)
        {
            var response = await client.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model = model,
                    MaxTokens = maxTokens,
                    Tools = tools,
                    Messages = messages
                },
                cancellationToken);

            // Add Claude's response to the conversation history, preserving the structured
            // content blocks (including ToolUseContent) so subsequent tool_result blocks
            // have a matching tool_use block to reference.
            messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            if (response.StopReason == "tool_use")
            {
                // Claude wants to call a tool — find all tool_use blocks in the response
                var toolUseBlocks = response.Content
                    .OfType<ToolUseContent>()
                    .ToList();

                var toolResults = new List<ContentBase>();

                foreach (var toolUse in toolUseBlocks)
                {
                    var result = await ExecuteToolAsync(toolUse, cancellationToken);
                    toolResults.Add(result);

                    if (result is ToolResultContent tr && tr.Content?.ToString()?.Contains("Updated") == true)
                        updatesApplied++;
                }

                // Send the tool results back to Claude so it can continue reasoning
                messages.Add(new Message { Role = RoleType.User, Content = toolResults.Cast<ContentBase>().ToList() });
            }
            else
            {
                // StopReason is "end_turn" — Claude is done
                continueLoop = false;

                // Log any final text Claude included in its response
                var finalText = response.Content
                    .OfType<TextContent>()
                    .FirstOrDefault()?.Text;

                if (!string.IsNullOrWhiteSpace(finalText))
                    _logger.LogInformation("[{Agent}] Claude summary: {Text}", Name, finalText);
            }
        }

        // ── LOG RESULTS ────────────────────────────────────────────────────────
        _logger.LogInformation(
            "[{Agent}] Cycle complete. {Updates} priority update(s) applied.", Name, updatesApplied);

        await _db.AgentLogs.AddAsync(new AgentLog
        {
            AgentName = Name,
            Action = updatesApplied > 0 ? "PrioritiesUpdated" : "NoChangesNeeded",
            Details = $"Analyzed {tasks.Count} task(s). Applied {updatesApplied} priority update(s).",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ── TOOL EXECUTION ─────────────────────────────────────────────────────────
    private async Task<ContentBase> ExecuteToolAsync(
        ToolUseContent toolUse,
        CancellationToken cancellationToken)
    {
        if (toolUse.Name != "update_task_priority")
        {
            return new ToolResultContent
            {
                ToolUseId = toolUse.Id,
                Content = new List<ContentBase> { new TextContent { Text = $"Unknown tool: {toolUse.Name}" } }
            };
        }

        try
        {
            // Parse the arguments Claude sent
            var args = toolUse.Input.Deserialize<UpdatePriorityArgs>()
                ?? throw new InvalidOperationException("Failed to deserialize tool arguments.");

            // Map the string Claude returns to your enum
            if (!Enum.TryParse<TaskPriority>(args.Priority, ignoreCase: true, out var priority))
            {
                return new ToolResultContent
                {
                    ToolUseId = toolUse.Id,
                    Content = new List<ContentBase> { new TextContent { Text = $"Invalid priority value: {args.Priority}" } }
                };
            }

            // ACT — write to the database
            var task = await _db.Tasks.FindAsync(new object[] { args.TaskId }, cancellationToken);
            if (task is null)
            {
                return new ToolResultContent
                {
                    ToolUseId = toolUse.Id,
                    Content = new List<ContentBase> { new TextContent { Text = $"Task {args.TaskId} not found." } }
                };
            }

            var previousPriority = task.Priority;
            task.Priority = priority;
            task.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[{Agent}] Updated Task {Id} '{Title}': {From} → {To}. Reason: {Reason}",
                Name, task.Id, task.Title, previousPriority, priority, args.Reasoning);

            return new ToolResultContent
            {
                ToolUseId = toolUse.Id,
                Content = new List<ContentBase> { new TextContent { Text = $"Updated Task {task.Id} ('{task.Title}') priority: {previousPriority} → {priority}." } }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Agent}] Tool execution failed for tool use {Id}", Name, toolUse.Id);
            return new ToolResultContent
            {
                ToolUseId = toolUse.Id,
                Content = new List<ContentBase> { new TextContent { Text = $"Error: {ex.Message}" } }
            };
        }
    }

    // ── PROMPT BUILDER ─────────────────────────────────────────────────────────
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

    // ── ARGUMENT DTO ───────────────────────────────────────────────────────────
    // This maps directly to what Claude sends back in the tool call arguments
    private sealed record UpdatePriorityArgs(
        [property: JsonPropertyName("task_id")] int TaskId,
        [property: JsonPropertyName("priority")] string Priority,
        [property: JsonPropertyName("reasoning")] string Reasoning
    );
}