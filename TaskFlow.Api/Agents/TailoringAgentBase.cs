using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Security;
using TaskFlow.Api.Services;
using Tool = Anthropic.SDK.Common.Tool;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Shared base for the Epic 3 "generate application material" agents
/// (<see cref="ResumeTailoringAgent"/>, <see cref="CoverLetterAgent"/>). They are structurally
/// near-identical: claim a task by <see cref="Kind"/>, fetch the base resume via a tool, produce
/// one markdown artifact, save it and move the task to Review, then attempt the atomic join that
/// promotes the parent <see cref="JobApplication"/> to <see cref="ApplicationState.ReviewReady"/>
/// once both siblings are done.
///
/// This base owns the <see cref="PromptSafety.WrapUntrusted"/> call itself — both for the job
/// posting text and the base resume — so a concrete subclass cannot structurally forget to wrap
/// untrusted content. A subclass supplies only which <see cref="TaskKind"/> it claims, its save
/// tool's name/description, and its own instructional framing text (see <see cref="BuildInstructions"/>).
/// </summary>
public abstract class TailoringAgentBase : ClaudeAgentBase
{
    private const string ReadBaseContextTool = "read_base_context";

    private readonly ITaskRepository _tasks;
    private readonly IResumeContextRepository _resumeContexts;
    private readonly IJobApplicationRepository _jobApplications;

    protected TailoringAgentBase(
        IClaudeClient claude,
        ITaskRepository tasks,
        IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger logger)
        : base(claude, logs, notifier, config, logger)
    {
        _tasks = tasks;
        _resumeContexts = resumeContexts;
        _jobApplications = jobApplications;
    }

    /// <summary>Which task kind this agent claims (e.g. ResumeTailoring, CoverLetterTailoring).</summary>
    protected abstract TaskKind Kind { get; }

    /// <summary>Name of this agent's save tool (e.g. "save_tailored_resume").</summary>
    protected abstract string SaveToolName { get; }

    /// <summary>Description Claude sees for the save tool.</summary>
    protected abstract string SaveToolDescription { get; }

