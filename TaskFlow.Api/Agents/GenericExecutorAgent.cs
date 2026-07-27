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
/// Executes generic tasks. Each cycle it atomically claims the oldest <see cref="WorkflowStatus.Todo"/>
/// task of <see cref="TaskKind.Generic"/>, works it through Claude, and hands it to
/// <see cref="WorkflowStatus.Review"/> for a human. It never sets Done — approval is a human step.
///
/// The conversation mechanics live in <see cref="ClaudeAgentBase"/>; this class supplies the two
/// tools (record_progress, request_review), the prompt, and the per-tool handlers, all bound to the
/// single task claimed this cycle so Claude never has to pass a task id it could get wrong.
/// </summary>
public class GenericExecutorAgent : ClaudeAgentBase
{
    private const string ProgressTool = "record_progress";
    private const string ReviewTool = "request_review";

    private readonly ITaskRepository _tasks;

    public GenericExecutorAgent(
        IClaudeClient claude,
        ITaskRepository tasks,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger<GenericExecutorAgent> logger)
        : base(claude, logs, notifier, config, logger)
    {
        _tasks = tasks;
    }

    public override string Name => "GenericExecutor";

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:ExecutorIntervalMinutes", 15));

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Without Claude we cannot do the work, so do not claim a task we would leave stuck InProgress.
        if (!ClaudeConfigured)
        {
            Logger.LogWarning("[{Agent}] Anthropic API key not configured. Skipping cycle.", Name);
            return;
        }

        // ── CLAIM ──────────────────────────────────────────────────────────────────
        var task = await _tasks.TryClaimNextAsync(TaskKind.Generic, Name, cancellationToken);
        if (task is null)
        {
            Logger.LogInformation("[{Agent}] No Todo task to execute. Skipping cycle.", Name);
            return;
        }

        Logger.LogInformation("[{Agent}] Claimed Task {Id} '{Title}'.", Name, task.Id, task.Title);
        await NotifyCycleStartedAsync(cancellationToken);

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.Claimed,
            TaskId = task.Id,
            Details = $"Claimed '{task.Title}' for execution.",
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        // ── REASON + ACT ───────────────────────────────────────────────────────────
        // Bind the claimed task to the dispatcher so the tools act on exactly this task.
        var actions = await RunToolConversationAsync(
            prompt: BuildPrompt(task),
            tools: BuildTools(),
            dispatch: (toolUse, ct) => ExecuteToolAsync(task, toolUse, ct),
            cancellationToken);

        Logger.LogInformation(
            "[{Agent}] Cycle complete for Task {Id}. {Count} tool action(s).", Name, task.Id, actions);

        // ── TERMINAL-STATE GUARANTEE (T4.4) ────────────────────────────────────────
        // If Claude ended without calling request_review, the task is still InProgress. The guarded
        // MarkForReviewAsync moves it to Review only in that case (it no-ops if request_review already
        // moved it), so a claimed task is never orphaned InProgress. Abnormal termination (an
        // exception mid-cycle) is a separate concern handled by Sprint 6 rollback-to-Todo.
        var autoFinalized = await _tasks.MarkForReviewAsync(task.Id, cancellationToken);
        if (autoFinalized)
        {
            await RecordActionAsync(new AgentLog
            {
                AgentName = Name,
                Action = AgentActions.AutoFinalized,
                TaskId = task.Id,
                Details = "Claude ended its turn without requesting review; auto-finalized to Review.",
                Success = true,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);

            Logger.LogInformation(
                "[{Agent}] Task {Id} auto-finalized to Review (no explicit review requested).", Name, task.Id);
        }

        await NotifyCycleCompletedAsync(cancellationToken);
    }

