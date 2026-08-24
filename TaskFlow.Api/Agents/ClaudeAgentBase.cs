using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using System.Text.Json;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Security;
using TaskFlow.Api.Services;
using Tool = Anthropic.SDK.Common.Tool;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Base class for agents that reason with Claude using tool calling.
///
/// It owns the mechanics that every Claude agent shares — driving the
/// observe/reason/act conversation loop, recording actions, and broadcasting
/// lifecycle events — so that each concrete agent only has to supply its own
/// policy: which tools it exposes, what prompt it builds, and how it handles
/// each tool call.
///
/// Collaborators are injected as abstractions (<see cref="IClaudeClient"/>,
/// <see cref="IAgentLogRepository"/>, <see cref="IAgentNotifier"/>) rather than a
/// concrete <c>DbContext</c> or SDK client, so agents are unit-testable with a
/// stubbed Claude and in-memory repositories. This follows Single Responsibility
/// (the conversation plumbing lives in one place) and Dependency Inversion (the
/// base depends on interfaces, not implementations).
/// </summary>
public abstract class ClaudeAgentBase : ITaskFlowAgent
{
    /// <summary>Safety cap so a runaway tool loop cannot call the API unbounded.</summary>
    private const int MaxToolLoopIterations = 10;

    private readonly IAgentNotifier _notifier;

    /// <summary>Claude client used to drive the tool-use conversation.</summary>
    protected IClaudeClient Claude { get; }

    /// <summary>
    /// Agent-log data access. Also the persistence seam for per-task changes: it
    /// shares the request-scoped <c>DbContext</c> with the task/user repositories,
    /// so saving a log here flushes any pending entity edits made in the same cycle.
    /// </summary>
    protected IAgentLogRepository Logs { get; }

    /// <summary>Application configuration (intervals, thresholds, Anthropic settings).</summary>
    protected IConfiguration Config { get; }

    /// <summary>Logger bound to the concrete agent type.</summary>
    protected ILogger Logger { get; }

