using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using System.Text.Json;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
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

    /// <summary>
    /// How much of a tool result's text <see cref="WasSuccessful"/> scans for an error phrase.
    /// Every error string this codebase actually produces is a short, code-generated sentence at
    /// the very start of the result; content that echoes back arbitrary text (e.g.
    /// TailoringAgentBase's read_base_context tool, which returns the user's own resume) is always
    /// wrapped first in PromptSafety.WrapUntrusted's framing sentence + tag, comfortably longer than
    /// this window - so ordinary prose containing a phrase like "does not exist" deep inside a large
    /// result is never mistaken for a real error (Epic 3 Pre-Merge Code Review, finding 2.1).
    /// </summary>
    private const int ErrorHeuristicScanWindow = 256;

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
    /// limit come from configuration (falling back to <see cref="AnthropicDefaults"/>).
    /// </summary>
    /// <returns>The number of tool calls that completed successfully.</returns>
    protected async Task<int> RunToolConversationAsync(
        string prompt,
        IReadOnlyList<Tool> tools,
        ToolDispatcher dispatch,
        CancellationToken cancellationToken)
    {
        var model = Config["Anthropic:Model"] ?? AnthropicDefaults.Model;
        var maxTokens = Config.GetValue("Anthropic:MaxTokens", AnthropicDefaults.MaxTokens);

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
                // StopReason is "end_turn" — Claude is finished.
                continueLoop = false;

                var finalText = response.Content
                    .OfType<TextContent>()
                    .FirstOrDefault()?.Text;

                if (!string.IsNullOrWhiteSpace(finalText))
                    Logger.LogInformation("[{Agent}] Claude summary: {Text}", Name, finalText);
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
    /// found, invalid argument, exception, etc.). Only scans the first
    /// <see cref="ErrorHeuristicScanWindow"/> characters — see that constant's doc comment for why.
    /// `internal` (not `protected`) purely so TaskFlow.Tests can exercise this heuristic directly.
    /// </summary>
    internal static bool WasSuccessful(ContentBase result)
    {
        var text = (result as ToolResultContent)?.Content?
            .OfType<TextContent>()
            .FirstOrDefault()?.Text ?? string.Empty;

        var window = text.Length <= ErrorHeuristicScanWindow ? text : text[..ErrorHeuristicScanWindow];

        return !window.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            && !window.Contains("not found", StringComparison.OrdinalIgnoreCase)
            && !window.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
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
