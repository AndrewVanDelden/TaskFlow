using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

/// <summary>
/// Board Done-column soft-archive: ArchiveAsync/UnarchiveAsync (single task) and
/// ArchiveAllDoneAsync (bulk "clear all Done"), plus GetAllAsync's new archived partition.
/// Real SQLite (not mocks), matching JobApplicationRepositoryPromotionTests' pattern - proves the
/// guarded ExecuteUpdateAsync predicates actually translate and execute correctly, not just compile.
/// </summary>
public class TaskRepositoryArchiveTests
{
    // ── ArchiveAsync ──────────────────────────────────────────────────────────
    [Fact]
    public async Task ArchiveAsync_archives_a_Done_task_and_returns_true()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem { Title = "Done task", Status = WorkflowStatus.Done };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var archived = await sut.ArchiveAsync(task.Id, callerId: 1);

        archived.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        var reloaded = await sut.GetByIdAsync(task.Id);
        reloaded!.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchiveAsync_returns_false_and_changes_nothing_for_a_non_Done_task()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem { Title = "Still in progress", Status = WorkflowStatus.InProgress };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var archived = await sut.ArchiveAsync(task.Id, callerId: 1);

        archived.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await sut.GetByIdAsync(task.Id);
        reloaded!.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task ArchiveAsync_is_idempotent_and_does_not_double_timestamp_an_already_archived_task()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var originalTimestamp = DateTime.UtcNow.AddHours(-1);
        var task = new TaskItem { Title = "Already archived", Status = WorkflowStatus.Done, ArchivedAt = originalTimestamp };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var archived = await sut.ArchiveAsync(task.Id, callerId: 1);

        archived.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await sut.GetByIdAsync(task.Id);
        reloaded!.ArchivedAt.Should().BeCloseTo(originalTimestamp, TimeSpan.FromSeconds(1));
    }

    // ── UnarchiveAsync ────────────────────────────────────────────────────────
    [Fact]
    public async Task UnarchiveAsync_restores_an_archived_task_and_returns_true()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem { Title = "Archived", Status = WorkflowStatus.Done, ArchivedAt = DateTime.UtcNow };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var restored = await sut.UnarchiveAsync(task.Id, callerId: 1);

        restored.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        var reloaded = await sut.GetByIdAsync(task.Id);
        reloaded!.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task UnarchiveAsync_returns_false_for_a_task_that_was_never_archived()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var task = new TaskItem { Title = "Never archived", Status = WorkflowStatus.Done };
        db.Context.Tasks.Add(task);
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var restored = await sut.UnarchiveAsync(task.Id, callerId: 1);

        restored.Should().BeFalse();
    }

    // ── ArchiveAllDoneAsync ───────────────────────────────────────────────────
    [Fact]
    public async Task ArchiveAllDoneAsync_archives_every_visible_Done_task_and_returns_the_exact_count()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);

        var owner = new JobApplication { State = ApplicationState.Building, OwnerId = 1 };
        var otherOwner = new JobApplication { State = ApplicationState.Building, OwnerId = 2 };
        db.Context.JobApplications.AddRange(owner, otherOwner);
        await db.Context.SaveChangesAsync();

        var genericDone = new TaskItem { Title = "Generic done", Status = WorkflowStatus.Done };
        var callersEpic3Done = new TaskItem { Title = "Caller's tailored resume", Status = WorkflowStatus.Done, Kind = TaskKind.ResumeTailoring, ApplicationId = owner.Id };
        var otherOwnersEpic3Done = new TaskItem { Title = "Other owner's tailored resume", Status = WorkflowStatus.Done, Kind = TaskKind.ResumeTailoring, ApplicationId = otherOwner.Id };
        var alreadyArchived = new TaskItem { Title = "Already archived", Status = WorkflowStatus.Done, ArchivedAt = DateTime.UtcNow };
        var notDone = new TaskItem { Title = "Still in review", Status = WorkflowStatus.Review };
        db.Context.Tasks.AddRange(genericDone, callersEpic3Done, otherOwnersEpic3Done, alreadyArchived, notDone);
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var count = await sut.ArchiveAllDoneAsync(callerId: 1);

        count.Should().Be(2);
        db.Context.ChangeTracker.Clear();
        (await sut.GetByIdAsync(genericDone.Id))!.ArchivedAt.Should().NotBeNull();
        (await sut.GetByIdAsync(callersEpic3Done.Id))!.ArchivedAt.Should().NotBeNull();
        (await sut.GetByIdAsync(otherOwnersEpic3Done.Id))!.ArchivedAt.Should().BeNull();
        (await sut.GetByIdAsync(notDone.Id))!.ArchivedAt.Should().BeNull();
    }

    // ── GetAllAsync(archived: ...) ────────────────────────────────────────────
    [Fact]
    public async Task GetAllAsync_archived_false_excludes_an_archived_task_that_would_otherwise_match()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        db.Context.Tasks.AddRange(
            new TaskItem { Title = "Active", Status = WorkflowStatus.Done },
            new TaskItem { Title = "Archived", Status = WorkflowStatus.Done, ArchivedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var active = await sut.GetAllAsync(null, null, archived: false, callerId: 1);

        active.Select(t => t.Title).Should().Contain("Active");
        active.Select(t => t.Title).Should().NotContain("Archived");
    }

    [Fact]
    public async Task GetAllAsync_archived_true_returns_only_archived_tasks_and_still_respects_ownership_scoping()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);

        var owner = new JobApplication { State = ApplicationState.Building, OwnerId = 1 };
        var otherOwner = new JobApplication { State = ApplicationState.Building, OwnerId = 2 };
        db.Context.JobApplications.AddRange(owner, otherOwner);
        await db.Context.SaveChangesAsync();

        db.Context.Tasks.AddRange(
            new TaskItem { Title = "Active generic", Status = WorkflowStatus.Done },
            new TaskItem { Title = "Archived generic", Status = WorkflowStatus.Done, ArchivedAt = DateTime.UtcNow },
            new TaskItem { Title = "Caller's archived resume", Status = WorkflowStatus.Done, Kind = TaskKind.ResumeTailoring, ApplicationId = owner.Id, ArchivedAt = DateTime.UtcNow },
            new TaskItem { Title = "Other owner's archived resume", Status = WorkflowStatus.Done, Kind = TaskKind.ResumeTailoring, ApplicationId = otherOwner.Id, ArchivedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var sut = new TaskRepository(db.Context);

        var archived = await sut.GetAllAsync(null, null, archived: true, callerId: 1);

        archived.Select(t => t.Title).Should().Contain(new[] { "Archived generic", "Caller's archived resume" });
        archived.Select(t => t.Title).Should().NotContain("Active generic");
        archived.Select(t => t.Title).Should().NotContain("Other owner's archived resume");
    }

    // The seeded board has Todo tasks; clear it so each test controls exactly which tasks exist.
    private static Task StartFromEmptyBoard(AppDbContext db) => db.Tasks.ExecuteDeleteAsync();
}
