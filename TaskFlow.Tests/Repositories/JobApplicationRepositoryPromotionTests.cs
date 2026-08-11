using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class JobApplicationRepositoryPromotionTests
{
    [Fact]
    public async Task TryPromoteToReviewReady_promotes_when_both_sibling_tasks_are_Review()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.Review);
        var repo = new JobApplicationRepository(db.Context);

        var promoted = await repo.TryPromoteToReviewReadyAsync(application.Id);

        promoted.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(application.Id);
        reloaded!.State.Should().Be(ApplicationState.ReviewReady);
    }

    [Fact]
    public async Task TryPromoteToReviewReady_does_not_promote_when_only_one_sibling_is_Review()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.InProgress);
        var repo = new JobApplicationRepository(db.Context);

        var promoted = await repo.TryPromoteToReviewReadyAsync(application.Id);

        promoted.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(application.Id);
        reloaded!.State.Should().Be(ApplicationState.Building);
    }

    [Fact]
    public async Task TryPromoteToReviewReady_does_not_promote_when_neither_sibling_is_Review()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Todo, WorkflowStatus.InProgress);
        var repo = new JobApplicationRepository(db.Context);

        var promoted = await repo.TryPromoteToReviewReadyAsync(application.Id);

        promoted.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(application.Id);
        reloaded!.State.Should().Be(ApplicationState.Building);
    }

    [Fact]
    public async Task TryPromoteToReviewReady_second_call_cannot_double_promote_once_already_ReviewReady()
    {
        // Proves the concurrency guard structurally: the WHERE clause requires State == Building,
        // so once the first caller's guarded UPDATE flips the row to ReviewReady, a second
        // near-simultaneous caller's guarded UPDATE matches zero rows — there is no window where
        // both calls could see Building and both succeed, because each call's success/failure is
        // decided atomically by the single UPDATE's own WHERE clause, not by a prior SELECT.
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.Review);
        var repo = new JobApplicationRepository(db.Context);

        var firstCall = await repo.TryPromoteToReviewReadyAsync(application.Id);
        var secondCall = await repo.TryPromoteToReviewReadyAsync(application.Id);

        firstCall.Should().BeTrue();
        secondCall.Should().BeFalse();
    }

    [Fact]
    public async Task TryPromoteToReviewReady_does_not_promote_a_degenerate_application_with_only_one_task()
    {
        // JobApplicationAssemblyService always creates exactly two sibling tasks, but the
        // repository-level guard must not crash — or promote — on a malformed single-task row.
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = new JobApplication { State = ApplicationState.Building };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        db.Context.Tasks.Add(new TaskItem
        {
            Title = "Tailor resume",
            Status = WorkflowStatus.Review,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        });
        await db.Context.SaveChangesAsync();
        var repo = new JobApplicationRepository(db.Context);

        var promoted = await repo.TryPromoteToReviewReadyAsync(application.Id);

        promoted.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(application.Id);
        reloaded!.State.Should().Be(ApplicationState.Building);
    }

    // Copilot's automated review (PR #43) found the original guard - Count(Review) == 2 - checks
    // that two tasks are Review, not that the two required kinds specifically are. This is not
    // reachable today (JobApplicationAssemblyService always creates exactly one of each kind), but
    // the guard itself shouldn't depend on that being the only way an application is ever built.
    [Fact]
    public async Task TryPromoteToReviewReady_does_not_promote_when_the_two_Review_tasks_are_the_same_kind()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = new JobApplication { State = ApplicationState.Building };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        db.Context.Tasks.AddRange(
            new TaskItem { Title = "Resume A", Status = WorkflowStatus.Review, Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id },
            new TaskItem { Title = "Resume B", Status = WorkflowStatus.Review, Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id });
        await db.Context.SaveChangesAsync();
        var repo = new JobApplicationRepository(db.Context);

        var promoted = await repo.TryPromoteToReviewReadyAsync(application.Id);

        promoted.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(application.Id);
        reloaded!.State.Should().Be(ApplicationState.Building);
    }

    // ── PromotePendingReviewReadyApplicationsAsync (the reconciliation sweep's repository call) ──
    // PR #43 review, round 2 (both a manual review and Copilot's automated review, independently):
    // the per-application join attempted right after a save is a best-effort trigger, not a
    // guarantee. If it's interrupted (an AgentLog write or the join call itself throwing), a
    // JobApplication can be left stuck at Building with both siblings already Review, with nothing
    // to retry it. This bulk method is what JobApplicationPromotionReconcilerService's periodic
    // sweep calls to find and fix every application in that state, not just one.

    [Fact]
    public async Task PromotePendingReviewReady_promotes_every_qualifying_application_and_returns_the_count()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var stuckA = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.Review);
        var stuckB = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.Review);
        var notYetDone = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.InProgress);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingReviewReadyApplicationsAsync();

        count.Should().Be(2);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(stuckA.Id))!.State.Should().Be(ApplicationState.ReviewReady);
        (await repo.GetByIdAsync(stuckB.Id))!.State.Should().Be(ApplicationState.ReviewReady);
        (await repo.GetByIdAsync(notYetDone.Id))!.State.Should().Be(ApplicationState.Building);
    }

    [Fact]
    public async Task PromotePendingReviewReady_returns_zero_when_nothing_qualifies()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Todo, WorkflowStatus.Todo);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingReviewReadyApplicationsAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task PromotePendingReviewReady_does_not_touch_an_application_already_ReviewReady()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Review, WorkflowStatus.Review);
        var repo = new JobApplicationRepository(db.Context);
        await repo.PromotePendingReviewReadyApplicationsAsync();

        var secondSweepCount = await repo.PromotePendingReviewReadyApplicationsAsync();

        secondSweepCount.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
    }

    [Fact]
    public async Task PromotePendingReviewReady_does_not_promote_when_the_two_Review_tasks_are_the_same_kind()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = new JobApplication { State = ApplicationState.Building };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        db.Context.Tasks.AddRange(
            new TaskItem { Title = "Resume A", Status = WorkflowStatus.Review, Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id },
            new TaskItem { Title = "Resume B", Status = WorkflowStatus.Review, Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id });
        await db.Context.SaveChangesAsync();
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingReviewReadyApplicationsAsync();

        count.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        var reloaded = await repo.GetByIdAsync(application.Id);
        reloaded!.State.Should().Be(ApplicationState.Building);
    }

    private static async Task<JobApplication> SeedApplicationWithSiblings(
        AppDbContext db, WorkflowStatus resumeStatus, WorkflowStatus coverLetterStatus)
    {
        var application = new JobApplication { State = ApplicationState.Building };
        db.JobApplications.Add(application);
        await db.SaveChangesAsync();

        db.Tasks.Add(new TaskItem
        {
            Title = "Tailor resume",
            Status = resumeStatus,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        });
        db.Tasks.Add(new TaskItem
        {
            Title = "Tailor cover letter",
            Status = coverLetterStatus,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id
        });
        await db.SaveChangesAsync();

        return application;
    }

    // The seeded board has Todo tasks; clear it so each test controls exactly which tasks exist.
    private static Task StartFromEmptyBoard(AppDbContext db) => db.Tasks.ExecuteDeleteAsync();
}
