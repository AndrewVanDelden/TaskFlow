using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Anthropic.SDK.Messaging;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Agents;

public class GenericExecutorAgentTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test"
        }).Build();

    [Fact]
    public async Task Claims_a_todo_task_works_it_and_moves_it_to_Review()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();      // start from a known-empty board
        var task = new TaskItem { Title = "Do the thing", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatRecordsProgressThenRequestsReview(
            note: "Planned the work.", summary: "Completed the thing.");

        // Constructor order: claude, tasks, logs, notifier, config, logger.
        var notifier = new Mock<IAgentNotifier>();
        var sut = new GenericExecutorAgent(
            claude, tasks, logs, notifier.Object, Config(), NullLogger<GenericExecutorAgent>.Instance);

        await sut.RunAsync(CancellationToken.None);

        // ExecuteUpdate bypasses the tracker; clear it so the read reflects true DB state.
        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Review);
        updated.ClaimedBy.Should().Be("GenericExecutor");

        var recent = await logs.GetRecentAsync("GenericExecutor", 10);
        recent.Should().Contain(l => l.Action == AgentActions.Claimed && l.TaskId == task.Id);
        recent.Should().Contain(l => l.Action == AgentActions.ProgressRecorded && l.TaskId == task.Id);
        recent.Should().Contain(l => l.Action == AgentActions.ReviewRequested && l.TaskId == task.Id);

        // T5.1: the board is told about each transition, live.
        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.InProgress, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.Review, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Auto_finalizes_to_Review_when_Claude_never_requests_review()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem { Title = "Do the thing", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        // Claude ends its turn with plain text and never calls request_review.
        var claude = StubClaude.ThatReturnsText("I thought about it but did not request review.");

        var notifier = new Mock<IAgentNotifier>();
        var sut = new GenericExecutorAgent(
            claude, tasks, logs, notifier.Object, Config(), NullLogger<GenericExecutorAgent>.Instance);

        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Review);   // never left orphaned InProgress

        var recent = await logs.GetRecentAsync("GenericExecutor", 10);
        recent.Should().Contain(l => l.Action == AgentActions.AutoFinalized && l.TaskId == task.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.ReviewRequested && l.TaskId == task.Id);

        // T5.1: claim and the auto-finalize both broadcast their transition.
        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.InProgress, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.Review, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task No_todo_task_skips_without_calling_Claude()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();      // no Todo tasks at all

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);

        var sut = new GenericExecutorAgent(
            claude.Object,
            new TaskRepository(db.Context),
            new AgentLogRepository(db.Context),
            Mock.Of<IAgentNotifier>(),
            Config(),
            NullLogger<GenericExecutorAgent>.Instance);

        await sut.RunAsync(CancellationToken.None);

        // Nothing to claim → the agent returns before ever calling Claude.
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
