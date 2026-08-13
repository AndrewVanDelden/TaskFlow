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

public class TaskPrioritizerAgentTests
{
    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test"
        }).Build();

    [Fact]
    public async Task Update_priority_tool_call_changes_priority_and_logs_it()
    {
        using var db = new SqliteInMemoryContext();

        // Arrange — one open task at Low priority.
        var task = new TaskItem
        {
            Title = "Low priority task",
            Status = WorkflowStatus.Todo,
            Priority = TaskPriority.Low,
            UpdatedAt = DateTime.UtcNow
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatUpdatesPriority(task.Id, priority: "High", reasoning: "overdue and blocking");

        // Constructor order: claude, tasks, logs, notifier, config, logger.
        var sut = new TaskPrioritizerAgent(
            claude,
            tasks,
            logs,
            Mock.Of<IAgentNotifier>(),
            Config(),
            NullLogger<TaskPrioritizerAgent>.Instance);

        // Act
        await sut.RunAsync(CancellationToken.None);

        // Assert — real database side effects
        var updated = await tasks.GetByIdAsync(task.Id);
        updated!.Priority.Should().Be(TaskPriority.High);

        var recent = await logs.GetRecentAsync("TaskPrioritizer", 10, callerId: 1);
        recent.Should().Contain(l => l.Action == "PriorityUpdated" && l.TaskId == task.Id);
    }

    [Fact]
    public async Task No_open_tasks_skips_without_calling_Claude()
    {
        using var db = new SqliteInMemoryContext();
        // Only a Done task exists, so there is nothing open to prioritize.
        db.Context.Tasks.Add(new TaskItem
        {
            Title = "Finished",
            Status = WorkflowStatus.Done,
            UpdatedAt = DateTime.UtcNow
        });
        await db.Context.SaveChangesAsync();

        var claude = new Mock<IClaudeClient>();

        var sut = new TaskPrioritizerAgent(
            claude.Object,
            new TaskRepository(db.Context),
            new AgentLogRepository(db.Context),
            Mock.Of<IAgentNotifier>(),
            Config(),
            NullLogger<TaskPrioritizerAgent>.Instance);

        await sut.RunAsync(CancellationToken.None);

        // No open tasks → the agent returns before ever calling Claude.
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
