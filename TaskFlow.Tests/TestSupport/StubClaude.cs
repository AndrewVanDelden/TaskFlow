using System.Text.Json;
using Anthropic.SDK.Messaging;
using TaskFlow.Api.Services;

namespace TaskFlow.Tests.TestSupport;

/// <summary>
/// Hand-written IClaudeClient that replays canned responses in order — no network, no tokens.
///
/// NOTE: constructing MessageResponse / ToolUseContent depends on the Anthropic.SDK types.
/// If this does not compile, adjust the property initializers to match the installed SDK
/// version (this is test scaffolding — a wrong shape only fails the test, never ships).
/// </summary>
public sealed class StubClaude : IClaudeClient
{
    private readonly Queue<MessageResponse> _responses;
    private readonly bool _throws;

    public StubClaude(params MessageResponse[] responses)
    {
        _responses = new Queue<MessageResponse>(responses);
        _throws = false;
    }

    private StubClaude(bool throws)
    {
        _responses = new Queue<MessageResponse>();
        _throws = throws;
    }

    public bool IsConfigured => true;

    /// <summary>The most recent request passed to <see cref="SendAsync"/>, for tests that need to
    /// assert on exactly what was sent (e.g. that untrusted content was wrapped before it left).</summary>
    public MessageParameters? LastRequest { get; private set; }

    public Task<MessageResponse> SendAsync(MessageParameters parameters, CancellationToken ct = default)
    {
        LastRequest = parameters;
        if (_throws)
            throw new InvalidOperationException("Claude call failed (test).");
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : EndTurn());
    }

    /// <summary>A client whose every call throws, to exercise the executor's rollback path.</summary>
    public static StubClaude ThatThrows() => new(throws: true);

    /// <summary>Scripts one escalate_task tool call, then an end_turn.</summary>
    public static StubClaude ThatEscalates(int taskId, string reason) => new(
        new MessageResponse
        {
            StopReason = "tool_use",
            Content = new List<ContentBase>
            {
                new ToolUseContent
                {
                    Id = "tool_1",
                    Name = "escalate_task",
                    Input = JsonSerializer.SerializeToNode(new { task_id = taskId, reason })!
                }
            }
        },
        EndTurn());

    /// <summary>Scripts one reassign_task tool call (omit newUserId to unassign), then an end_turn.</summary>
    public static StubClaude ThatReassigns(int taskId, int? newUserId, string reason) => new(
        ToolUse("tool_1", "reassign_task", new { task_id = taskId, new_user_id = newUserId, reason }),
        EndTurn());

    /// <summary>Scripts one flag_for_review tool call, then an end_turn.</summary>
    public static StubClaude ThatFlags(int taskId, string concern) => new(
        ToolUse("tool_1", "flag_for_review", new { task_id = taskId, concern }),
        EndTurn());

    /// <summary>Scripts a call to a tool name the agent does not recognize, then an end_turn -
    /// exercises the dispatcher's "unknown tool" fallback branch.</summary>
    public static StubClaude ThatCallsAnUnknownTool() => new(
        ToolUse("tool_1", "not_a_real_tool", new { }),
        EndTurn());

    /// <summary>
    /// Scripts a call to <paramref name="toolName"/> whose arguments are not a JSON object (a bare
    /// string), so deserializing them into the tool's argument record throws - exercising a tool
    /// dispatcher's catch-and-report-as-a-tool-error branch, rather than the agent crashing.
    /// </summary>
    public static StubClaude ThatCallsToolWithUndeserializableArgs(string toolName) => new(
        new MessageResponse
        {
            StopReason = "tool_use",
            Content = new List<ContentBase>
            {
                new ToolUseContent
                {
                    Id = "tool_1",
                    Name = toolName,
                    Input = JsonSerializer.SerializeToNode("not-an-object")!
                }
            }
        },
        EndTurn());

    /// <summary>Scripts one update_task_priority tool call, then an end_turn.</summary>
    public static StubClaude ThatUpdatesPriority(int taskId, string priority, string reasoning) => new(
        new MessageResponse
        {
            StopReason = "tool_use",
            Content = new List<ContentBase>
            {
                new ToolUseContent
                {
                    Id = "tool_1",
                    Name = "update_task_priority",
                    Input = JsonSerializer.SerializeToNode(new { task_id = taskId, priority, reasoning })!
                }
            }
        },
        EndTurn());

    /// <summary>Scripts a single plain-text reply (used by the ingestion parser).</summary>
    public static StubClaude ThatReturnsText(string text) => new(
        new MessageResponse
        {
            StopReason = "end_turn",
            Content = new List<ContentBase> { new TextContent { Text = text } }
        });

    /// <summary>Scripts a record_progress call, then a request_review call, then an end_turn.</summary>
    public static StubClaude ThatRecordsProgressThenRequestsReview(string note, string summary) => new(
        ToolUse("tool_1", "record_progress", new { note }),
        ToolUse("tool_2", "request_review", new { summary }),
        EndTurn());

    /// <summary>
    /// Scripts a read_base_context call, then a save call for <paramref name="saveToolName"/> with
    /// <paramref name="content"/>, then an end_turn. Shared by the Sprint 3R tailoring agents
    /// (ResumeTailoringAgent, CoverLetterAgent), which differ only in their save tool's name.
    /// </summary>
    public static StubClaude ThatReadsContextThenSaves(string saveToolName, string content) => new(
        ToolUse("tool_1", "read_base_context", new { }),
        ToolUse("tool_2", saveToolName, new { content }),
        EndTurn());

    /// <summary>
    /// Scripts a single save call for <paramref name="saveToolName"/> with <paramref name="content"/>
    /// (skipping read_base_context), then an end_turn. Used to script an over-length save attempt
    /// that gets rejected by ToolOutputValidator before the cycle ends.
    /// </summary>
    public static StubClaude ThatSavesOnly(string saveToolName, string content) => new(
        ToolUse("tool_1", saveToolName, new { content }),
        EndTurn());

    private static MessageResponse ToolUse(string id, string name, object args) => new()
    {
        StopReason = "tool_use",
        Content = new List<ContentBase>
        {
            new ToolUseContent { Id = id, Name = name, Input = JsonSerializer.SerializeToNode(args)! }
        }
    };

    private static MessageResponse EndTurn() => new()
    {
        StopReason = "end_turn",
        Content = new List<ContentBase> { new TextContent { Text = "Done." } }
    };
}
