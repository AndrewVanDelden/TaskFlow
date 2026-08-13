using FluentAssertions;
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

public class StaleTaskAgentTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test",
            ["Agents:StaleTaskThresholdHours"] = "48"
        }).Build();

    // Constructor order: claude, tasks, users, logs, notifier, config, logger.
    private static StaleTaskAgent CreateSut(
        IClaudeClient claude, ITaskRepository tasks, IUserRepository users, IAgentLogRepository logs) =>
        new(claude, tasks, users, logs, Mock.Of<IAgentNotifier>(), Config(), NullLogger<StaleTaskAgent>.Instance);

    private static async Task<TaskItem> SeedStaleTaskAsync(SqliteInMemoryContext db, int? assignedToId = null)
    {
        var task = new TaskItem
        {
            Title = "Old task",
            Status = WorkflowStatus.Todo,
            Priority = TaskPriority.Medium,
            UpdatedAt = DateTime.UtcNow.AddDays(-10),
            AssignedToId = assignedToId
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        return task;
    }

    [Fact]
    public async Task Escalate_tool_call_sets_priority_High_and_logs_it()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedStaleTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatEscalates(task.Id, reason: "overdue 10 days");

        var sut = CreateSut(claude, tasks, users, logs);
        await sut.RunAsync(CancellationToken.None);

        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Priority.Should().Be(TaskPriority.High);

        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().Contain(l => l.Action == AgentActions.Escalated && l.TaskId == task.Id);
    }

    // Epic 3 Pre-Merge Code Review, finding 6.2: EscalateAsync's task-not-found branch was untested.
    [Fact]
    public async Task Escalate_tool_call_reports_not_found_and_logs_nothing_when_the_task_no_longer_exists()
    {
        using var db = new SqliteInMemoryContext();
        var staleAnchor = await SeedStaleTaskAsync(db); // keeps the cycle from short-circuiting on "no stale tasks"
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        const int missingTaskId = 999;
        var claude = StubClaude.ThatEscalates(missingTaskId, reason: "does not matter");

        var sut = CreateSut(claude, tasks, users, logs);
        var act = async () => await sut.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l => l.Action == AgentActions.Escalated);
        // The seeded anchor task itself must be untouched by the missing-id call.
        var anchor = await tasks.GetByIdAsync(staleAnchor.Id);
        anchor!.Priority.Should().Be(TaskPriority.Medium);
    }

    // Epic 3 Pre-Merge Code Review, finding 6.2: ReassignAsync was entirely untested.
    [Fact]
    public async Task Reassign_tool_call_moves_ownership_to_the_new_user_and_logs_it()
    {
        using var db = new SqliteInMemoryContext();
        // Ids 1 and 2 are already taken by AppDbContext's seed data (EnsureCreated applies it).
        db.Context.Users.Add(new User { Id = 101, Name = "Alice", Email = "alice@example.com", PasswordHash = "x" });
        db.Context.Users.Add(new User { Id = 102, Name = "Bob", Email = "bob@example.com", PasswordHash = "x" });
        await db.Context.SaveChangesAsync();
        var task = await SeedStaleTaskAsync(db, assignedToId: 101);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReassigns(task.Id, newUserId: 102, reason: "Alice is overloaded");

        var sut = CreateSut(claude, tasks, users, logs);
        await sut.RunAsync(CancellationToken.None);

        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.AssignedToId.Should().Be(102);

        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().Contain(l => l.Action == AgentActions.Reassigned && l.TaskId == task.Id);
    }

    [Fact]
    public async Task Reassign_tool_call_unassigns_the_task_when_no_new_user_id_is_given()
    {
        using var db = new SqliteInMemoryContext();
        db.Context.Users.Add(new User { Id = 101, Name = "Alice", Email = "alice@example.com", PasswordHash = "x" });
        await db.Context.SaveChangesAsync();
        var task = await SeedStaleTaskAsync(db, assignedToId: 101);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReassigns(task.Id, newUserId: null, reason: "returning to the pool");

        var sut = CreateSut(claude, tasks, users, logs);
        await sut.RunAsync(CancellationToken.None);

        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.AssignedToId.Should().BeNull();
    }

    [Fact]
    public async Task Reassign_tool_call_reports_an_error_and_makes_no_change_when_the_new_user_does_not_exist()
    {
        using var db = new SqliteInMemoryContext();
        db.Context.Users.Add(new User { Id = 101, Name = "Alice", Email = "alice@example.com", PasswordHash = "x" });
        await db.Context.SaveChangesAsync();
        var task = await SeedStaleTaskAsync(db, assignedToId: 101);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        const int nonExistentUserId = 4242;
        var claude = StubClaude.ThatReassigns(task.Id, newUserId: nonExistentUserId, reason: "does not matter");

        var sut = CreateSut(claude, tasks, users, logs);
        await sut.RunAsync(CancellationToken.None);

        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.AssignedToId.Should().Be(101); // unchanged
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l => l.Action == AgentActions.Reassigned);
    }

    [Fact]
    public async Task Reassign_tool_call_reports_not_found_and_logs_nothing_when_the_task_no_longer_exists()
    {
        using var db = new SqliteInMemoryContext();
        await SeedStaleTaskAsync(db); // anchor so the cycle has stale work to look at
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReassigns(taskId: 999, newUserId: null, reason: "does not matter");

        var sut = CreateSut(claude, tasks, users, logs);
        var act = async () => await sut.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l => l.Action == AgentActions.Reassigned);
    }

    // Epic 3 Pre-Merge Code Review, finding 6.2: FlagAsync was entirely untested.
    [Fact]
    public async Task Flag_tool_call_logs_the_concern_without_modifying_the_task()
    {
        using var db = new SqliteInMemoryContext();
        var task = await SeedStaleTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatFlags(task.Id, concern: "Not sure this is still needed.");

        var sut = CreateSut(claude, tasks, users, logs);
        await sut.RunAsync(CancellationToken.None);

        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Priority.Should().Be(TaskPriority.Medium);   // unmodified - flag is log-only
        updated.Status.Should().Be(WorkflowStatus.Todo);

        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().Contain(l =>
            l.Action == AgentActions.FlaggedForReview &&
            l.TaskId == task.Id &&
            l.Details == "Not sure this is still needed.");
    }

    [Fact]
    public async Task Flag_tool_call_reports_not_found_and_logs_nothing_when_the_task_no_longer_exists()
    {
        using var db = new SqliteInMemoryContext();
        await SeedStaleTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatFlags(taskId: 999, concern: "does not matter");

        var sut = CreateSut(claude, tasks, users, logs);
        var act = async () => await sut.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l => l.Action == AgentActions.FlaggedForReview);
    }

    // Epic 3 Pre-Merge Code Review, finding 6.2: the unknown-tool dispatch branch was untested.
    [Fact]
    public async Task Unknown_tool_call_is_reported_as_an_error_without_throwing_or_logging()
    {
        using var db = new SqliteInMemoryContext();
        await SeedStaleTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatCallsAnUnknownTool();

        var sut = CreateSut(claude, tasks, users, logs);
        var act = async () => await sut.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l =>
            l.Action == AgentActions.Escalated || l.Action == AgentActions.Reassigned || l.Action == AgentActions.FlaggedForReview);
    }

    // Epic 3 Pre-Merge Code Review, finding 6.2: the tool-dispatch exception catch was untested.
    [Fact]
    public async Task Tool_call_with_undeserializable_arguments_is_caught_and_does_not_crash_the_cycle()
    {
        using var db = new SqliteInMemoryContext();
        await SeedStaleTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatCallsToolWithUndeserializableArgs("escalate_task");

        var sut = CreateSut(claude, tasks, users, logs);
        var act = async () => await sut.RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l => l.Action == AgentActions.Escalated);
    }

    // Epic 3 Pre-Merge Code Review, finding 6.2: the !ClaudeConfigured skip was untested.
    [Fact]
    public async Task Skips_the_cycle_without_calling_Claude_when_Claude_is_not_configured()
    {
        using var db = new SqliteInMemoryContext();
        await SeedStaleTaskAsync(db);
        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(false);

        var sut = CreateSut(claude.Object, tasks, users, logs);
        await sut.RunAsync(CancellationToken.None);

        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10, callerId: 1);
        recent.Should().NotContain(l => l.Action == AgentActions.CycleActions || l.Action == AgentActions.NoActionNeeded);
    }

    [Fact]
    public async Task No_stale_tasks_skips_without_calling_Claude()
    {
        using var db = new SqliteInMemoryContext();
        // A fresh task (updated now) is not stale, so there is nothing to do.
        db.Context.Tasks.Add(new TaskItem { Title = "Fresh", Status = WorkflowStatus.Todo, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var claude = new Mock<IClaudeClient>();

        var sut = CreateSut(
            claude.Object,
            new TaskRepository(db.Context),
            new UserRepository(db.Context),
            new AgentLogRepository(db.Context));

        await sut.RunAsync(CancellationToken.None);

        // No stale tasks → the agent returns before ever calling Claude.
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
