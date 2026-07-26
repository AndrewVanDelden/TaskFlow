using Anthropic.SDK.Messaging;
using System.Text.Json;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using Tool = Anthropic.SDK.Common.Tool;
using Function = Anthropic.SDK.Common.Function;

namespace TaskFlow.Api.Agents;

/// <summary>
/// Base for agents that reason with Claude via tool calling. Owns the observe/reason/act
/// loop, action recording, and lifecycle broadcasts. Concrete agents supply their tools,
/// prompt, and per-tool handlers. Every dependency is a seam, so agents are unit-testable.
/// </summary>
public abstract class ClaudeAgentBase : ITaskFlowAgent
{
    private const int MaxToolLoopIterations = 10;

    private readonly IClaudeClient _claude;
    private readonly IAgentNotifier _notifier;

    protected IAgentLogRepository Logs { get; }
    protected IConfiguration Config { get; }
    protected ILogger Logger { get; }

    protected ClaudeAgentBase(
        IClaudeClient claude,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IConfiguration config,
        ILogger logger)
    {
        _claude = claude;
        Logs = logs;
        _notifier = notifier;
        Config = config;
        Logger = logger;
    }

    public abstract string Name { get; }
    public abstract TimeSpan Interval { get; }
    public abstract Task RunAsync(CancellationToken cancellationToken);

    /// <summary>True when Claude has an API key; agents skip their cycle when false.</summary>
    protected bool ClaudeConfigured => _claude.IsConfigured;

    protected delegate Task<ContentBase> ToolDispatcher(ToolUseContent toolUse, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the full tool-use conversation: send the prompt, let Claude call tools, execute
    /// each via <paramref name="dispatch"/>, feed results back, repeat until Claude ends its
    /// turn or the iteration cap is hit. Returns the count of successful tool calls.
    /// </summary>
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

        while (continueLoop && iterations < MaxToolLoopIterations && !cancellationToken.IsCancellationRequested)
        {
            iterations++;

            var response = await _claude.SendAsync(new MessageParameters
            {
                Model = model,
                MaxTokens = maxTokens,
                Tools = tools.ToList(),
                Messages = messages
            }, cancellationToken);

            messages.Add(new Message { Role = RoleType.Assistant, Content = response.Content });

            if (response.StopReason == "tool_use")
            {
                var toolResults = new List<ContentBase>();
                foreach (var toolUse in response.Content.OfType<ToolUseContent>())
                {
                    var result = await dispatch(toolUse, cancellationToken);
                    toolResults.Add(result);
                    if (WasSuccessful(result)) successfulActions++;
                }
                messages.Add(new Message { Role = RoleType.User, Content = toolResults });
            }
            else
            {
                continueLoop = false;
                var finalText = response.Content.OfType<TextContent>().FirstOrDefault()?.Text;
                if (!string.IsNullOrWhiteSpace(finalText))
                    Logger.LogInformation("[{Agent}] Claude summary: {Text}", Name, finalText);
            }
        }

        if (continueLoop && iterations >= MaxToolLoopIterations)
            Logger.LogWarning("[{Agent}] Hit max tool-loop iterations ({Max}). Ending cycle early.", Name, MaxToolLoopIterations);

        return successfulActions;
    }

    protected static Tool DefineTool(string name, string description, object schema) =>
        new Function(name, description, JsonSerializer.Serialize(schema));

    protected static ToolResultContent ToolResult(ToolUseContent toolUse, string text) =>
        new() { ToolUseId = toolUse.Id, Content = new List<ContentBase> { new TextContent { Text = text } } };

    protected static bool WasSuccessful(ContentBase result)
    {
        var text = (result as ToolResultContent)?.Content?.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
        return !text.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("not found", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists an action log and broadcasts it. TaskRepository and AgentLogRepository share
    /// the same scoped DbContext, so this save also persists any task change made in the same
    /// handler.
    /// </summary>
    protected async Task RecordActionAsync(AgentLog log, CancellationToken cancellationToken)
    {
        await Logs.AddAsync(log, cancellationToken);
        await Logs.SaveChangesAsync(cancellationToken);
        await _notifier.AgentActionAsync(log, cancellationToken);
    }

    protected Task NotifyCycleStartedAsync(CancellationToken ct) => _notifier.AgentCycleAsync(Name, AgentPhases.Started, ct);
    protected Task NotifyCycleCompletedAsync(CancellationToken ct) => _notifier.AgentCycleAsync(Name, AgentPhases.Completed, ct);
}