    protected ClaudeAgentBase(
        IClaudeClient claude,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger logger)
    {
        Claude = claude;
        Logs = logs;
        _notifier = notifier;
        Config = config;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract TimeSpan Interval { get; }

    /// <inheritdoc />
    public abstract Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Default: never completes early, so the loop just waits out the full Interval - identical to
    /// this member not existing at all. Overridden by GenericExecutorAgent, the one agent with a
    /// human on/off switch worth waking early for.
    /// </summary>
    public virtual Task WaitForWakeSignalAsync(CancellationToken cancellationToken) =>
        Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>
    /// Handles a single tool call Claude requested and returns the tool result
    /// to feed back into the conversation. Implemented by each concrete agent.
    /// </summary>
    protected delegate Task<ContentBase> ToolDispatcher(
        ToolUseContent toolUse,
        CancellationToken cancellationToken);

    /// <summary>True when Claude has an API key configured; lets a cycle skip quietly otherwise.</summary>
    protected bool ClaudeConfigured => Claude.IsConfigured;

    /// <summary>
    /// Runs the full tool-use conversation: send the prompt, let Claude call tools,
    /// execute each via <paramref name="dispatch"/>, feed results back, and repeat
    /// until Claude ends its turn or the iteration cap is hit. Model and token
    /// limit come from configuration (falling back to <see cref="AnthropicDefaults"/>),
    /// unless the caller supplies its own resolved <paramref name="maxTokensOverride"/>
    /// (e.g. TailoringAgentBase, whose agents need a higher ceiling than the shared default).
    /// </summary>
    /// <returns>The number of tool calls that completed successfully.</returns>
    protected async Task<int> RunToolConversationAsync(
        string prompt,
        IReadOnlyList<Tool> tools,
        ToolDispatcher dispatch,
        CancellationToken cancellationToken,
        int? maxTokensOverride = null)
    {
        var model = Config["Anthropic:Model"] ?? AnthropicDefaults.Model;
        var maxTokens = maxTokensOverride ?? Config.GetValue("Anthropic:MaxTokens", AnthropicDefaults.MaxTokens);

        var messages = new List<Message> { new(RoleType.User, prompt) };
        var successfulActions = 0;
        var iterations = 0;
        var continueLoop = true;

        while (continueLoop
               && iterations < MaxToolLoopIterations
               && !cancellationToken.IsCancellationRequested)
        {
            iterations++;

            var response = await Claude.SendAsync(
                new MessageParameters
                {
                    Model = model,
                    MaxTokens = maxTokens,
                    Tools = tools.ToList(),
                    Messages = messages
                },
                cancellationToken);

            // Preserve the structured content blocks so the tool_result blocks we
            // send next have a matching tool_use block to reference.
            messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            if (response.StopReason == "tool_use")
            {
                var toolResults = new List<ContentBase>();

                foreach (var toolUse in response.Content.OfType<ToolUseContent>())
                {
                    var result = await dispatch(toolUse, cancellationToken);
                    toolResults.Add(result);

                    if (WasSuccessful(result))
                        successfulActions++;
                }

                messages.Add(new Message { Role = RoleType.User, Content = toolResults });
            }
            else
            {
                // Claude did not call a tool this turn. Usually StopReason is "end_turn" (a normal
                // completion), but it can also be "max_tokens" if the response was cut off
                // mid-generation before it reached a tool call — exactly the live incident this
                // fix addresses (PR #56): a truncated response looks identical to a short, complete
                // one unless distinguished here. Logged as a warning, not the routine summary line,
                // so a recurrence (e.g. a resume that still exceeds the raised ceiling) is visible
                // in logs instead of indistinguishable from Claude just finishing early.
                continueLoop = false;

                var finalText = response.Content
                    .OfType<TextContent>()
                    .FirstOrDefault()?.Text;

                if (response.StopReason == "max_tokens")
                {
                    Logger.LogWarning(
                        "[{Agent}] Response was truncated at the token limit before calling a tool. " +
                        "Consider raising the configured max tokens for this agent. Partial text: {Text}",
                        Name, finalText);
                }
                else if (!string.IsNullOrWhiteSpace(finalText))
                {
                    Logger.LogInformation("[{Agent}] Claude summary: {Text}", Name, finalText);
                }
            }
        }

        if (continueLoop && iterations >= MaxToolLoopIterations)
        {
            Logger.LogWarning(
                "[{Agent}] Hit max tool-loop iterations ({Max}). Ending cycle early.",
                Name, MaxToolLoopIterations);
        }

        return successfulActions;
    }

    /// <summary>
    /// Convenience factory for a Claude tool: serializes the anonymous
    /// <paramref name="schema"/> object into the JSON schema string the SDK expects.
    /// </summary>
    protected static Tool DefineTool(string name, string description, object schema) =>
        new Function(name, description, JsonSerializer.Serialize(schema));

    /// <summary>Wraps a plain-text result in the <see cref="ToolResultContent"/> shape the SDK expects.</summary>
    protected static ToolResultContent ToolResult(ToolUseContent toolUse, string text) =>
        new()
        {
            ToolUseId = toolUse.Id,
            Content = new List<ContentBase> { new TextContent { Text = text } }
        };

    /// <summary>
    /// A tool call counts as a real action only if it did not report an error (unknown tool, not
    /// found, invalid argument, exception, etc.). Every error string this codebase actually
    /// produces is a short, code-generated sentence; content that echoes back arbitrary text (e.g.
    /// TailoringAgentBase's read_base_context tool, which returns the user's own resume) is always
    /// wrapped first via <see cref="PromptSafety.WrapUntrusted"/>. A fixed-length scan window
    /// cannot safely rule out a false positive there — the user's own content could start with a
    /// trigger phrase immediately after the wrapper's tag (Epic 3 Pre-Merge Code Review, finding
    /// 2.1; a first attempt using a 256-character window still missed exactly this case, caught by
    /// PR #50's Copilot review) — so wrapped content is recognized by its fixed framing prefix and
    /// exempted from the heuristic entirely, rather than scanning a bounded prefix of it.
    /// `internal` (not `protected`) purely so TaskFlow.Tests can exercise this heuristic directly.
    /// </summary>
    internal static bool WasSuccessful(ContentBase result)
    {
        var text = (result as ToolResultContent)?.Content?
            .OfType<TextContent>()
            .FirstOrDefault()?.Text ?? string.Empty;

        if (text.StartsWith(PromptSafety.FramingPrefix, StringComparison.Ordinal))
            return true;

        return !text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("not found", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists a per-task action log and broadcasts it to connected dashboards.
    /// Saving here also flushes any task/user entity edits made earlier in the same
    /// cycle, since all repositories share one <c>DbContext</c>.
    /// </summary>
    /// <param name="ownerId">The owning user to scope the broadcast to, or null for the shared
    /// generic board - pass the acted-on task's <see cref="Models.TaskItem.OwnerId"/>.</param>
    protected async Task RecordActionAsync(AgentLog log, int? ownerId, CancellationToken cancellationToken)
    {
        await Logs.AddAsync(log, cancellationToken);
        await Logs.SaveChangesAsync(cancellationToken);
        await _notifier.AgentActionAsync(log, ownerId, cancellationToken);
    }

    /// <summary>
    /// Persists a cycle-summary log without broadcasting a per-action event
    /// (the cycle start/complete events cover the dashboard's cycle status).
    /// </summary>
    protected async Task RecordCycleSummaryAsync(AgentLog log, CancellationToken cancellationToken)
    {
        await Logs.AddAsync(log, cancellationToken);
        await Logs.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Broadcasts that this agent has started a cycle.</summary>
    protected Task NotifyCycleStartedAsync(CancellationToken cancellationToken) =>
        _notifier.AgentCycleAsync(Name, AgentPhases.Started, cancellationToken);

    /// <summary>Broadcasts that this agent has completed a cycle.</summary>
    protected Task NotifyCycleCompletedAsync(CancellationToken cancellationToken) =>
        _notifier.AgentCycleAsync(Name, AgentPhases.Completed, cancellationToken);

    /// <summary>Broadcasts that a task moved to a new status, so boards update that one card live.</summary>
    /// <param name="ownerId">See <see cref="RecordActionAsync"/>.</param>
    protected Task NotifyTaskMovedAsync(int taskId, WorkflowStatus status, int? ownerId, CancellationToken cancellationToken) =>
        _notifier.TaskMovedAsync(taskId, status, ownerId, cancellationToken);
}
