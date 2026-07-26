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

    public StubClaude(params MessageResponse[] responses) =>
        _responses = new Queue<MessageResponse>(responses);

    public bool IsConfigured => true;

    public Task<MessageResponse> SendAsync(MessageParameters parameters, CancellationToken ct = default) =>
        Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : EndTurn());

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

    private static MessageResponse EndTurn() => new()
    {
        StopReason = "end_turn",
        Content = new List<ContentBase> { new TextContent { Text = "Done." } }
    };
}
