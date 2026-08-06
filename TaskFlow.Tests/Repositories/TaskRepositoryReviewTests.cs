using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryReviewTests
{
    [Fact]
    public async Task MarkForReview_moves_an_InProgress_task_to_Review()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem { Title = "In flight", Status = WorkflowStatus.InProgress, Kind = TaskKind.Generic };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var moved = await repo.MarkForReviewAsync(task.Id);

        moved.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(task.Id))!.Status.Should().Be(WorkflowStatus.Review);
    }

    [Fact]
    public async Task MarkForReview_does_nothing_when_task_is_not_InProgress()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        var task = new TaskItem { Title = "Still todo", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var moved = await repo.MarkForReviewAsync(task.Id);

        moved.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(task.Id))!.Status.Should().Be(WorkflowStatus.Todo);
    }
}
