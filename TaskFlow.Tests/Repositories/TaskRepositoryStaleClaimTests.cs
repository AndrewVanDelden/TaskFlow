using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryStaleClaimTests
{
    [Fact]
    public async Task RecoverStaleInProgress_returns_a_long_stuck_InProgress_task_to_Todo_and_clears_the_owner()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem
        {
            Title = "Stuck",
            Status = WorkflowStatus.InProgress,
            Kind = TaskKind.Generic,
            ClaimedBy = "GenericExecutor"
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        task.UpdatedAt = DateTime.UtcNow.AddHours(-2);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var recovered = await repo.RecoverStaleInProgressAsync(TimeSpan.FromMinutes(30));

        recovered.Should().Be(1);
        db.Context.ChangeTracker.Clear();
        var updated = await repo.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();
    }

    [Fact]
    public async Task RecoverStaleInProgress_does_not_touch_a_recently_claimed_InProgress_task()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem
        {
            Title = "Fresh claim",
            Status = WorkflowStatus.InProgress,
            Kind = TaskKind.Generic,
            ClaimedBy = "GenericExecutor"
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        task.UpdatedAt = DateTime.UtcNow.AddMinutes(-1);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var recovered = await repo.RecoverStaleInProgressAsync(TimeSpan.FromMinutes(30));

        recovered.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        var untouched = await repo.GetByIdAsync(task.Id);
        untouched!.Status.Should().Be(WorkflowStatus.InProgress);
        untouched.ClaimedBy.Should().Be("GenericExecutor");
    }

    [Theory]
    [InlineData(WorkflowStatus.Todo)]
    [InlineData(WorkflowStatus.Done)]
    public async Task RecoverStaleInProgress_ignores_tasks_not_InProgress_regardless_of_age(WorkflowStatus status)
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem
        {
            Title = "Not in progress",
            Status = status,
            Kind = TaskKind.Generic
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        task.UpdatedAt = DateTime.UtcNow.AddHours(-2);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var recovered = await repo.RecoverStaleInProgressAsync(TimeSpan.FromMinutes(30));

        recovered.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        var untouched = await repo.GetByIdAsync(task.Id);
        untouched!.Status.Should().Be(status);
    }

    [Fact]
    public async Task RecoverStaleInProgress_returns_the_count_of_all_tasks_recovered()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var stuckOne = new TaskItem { Title = "Stuck 1", Status = WorkflowStatus.InProgress, Kind = TaskKind.Generic, ClaimedBy = "A" };
        var stuckTwo = new TaskItem { Title = "Stuck 2", Status = WorkflowStatus.InProgress, Kind = TaskKind.Generic, ClaimedBy = "B" };
        var fresh = new TaskItem { Title = "Fresh", Status = WorkflowStatus.InProgress, Kind = TaskKind.Generic, ClaimedBy = "C" };
        db.Context.Tasks.AddRange(stuckOne, stuckTwo, fresh);
        await db.Context.SaveChangesAsync();
        stuckOne.UpdatedAt = DateTime.UtcNow.AddHours(-2);
        stuckTwo.UpdatedAt = DateTime.UtcNow.AddHours(-3);
        fresh.UpdatedAt = DateTime.UtcNow.AddMinutes(-1);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var recovered = await repo.RecoverStaleInProgressAsync(TimeSpan.FromMinutes(30));

        recovered.Should().Be(2);
    }
}
