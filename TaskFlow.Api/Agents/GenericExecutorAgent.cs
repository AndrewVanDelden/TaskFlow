using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
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
    private readonly IExecutorSwitch _switch;
    private readonly ISpendGuard _spendGuard;

    public GenericExecutorAgent(
        IClaudeClient claude,
        ITaskRepository tasks,
        IExecutorSwitch executorSwitch,
        ISpendGuard spendGuard,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger<GenericExecutorAgent> logger)
        : base(claude, logs, notifier, config, logger)
    {
        _tasks = tasks;
        _switch = executorSwitch;
        _spendGuard = spendGuard;
    }

    public override string Name => AgentNames.GenericExecutor;

    public override TimeSpan Interval =>
        TimeSpan.FromMinutes(Config.GetValue("Agents:ExecutorIntervalMinutes", 15));

    // User report (2026-08-24): pressing "Enable" (or toggling off then on) should run a cycle right
    // away, not wait out however much of the interval remains. Delegates to the switch itself, which
    // is the thing that actually knows when a human just re-enabled it.
    public override Task WaitForWakeSignalAsync(CancellationToken cancellationToken) =>
        _switch.WaitForWakeAsync(cancellationToken);

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        // ── GUARDS (run before claiming; each is a separate policy) ─────────────────
        if (!_switch.IsEnabled)
        {
            Logger.LogInformation("[{Agent}] Executor is paused. Skipping cycle.", Name);
            return;
        }

        // Without Claude we cannot do the work, so do not claim a task we would leave stuck InProgress.
        if (!ClaudeConfigured)
        {
            Logger.LogWarning("[{Agent}] Anthropic API key not configured. Skipping cycle.", Name);
            return;
        }

        if (!await _spendGuard.CanRunAsync(cancellationToken))
        {
            Logger.LogWarning("[{Agent}] Daily execution cap reached. Skipping cycle.", Name);
            return;
        }

        // ── CLAIM ──────────────────────────────────────────────────────────────────
        var task = await _tasks.TryClaimNextAsync(TaskKind.Generic, Name, cancellationToken);
        if (task is null)
        {
            // Nothing to claim. If the board still has open work (anything not Done), stay enabled and
            // keep polling; if the board is clear, pause the executor until a human turns it back on.
            var openCount = await _tasks.CountOpenAsync(cancellationToken);
            if (openCount == 0)
            {
                Logger.LogInformation("[{Agent}] Board is clear; pausing the executor.", Name);
                _switch.Disable();
            }
            else
            {
                Logger.LogInformation(
                    "[{Agent}] No Todo task to execute; {Open} open task(s) remain, staying enabled.", Name, openCount);
            }
            return;
        }

        try
        {
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
            }, task.OwnerId, cancellationToken);

            await NotifyTaskMovedAsync(task.Id, WorkflowStatus.InProgress, task.OwnerId, cancellationToken);

            // ── REASON + ACT ───────────────────────────────────────────────────────────
            // Fold any outstanding review feedback into the prompt so a rejection reason is not lost
            // across reworks (it may take several rejections before a human approves).
            var reasons = await OutstandingRejectionReasonsAsync(task.Id, cancellationToken);

            // Bind the claimed task to the dispatcher so the tools act on exactly this task.
            var actions = await RunToolConversationAsync(
                prompt: ExecutorPrompt.Build(task, reasons),
                tools: BuildTools(),
                dispatch: (toolUse, ct) => ExecuteToolAsync(task, toolUse, ct),
                cancellationToken);

            Logger.LogInformation(
                "[{Agent}] Cycle complete for Task {Id}. {Count} tool action(s).", Name, task.Id, actions);

            // A cancelled cycle is abnormal termination: roll back rather than finalize a half-done
            // task (T6.3), so a shutdown mid-work does not leave a card sitting in Review.
            if (cancellationToken.IsCancellationRequested)
            {
                await RollBackAsync(task, "cycle cancelled");
                return;
            }

            // ── TERMINAL-STATE GUARANTEE (T4.4) ────────────────────────────────────────
            // If Claude ended without calling request_review, the task is still InProgress. The guarded
            // MarkForReviewAsync moves it to Review only in that case (it no-ops if request_review already
            // moved it), so a claimed task is never orphaned InProgress.
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
                }, task.OwnerId, cancellationToken);

                await NotifyTaskMovedAsync(task.Id, WorkflowStatus.Review, task.OwnerId, cancellationToken);

                Logger.LogInformation(
                    "[{Agent}] Task {Id} auto-finalized to Review (no explicit review requested).", Name, task.Id);
            }

            await NotifyCycleCompletedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Abnormal termination (T6.3): release the claim so the task is never orphaned InProgress.
            Logger.LogError(ex, "[{Agent}] Cycle failed for Task {Id}; rolling back to Todo.", Name, task.Id);
            await RollBackAsync(task, ex.Message);
        }
    }

    // ── ROLLBACK (T6.3) ─────────────────────────────────────────────────────────────
    // Returns a claimed task to the pool. The guarded ReleaseClaimAsync no-ops if the task already
    // moved on (e.g. request_review reached Review before the failure), so this is safe to call.
    // Uses CancellationToken.None so the rollback completes even when the cycle itself was cancelled.
    private async Task RollBackAsync(TaskItem task, string reason)
    {
        var released = await _tasks.ReleaseClaimAsync(task.Id, CancellationToken.None);
        if (!released)
            return;

        await RecordActionAsync(new AgentLog
        {
            AgentName = Name,
            Action = AgentActions.RolledBack,
            TaskId = task.Id,
            Details = $"Rolled back to Todo: {reason}",
            Success = false,
            CreatedAt = DateTime.UtcNow
        }, task.OwnerId, CancellationToken.None);

        await NotifyTaskMovedAsync(task.Id, WorkflowStatus.Todo, task.OwnerId, CancellationToken.None);
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
        }, task.OwnerId, cancellationToken);

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
        }, task.OwnerId, cancellationToken);

        await NotifyTaskMovedAsync(task.Id, WorkflowStatus.Review, task.OwnerId, cancellationToken);

        Logger.LogInformation("[{Agent}] Task {Id} moved to Review: {Summary}", Name, task.Id, args.Summary);
        return ToolResult(toolUse, $"Task {task.Id} moved to Review.");
    }

    // ── REVIEW FEEDBACK ────────────────────────────────────────────────────────────
    // Prior rejection reasons for the task, oldest first, so the model can address all of them.
    // They persist as `Rejected` logs until the task is approved, so multiple rejections accumulate.
    private async Task<IReadOnlyList<string>> OutstandingRejectionReasonsAsync(int taskId, CancellationToken ct)
    {
        var rejections = await Logs.GetByTaskAndActionAsync(taskId, AgentActions.Rejected, 20, ct);
        return rejections
            .Where(r => !string.IsNullOrWhiteSpace(r.Details))
            .Select(r => r.Details!)
            .Reverse() // repository returns newest-first; present oldest-first
            .ToList();
    }

    // ── ARGUMENT RECORDS ───────────────────────────────────────────────────────────
    private sealed record ProgressArgs([property: JsonPropertyName("note")] string Note);
    private sealed record ReviewArgs([property: JsonPropertyName("summary")] string Summary);
}
