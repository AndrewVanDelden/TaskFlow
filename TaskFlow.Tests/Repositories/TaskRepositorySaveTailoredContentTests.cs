using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositorySaveTailoredContentTests
{
    [Fact]
    public async Task SaveTailoredContentAndMarkForReview_saves_content_and_moves_an_InProgress_task_to_Review()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem
        {
            Title = "Tailor resume",
            Status = WorkflowStatus.InProgress,
            Kind = TaskKind.ResumeTailoring
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var moved = await repo.SaveTailoredContentAndMarkForReviewAsync(task.Id, "Tailored resume body");

        moved.Should().BeTrue();
        // Fresh, no-tracking read: ExecuteUpdateAsync bypasses the change tracker, so a tracked
        // read of `task` would be stale (see TryClaimNextAsync's comment on the same pattern).
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(task.Id);
        reloaded!.Status.Should().Be(WorkflowStatus.Review);
        reloaded.TailoredContent.Should().Be("Tailored resume body");
    }

    [Fact]
    public async Task SaveTailoredContentAndMarkForReview_does_nothing_when_task_is_still_Todo()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem
        {
            Title = "Never claimed",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var moved = await repo.SaveTailoredContentAndMarkForReviewAsync(task.Id, "Should not be saved");

        moved.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(task.Id);
        reloaded!.Status.Should().Be(WorkflowStatus.Todo);
        reloaded.TailoredContent.Should().BeNull();
    }

    [Fact]
    public async Task SaveTailoredContentAndMarkForReview_is_idempotent_and_does_not_resave_when_already_Review()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem
        {
            Title = "Already reviewed",
            Status = WorkflowStatus.Review,
            Kind = TaskKind.ResumeTailoring,
            TailoredContent = "Original content"
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);

        var moved = await repo.SaveTailoredContentAndMarkForReviewAsync(task.Id, "Attempted overwrite");

        moved.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(task.Id);
        reloaded!.Status.Should().Be(WorkflowStatus.Review);
        reloaded.TailoredContent.Should().Be("Original content");
    }

    [Fact]
    public async Task SaveTailoredContentAndMarkForReview_accepts_content_at_exactly_the_max_length_boundary()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem
        {
            Title = "Boundary case",
            Status = WorkflowStatus.InProgress,
            Kind = TaskKind.CoverLetterTailoring
        };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var repo = new TaskRepository(db.Context);
        var maxContent = new string('x', TaskItem.TailoredContentMaxLength);

        var moved = await repo.SaveTailoredContentAndMarkForReviewAsync(task.Id, maxContent);

        moved.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(task.Id);
        reloaded!.TailoredContent.Should().HaveLength(TaskItem.TailoredContentMaxLength);
        reloaded.TailoredContent.Should().Be(maxContent);
    }

    // The seeded board has Todo tasks; clear it so each test controls exactly which tasks exist.
    private static Task StartFromEmptyBoard(AppDbContext db) => db.Tasks.ExecuteDeleteAsync();
}
