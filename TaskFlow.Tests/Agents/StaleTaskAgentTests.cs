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

    [Fact]
    public async Task Escalate_tool_call_sets_priority_High_and_logs_it()
    {
        using var db = new SqliteInMemoryContext();

        // Arrange — one stale, open task (updated long ago, not Done).
        var task = new TaskItem
        {
            Title = "Old task",
            Status = WorkflowStatus.Todo,
            Priority = TaskPriority.Medium,
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var users = new UserRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatEscalates(task.Id, reason: "overdue 10 days");

        // Constructor order: claude, tasks, users, logs, notifier, config, logger.
        var sut = new StaleTaskAgent(
            claude,
            tasks,
            users,
            logs,
            Mock.Of<IAgentNotifier>(),
            Config(),
            NullLogger<StaleTaskAgent>.Instance);

        // Act
        await sut.RunAsync(CancellationToken.None);

        // Assert — real database side effects
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Priority.Should().Be(TaskPriority.High);

        var recent = await logs.GetRecentAsync("StaleTaskDetector", 10);
        recent.Should().Contain(l => l.Action == "Escalated" && l.TaskId == task.Id);
    }

    [Fact]
    public async Task No_stale_tasks_skips_without_calling_Claude()
    {
        using var db = new SqliteInMemoryContext();
        // A fresh task (updated now) is not stale, so there is nothing to do.
        db.Context.Tasks.Add(new TaskItem { Title = "Fresh", Status = WorkflowStatus.Todo, UpdatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var claude = new Mock<IClaudeClient>();

        var sut = new StaleTaskAgent(
            claude.Object,
            new TaskRepository(db.Context),
            new UserRepository(db.Context),
            new AgentLogRepository(db.Context),
            Mock.Of<IAgentNotifier>(),
            Config(),
            NullLogger<StaleTaskAgent>.Instance);

        await sut.RunAsync(CancellationToken.None);

        // No stale tasks → the agent returns before ever calling Claude.
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
