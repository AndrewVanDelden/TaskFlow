using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class AgentLogRepositoryTests
{
    private static AgentLog Log(int taskId, string action, string details, DateTime at) => new()
    {
        AgentName = "Reviewer",
        Action = action,
        TaskId = taskId,
        Details = details,
        Success = false,
        CreatedAt = at,
    };

    [Fact]
    public async Task GetByTaskAndAction_returns_matching_logs_newest_first()
    {
        using var db = new SqliteInMemoryContext();
        db.Context.AgentLogs.AddRange(
            Log(1, "Rejected", "first", new DateTime(2026, 7, 28, 10, 0, 0, DateTimeKind.Utc)),
            Log(1, "Rejected", "second", new DateTime(2026, 7, 28, 11, 0, 0, DateTimeKind.Utc)),
            Log(2, "Rejected", "other task", new DateTime(2026, 7, 28, 12, 0, 0, DateTimeKind.Utc)),
            Log(1, "Claimed", "not a rejection", new DateTime(2026, 7, 28, 9, 0, 0, DateTimeKind.Utc)));
        await db.Context.SaveChangesAsync();
        var repo = new AgentLogRepository(db.Context);

        var result = await repo.GetByTaskAndActionAsync(1, "Rejected", 10);

        result.Select(l => l.Details).Should().Equal("second", "first"); // task 1, Rejected only, newest-first
    }

    // Copilot's automated review (PR #50): the SignalR ownership fix (finding 1.1) only closed the
    // live broadcast path - GET /api/AgentLogs still returned every log to any authenticated
    // caller, including Epic 3 sibling-task Details text that names another user's job posting.
    // GetRecentAsync must apply the same ownership rule TaskRepository.GetAllAsync already applies
    // to the tasks themselves.
    private async Task<SqliteInMemoryContext> SeededDbAsync()
    {
        var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        await db.Context.JobApplications.ExecuteDeleteAsync();
        await db.Context.AgentLogs.ExecuteDeleteAsync();
        return db;
    }

    [Fact]
    public async Task GetRecentAsync_includes_a_cycle_summary_log_with_no_TaskId_for_any_caller()
    {
        using var db = await SeededDbAsync();
        db.Context.AgentLogs.Add(new AgentLog { AgentName = "TaskPrioritizer", Action = "NoChangesNeeded", TaskId = null });
        await db.Context.SaveChangesAsync();
        var repo = new AgentLogRepository(db.Context);

        var result = await repo.GetRecentAsync(null, 10, callerId: 999);

        result.Should().ContainSingle(l => l.Action == "NoChangesNeeded");
    }

    [Fact]
    public async Task GetRecentAsync_includes_a_generic_tasks_log_for_any_caller()
    {
        using var db = await SeededDbAsync();
        var genericTask = new TaskItem { Title = "Shared", Kind = TaskKind.Generic };
        db.Context.Tasks.Add(genericTask);
        await db.Context.SaveChangesAsync();
        db.Context.AgentLogs.Add(new AgentLog { AgentName = "GenericExecutor", Action = "Claimed", TaskId = genericTask.Id });
        await db.Context.SaveChangesAsync();
        var repo = new AgentLogRepository(db.Context);

        var result = await repo.GetRecentAsync(null, 10, callerId: 999);

        result.Should().ContainSingle(l => l.TaskId == genericTask.Id);
    }

    [Fact]
    public async Task GetRecentAsync_includes_an_Epic3_sibling_tasks_log_only_for_its_applications_owner()
    {
        using var db = await SeededDbAsync();
        var application = new JobApplication { IngestionSessionId = "s", OwnerId = 1 };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        var siblingTask = new TaskItem { Title = "Senior Backend Engineer", Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id };
        db.Context.Tasks.Add(siblingTask);
        await db.Context.SaveChangesAsync();
        db.Context.AgentLogs.Add(new AgentLog
        {
            AgentName = "ResumeTailoringAgent",
            Action = "Claimed",
            TaskId = siblingTask.Id,
            Details = "Claimed 'Senior Backend Engineer' for tailoring."
        });
        await db.Context.SaveChangesAsync();
        var repo = new AgentLogRepository(db.Context);

        var ownersView = await repo.GetRecentAsync(null, 10, callerId: 1);
        var strangersView = await repo.GetRecentAsync(null, 10, callerId: 2);

        ownersView.Should().ContainSingle(l => l.TaskId == siblingTask.Id);
        strangersView.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentAsync_excludes_a_log_whose_task_no_longer_exists()
    {
        using var db = await SeededDbAsync();
        // No Task with Id 12345 - simulates a log left behind after its task was deleted. Fails
        // closed (excluded) rather than guessing at ownership for a row that can no longer prove it.
        db.Context.AgentLogs.Add(new AgentLog { AgentName = "GenericExecutor", Action = "Claimed", TaskId = 12345 });
        await db.Context.SaveChangesAsync();
        var repo = new AgentLogRepository(db.Context);

        var result = await repo.GetRecentAsync(null, 10, callerId: 1);

        result.Should().BeEmpty();
    }
}