    /// <summary>
    /// Appends this agent's specific instructions to the shared framing. Do not include the
    /// job posting or base resume text here — the base class already handles both.
    /// </summary>
    protected abstract string BuildInstructions();

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!ClaudeConfigured)
        {
            Logger.LogWarning("[{Agent}] Anthropic API key not configured. Skipping cycle.", Name);
            return;
        }

        var task = await _tasks.TryClaimNextAsync(Kind, Name, cancellationToken);
        if (task is null)
        {
            Logger.LogInformation("[{Agent}] No Todo task of kind {Kind} to claim.", Name, Kind);
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
                Details = $"Claimed '{task.Title}' for tailoring.",
                Success = true,
                CreatedAt = DateTime.UtcNow
            }, task.OwnerId, cancellationToken);

            await NotifyTaskMovedAsync(task.Id, WorkflowStatus.InProgress, task.OwnerId, cancellationToken);

            // task.ApplicationId is nullable in the type system, but assembly always sets it for
            // these kinds. If it's null, that's a data-integrity problem, not a normal "nothing to
            // do" case — roll back rather than throw past this point.
            if (task.ApplicationId is null)
            {
                Logger.LogError(
                    "[{Agent}] Task {Id} has no ApplicationId; cannot resolve its base resume.", Name, task.Id);
                await RollBackAsync(task, "Task has no ApplicationId (data integrity).");
                return;
            }

            var application = await _jobApplications.GetByIdAsync(task.ApplicationId.Value, cancellationToken);
            if (application is null)
            {
                Logger.LogError(
                    "[{Agent}] JobApplication {AppId} for Task {Id} was not found.",
                    Name, task.ApplicationId, task.Id);
                await RollBackAsync(task, $"JobApplication {task.ApplicationId} not found.");
                return;
            }

            // Resolved before any Claude call: an unusable base resume (e.g. deleted after
            // assembly, racing agent pickup) must fail before spending an API call on it.
            var resumeContext = await _resumeContexts.GetForOwnerAsync(
                application.IngestionSessionId, application.OwnerId, cancellationToken);
            if (resumeContext is null)
            {
                Logger.LogError(
                    "[{Agent}] No ResumeContext for session '{Session}' owner {Owner} (Task {Id}).",
                    Name, application.IngestionSessionId, application.OwnerId, task.Id);
                await RollBackAsync(task, "No base resume (ResumeContext) available for this application.");
                return;
            }

            var maxTokens = Config.GetValue("Anthropic:TailoringMaxTokens", AnthropicDefaults.TailoringMaxTokens);
            var actions = await RunToolConversationAsync(
                prompt: BuildPrompt(task, application),
                tools: BuildTools(),
                dispatch: (toolUse, ct) => ExecuteToolAsync(task, application, resumeContext, toolUse, ct),
                cancellationToken,
                maxTokensOverride: maxTokens);

            Logger.LogInformation(
                "[{Agent}] Cycle complete for Task {Id}. {Count} tool action(s).", Name, task.Id, actions);

            // A cancelled cycle is abnormal termination: roll back rather than finalize a half-done
            // task, mirroring GenericExecutorAgent's cancellation handling.
            if (cancellationToken.IsCancellationRequested)
            {
                await RollBackAsync(task, "cycle cancelled");
                return;
            }

            // Terminal-state handling, deliberately the INVERSE of GenericExecutorAgent's
            // auto-finalize-to-Review: if Claude ended without ever successfully calling the save
            // tool, the task is still InProgress. There is nothing to review without saved content,
            // so roll back to Todo rather than forcing an empty card into Review. RollBackAsync's
            // guarded ReleaseClaimAsync (WHERE Status == InProgress) IS the check here — atomic, not
            // a separate tracked read-then-branch — so it is a harmless no-op if the save tool
            // already moved the task on to Review.
            if (await RollBackAsync(task, "Cycle ended without saving tailored content."))
                return;

            await NotifyCycleCompletedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Agent}] Cycle failed for Task {Id}; rolling back to Todo.", Name, task.Id);
            await RollBackAsync(task, ex.Message);
        }
    }

    // ── ROLLBACK ───────────────────────────────────────────────────────────────────
    // Returns a claimed task to the pool. The guarded ReleaseClaimAsync (WHERE Status ==
    // InProgress) is what makes this both atomic and safe to call unconditionally: it no-ops
    // (returns false) if the task already moved on (e.g. a save already reached Review). Uses
    // CancellationToken.None so the rollback completes even when the cycle itself was cancelled.
    // Returns true if a rollback actually happened, so callers can branch on it (see the
    // terminal-state check in RunAsync, which relies on this instead of a separate tracked read).
    private async Task<bool> RollBackAsync(TaskItem task, string reason)
    {
        var released = await _tasks.ReleaseClaimAsync(task.Id, CancellationToken.None);
        if (!released)
            return false;

        // The claim release above is already atomic and committed — the task is genuinely back in
        // Todo regardless of what happens next. The log write and notify are best-effort audit
        // trail, not part of that guarantee, so a failure here must not throw past this point and
        // turn an already-successful recovery into an unhandled exception (PR #43 review, round 3:
        // Copilot's automated review).
        try
        {
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
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Agent}] Task {Id} was released but recording the rollback failed.", Name, task.Id);
        }

        return true;
    }

    // ── PROMPT ─────────────────────────────────────────────────────────────────────
    // The job-posting text is small and already on the claimed task, so it goes directly into the
    // initial prompt (wrapped) — no tool round-trip needed for it. The base resume, by contrast, is
    // fetched lazily via read_base_context so a cycle that never needs it never fetches it.
    private string BuildPrompt(TaskItem task, JobApplication application)
    {
        var wrappedJobPosting = PromptSafety.WrapUntrusted(FormatJobPosting(task, application), "job_posting");

        return
            "You are working one task in a job-application pipeline. Below is the job posting you " +
            "are tailoring output for. First call read_base_context to fetch the candidate's base " +
            "resume, then produce your output and save it using the save tool described below. " +
            "Do not describe your plan in a text response - go directly from reading the base " +
            "resume to calling the save tool with your final output. Do not end your turn until " +
            "you have called the save tool.\n\n" +
            wrappedJobPosting + "\n\n" +
            BuildInstructions();
    }

    // PR #55 review (finding 1): Company lives on JobApplication, not TaskItem.SourceSection - the
    // real job-posting parsers (ClaudeJobPostingParser, JobPostingParser) always leave Section
    // empty now, so reading task.SourceSection here silently dropped the company from every
    // tailoring prompt. Read it from the already-loaded application instead.
    private static string FormatJobPosting(TaskItem task, JobApplication application)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Title: {task.Title}");
        if (!string.IsNullOrWhiteSpace(application.Company))
            sb.AppendLine($"Company: {application.Company}");
        if (!string.IsNullOrWhiteSpace(task.Description))
            sb.AppendLine($"Description: {task.Description}");
        return sb.ToString();
    }

    // ── TOOL DEFINITIONS ───────────────────────────────────────────────────────────
    private List<Tool> BuildTools() =>
    [
        DefineTool(
            ReadBaseContextTool,
            "Fetch the candidate's base resume. Call this first.",
            new
            {
                type = "object",
                properties = new Dictionary<string, object>(),
                required = Array.Empty<string>()
            }),

        DefineTool(
            SaveToolName,
            SaveToolDescription,
            new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["content"] = new { type = "string", description = "The final generated content to save, as markdown." }
                },
                required = new[] { "content" }
            })
    ];

    // ── TOOL DISPATCH ──────────────────────────────────────────────────────────────
    private async Task<ContentBase> ExecuteToolAsync(
        TaskItem task,
        JobApplication application,
        ResumeContext resumeContext,
        ToolUseContent toolUse,
        CancellationToken cancellationToken)
    {
        try
        {
            if (toolUse.Name == ReadBaseContextTool)
                return ReadBaseContext(resumeContext, toolUse);

            if (toolUse.Name == SaveToolName)
                return await SaveAsync(task, application, toolUse, cancellationToken);

            return ToolResult(toolUse, $"Error: unknown tool {toolUse.Name}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Agent}] Tool execution failed for {Tool}", Name, toolUse.Name);
            return ToolResult(toolUse, $"Error: {ex.Message}");
        }
    }

    // ── READ BASE CONTEXT ──────────────────────────────────────────────────────────
    private static ContentBase ReadBaseContext(ResumeContext resumeContext, ToolUseContent toolUse)
    {
        var wrapped = PromptSafety.WrapUntrusted(resumeContext.Content, "base_resume");
        return ToolResult(toolUse, wrapped);
    }

    // ── SAVE ───────────────────────────────────────────────────────────────────────
    private async Task<ContentBase> SaveAsync(
        TaskItem task, JobApplication application, ToolUseContent toolUse, CancellationToken cancellationToken)
    {
        var args = toolUse.Input.Deserialize<SaveArgs>()
            ?? throw new InvalidOperationException($"Failed to deserialize {SaveToolName} arguments.");

        var validated = ToolOutputValidator.Validate(args.Content, TaskItem.TailoredContentMaxLength);
        if (!validated.IsSuccess)
        {
            // Let Claude see the error and, within RunToolConversationAsync's iteration cap,
            // potentially retry with corrected output. Do not save or transition status.
            return ToolResult(toolUse, $"Error: {validated.Error}");
        }

        var moved = await _tasks.SaveTailoredContentAndMarkForReviewAsync(task.Id, validated.Value!, cancellationToken);
        if (!moved)
        {
            // Genuine race/anomaly (task was no longer InProgress) — not expected in normal flow,
            // but must not crash or attempt the join promotion.
            Logger.LogWarning(
                "[{Agent}] Task {Id} was not InProgress when {Tool} was called; nothing saved.",
                Name, task.Id, SaveToolName);
            return ToolResult(toolUse, $"Error: Task {task.Id} was not InProgress; nothing to save.");
        }

        // The atomic save above already committed — the task is genuinely saved and in Review
        // regardless of what happens below. Everything from here on (the save's own audit log, the
        // notify, the join attempt, and the join's own audit log) is best-effort follow-up, not
        // part of that guarantee: each step gets its own try/catch so a failure in one does not
        // block the next, and none of them can turn an already-successful save into a misreported
        // tool error (PR #43 review, round 4: Copilot's automated review found this same pattern
        // one spot after where round 3's RollBackAsync fix landed). If the join attempt itself is
        // what fails, JobApplicationPromotionReconcilerService is the backstop that retries it.
        try
        {
            await RecordActionAsync(new AgentLog
            {
                AgentName = Name,
                Action = AgentActions.TailoredContentSaved,
                TaskId = task.Id,
                Details = "Tailored content saved; moved to Review.",
                Success = true,
                CreatedAt = DateTime.UtcNow
            }, task.OwnerId, cancellationToken);

            await NotifyTaskMovedAsync(task.Id, WorkflowStatus.Review, task.OwnerId, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Agent}] Task {Id} was saved, but recording it failed.", Name, task.Id);
        }

        bool promoted;
        try
        {
            // Atomic join attempt. False is the normal case (sibling not done yet) — not an error, no log.
            promoted = await _jobApplications.TryPromoteToReviewReadyAsync(application.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[{Agent}] Join attempt failed for JobApplication {AppId}; the reconciliation sweep will retry.",
                Name, application.Id);
            promoted = false;
        }

        if (promoted)
        {
            try
            {
                await RecordActionAsync(new AgentLog
                {
                    AgentName = Name,
                    Action = AgentActions.ApplicationReviewReady,
                    TaskId = task.Id,
                    Details = $"JobApplication {application.Id} promoted to ReviewReady.",
                    Success = true,
                    CreatedAt = DateTime.UtcNow
                }, task.OwnerId, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "[{Agent}] JobApplication {AppId} was promoted, but recording it failed.", Name, application.Id);
            }
        }

        return ToolResult(toolUse, $"Saved and moved Task {task.Id} to Review.");
    }

    // ── ARGUMENT RECORD ────────────────────────────────────────────────────────────
    private sealed record SaveArgs([property: JsonPropertyName("content")] string? Content);
}
