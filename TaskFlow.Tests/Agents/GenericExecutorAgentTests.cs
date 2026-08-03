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

    private static IExecutorSwitch EnabledSwitch()
    {
        var sw = new Mock<IExecutorSwitch>();
        sw.SetupGet(s => s.IsEnabled).Returns(true);
        return sw.Object;
    }

    private static ISpendGuard PermissiveSpendGuard()
    {
        var guard = new Mock<ISpendGuard>();
        guard.Setup(g => g.CanRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return guard.Object;
    }

    // A real switch (enabled) so a test can assert the executor pauses itself.
    private static ExecutorSwitch RealEnabledSwitch() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Agents:ExecutorEnabled"] = "true" })
            .Build());

    // Builds the executor with enabled/permissive guards by default; a test overrides the one it exercises.
    private static GenericExecutorAgent CreateSut(
        IClaudeClient claude,
        ITaskRepository tasks,
        IAgentLogRepository logs,
        IAgentNotifier notifier,
        IExecutorSwitch? sw = null,
        ISpendGuard? guard = null) =>
        new(claude, tasks, sw ?? EnabledSwitch(), guard ?? PermissiveSpendGuard(),
            logs, notifier, Config(), NullLogger<GenericExecutorAgent>.Instance);

    private static async Task<TaskItem> SeedTodoTaskAsync(SqliteInMemoryContext db)
    {
        await db.Context.Tasks.ExecuteDeleteAsync();      // start from a known-empty board
        var task = new TaskItem { Title = "Do the thing", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        return task;
    }

    [Fact]
    public async Task Claims_a_todo_task_works_it_and_moves_it_to_Review()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedTodoTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatRecordsProgressThenRequestsReview(
            note: "Planned the work.", summary: "Completed the thing.");
        var notifier = new Mock<IAgentNotifier>();

        var sut = CreateSut(claude, tasks, logs, notifier.Object);
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Review);   // guardrail: the executor never reaches Done
        updated.ClaimedBy.Should().Be("GenericExecutor");

        var recent = await logs.GetRecentAsync("GenericExecutor", 10);
        recent.Should().Contain(l => l.Action == AgentActions.Claimed && l.TaskId == task.Id);
        recent.Should().Contain(l => l.Action == AgentActions.ProgressRecorded && l.TaskId == task.Id);
        recent.Should().Contain(l => l.Action == AgentActions.ReviewRequested && l.TaskId == task.Id);

        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.InProgress, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.Review, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Auto_finalizes_to_Review_when_Claude_never_requests_review()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedTodoTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReturnsText("I thought about it but did not request review.");
        var notifier = new Mock<IAgentNotifier>();

        var sut = CreateSut(claude, tasks, logs, notifier.Object);
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Review);   // never left orphaned InProgress

        var recent = await logs.GetRecentAsync("GenericExecutor", 10);
        recent.Should().Contain(l => l.Action == AgentActions.AutoFinalized && l.TaskId == task.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.ReviewRequested && l.TaskId == task.Id);

        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.InProgress, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.TaskMovedAsync(task.Id, WorkflowStatus.Review, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Pauses_itself_when_the_board_is_clear()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();      // no open work anywhere

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);
        var sw = RealEnabledSwitch();

        var sut = CreateSut(claude.Object, new TaskRepository(db.Context), new AgentLogRepository(db.Context),
            Mock.Of<IAgentNotifier>(), sw: sw);
        await sut.RunAsync(CancellationToken.None);

        sw.IsEnabled.Should().BeFalse();   // paused itself: nothing To Do / In Progress / Review
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stays_enabled_when_open_work_remains_but_nothing_to_claim()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        db.Context.Tasks.Add(new TaskItem { Title = "Awaiting review", Status = WorkflowStatus.Review, Kind = TaskKind.Generic });
        await db.Context.SaveChangesAsync();

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);
        var sw = RealEnabledSwitch();

        var sut = CreateSut(claude.Object, new TaskRepository(db.Context), new AgentLogRepository(db.Context),
            Mock.Of<IAgentNotifier>(), sw: sw);
        await sut.RunAsync(CancellationToken.None);

        sw.IsEnabled.Should().BeTrue();    // a Review task is still open, so it keeps running
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Skips_without_claiming_when_the_executor_is_paused()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedTodoTaskAsync(db);
        var tasks = new TaskRepository(db.Context);

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);

        var pausedSwitch = new Mock<IExecutorSwitch>();
        pausedSwitch.SetupGet(s => s.IsEnabled).Returns(false);

        var sut = CreateSut(claude.Object, tasks, new AgentLogRepository(db.Context), Mock.Of<IAgentNotifier>(),
            sw: pausedSwitch.Object);
        await sut.RunAsync(CancellationToken.None);

        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        db.Context.ChangeTracker.Clear();
        (await tasks.GetByIdAsync(task.Id))!.Status.Should().Be(WorkflowStatus.Todo);   // untouched
    }

    [Fact]
    public async Task Skips_without_claiming_when_over_the_daily_cap()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedTodoTaskAsync(db);
        var tasks = new TaskRepository(db.Context);

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);

        var overBudget = new Mock<ISpendGuard>();
        overBudget.Setup(g => g.CanRunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut(claude.Object, tasks, new AgentLogRepository(db.Context), Mock.Of<IAgentNotifier>(),
            guard: overBudget.Object);
        await sut.RunAsync(CancellationToken.None);

        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        db.Context.ChangeTracker.Clear();
        (await tasks.GetByIdAsync(task.Id))!.Status.Should().Be(WorkflowStatus.Todo);   // untouched
    }

    [Fact]
    public async Task Rolls_the_task_back_to_Todo_when_the_cycle_throws()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedTodoTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatThrows();

        var sut = CreateSut(claude, tasks, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);   // must not throw; the executor rolls back

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);   // returned to the pool
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync("GenericExecutor", 10);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == task.Id);
    }

    [Fact]
    public async Task Includes_prior_rejection_reasons_in_the_prompt_it_sends_to_Claude()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedTodoTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        db.Context.AgentLogs.Add(new AgentLog
        {
            AgentName = "Reviewer",
            Action = AgentActions.Rejected,
            TaskId = task.Id,
            Details = "The haiku must mention frost.",
            Success = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.Context.SaveChangesAsync();

        string? prompt = null;
        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);
        claude
            .Setup(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .Callback<MessageParameters, CancellationToken>((p, _) =>
                prompt ??= p.Messages[0].Content.OfType<TextContent>().FirstOrDefault()?.Text)
            .ReturnsAsync(new MessageResponse
            {
                StopReason = "end_turn",
                Content = new List<ContentBase> { new TextContent { Text = "ok" } }
            });

        var sut = CreateSut(claude.Object, tasks, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        prompt.Should().NotBeNull();
        prompt!.Should().Contain("The haiku must mention frost."); // the rejection is folded into the prompt
    }
}
