using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryReleaseTests
{
    [Fact]
    public async Task ReleaseClaim_returns_an_InProgress_task_to_Todo_and_clears_the_owner()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem
        {
            Title = "Claimed",
            Status = WorkflowStatus.InProgress,
            Kind = TaskKind.Generic,
            ClaimedBy = "GenericExecutor"
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var released = await repo.ReleaseClaimAsync(task.Id);

        released.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        var updated = await repo.GetByIdAsync(task.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseClaim_does_nothing_when_task_is_not_InProgress()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem { Title = "Reviewed", Status = WorkflowStatus.Review, Kind = TaskKind.Generic };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var released = await repo.ReleaseClaimAsync(task.Id);

        released.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(task.Id))!.Status.Should().Be(WorkflowStatus.Review);
    }
}