    // ── TOOL DEFINITIONS ───────────────────────────────────────────────────────────
    private static List<Tool> BuildTools() =>
    [
        DefineTool(
            ProgressTool,
            "Record a short progress note as you work the task. Use it to log your plan or a step you completed.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["note"] = new { type = "string", description = "One or two sentences describing the progress." }
                },
                required = new[] { "note" }
            }),

        DefineTool(
            ReviewTool,
            "Hand the task to a human for review once the work is complete. This moves the task to Review. " +
            "Always call this exactly once when you are done.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["summary"] = new { type = "string", description = "A short summary of what was done." }
                },
                required = new[] { "summary" }
            })
    ];

    // ── TOOL DISPATCH ──────────────────────────────────────────────────────────────
    private async Task<ContentBase> ExecuteToolAsync(
        TaskItem task,
        ToolUseContent toolUse,
        CancellationToken cancellationToken)
    {
        try
        {
            return toolUse.Name switch
            {
                ProgressTool => await RecordProgressAsync(task, toolUse, cancellationToken),
                ReviewTool   => await RequestReviewAsync(task, toolUse, cancellationToken),
                _            => ToolResult(toolUse, $"Error: unknown tool {toolUse.Name}")
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Agent}] Tool execution failed for {Tool}", Name, toolUse.Name);
            return ToolResult(toolUse, $"Error: {ex.Message}");
        }
    }

    // ── RECORD PROGRESS ────────────────────────────────────────────────────────────
    // Log only; does not change the task's status. That is intentional.
    private async Task<ContentBase> RecordProgressAsync(
        TaskItem task, ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<ProgressArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize record_progress arguments.");

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.ProgressRecorded,
            TaskId = task.Id,
            Details = args.Note,
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        Logger.LogInformation("[{Agent}] Progress on Task {Id}: {Note}", Name, task.Id, args.Note);
        return ToolResult(toolUse, $"Progress recorded for Task {task.Id}.");
    }

    // ── REQUEST REVIEW ─────────────────────────────────────────────────────────────
    private async Task<ContentBase> RequestReviewAsync(
        TaskItem task, ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<ReviewArgs>()
            ?? throw new InvalidOperationException("Failed to deserialize request_review arguments.");

        // Guarded transition in the repository (InProgress -> Review). Done via ExecuteUpdate rather
        // than mutating a tracked entity, because the claimed task came back from a no-tracking read.
        var moved = await _tasks.MarkForReviewAsync(task.Id, cancellationToken);
        if (!moved)
            return ToolResult(toolUse, $"Task {task.Id} was not InProgress; nothing to move to Review.");

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.ReviewRequested,
            TaskId = task.Id,
            Details = args.Summary,
            Success = true,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        Logger.LogInformation("[{Agent}] Task {Id} moved to Review: {Summary}", Name, task.Id, args.Summary);
        return ToolResult(toolUse, $"Task {task.Id} moved to Review.");
    }

    // ── PROMPT BUILDER ─────────────────────────────────────────────────────────────
    private static string BuildPrompt(TaskItem task)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an autonomous task-execution agent for a software team.");
        sb.AppendLine("You cannot write files or change the codebase yourself. Your job is to reason about the");
        sb.AppendLine("task, record a brief plan and any progress, then hand the task to a human for review.");
        sb.AppendLine();
        sb.AppendLine($"Task {task.Id}: {task.Title}");
        if (!string.IsNullOrWhiteSpace(task.Description))
            sb.AppendLine($"Description: {task.Description}");
        sb.AppendLine();
        sb.AppendLine("How to proceed:");
        sb.AppendLine("  1. Think through what completing this task requires.");
        sb.AppendLine("  2. Call record_progress with a short note on your plan (one or two sentences; do NOT paste code or file contents).");
        sb.AppendLine("  3. Call request_review with a one-paragraph summary. This hands the task to a human.");
        sb.AppendLine("Finish by calling request_review exactly once. Keep messages short and do not output code or file contents.");
        return sb.ToString();
    }

    // ── ARGUMENT RECORDS ───────────────────────────────────────────────────────────
    private sealed record ProgressArgs([property: JsonPropertyName("note")] string Note);
    private sealed record ReviewArgs([property: JsonPropertyName("summary")] string Summary);
}
