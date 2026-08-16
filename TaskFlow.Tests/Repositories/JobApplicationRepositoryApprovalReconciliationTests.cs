using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

/// <summary>
/// Board bug (found 2026-08-14, reproduced against real data): before TaskService.ApproveAsync/
/// RejectAsync/UpdateStatusAsync gained their pair-invariant guard, an Epic-3 sibling task approved,
/// rejected, or drag-moved to Done individually left its JobApplication permanently stuck below
/// Approved - no retry path ever fixed it, and the Board's export-download gating
/// (applicationState === 'Approved') correctly refused to show real, already-generated content that
/// was otherwise unreachable. The new guard stops this from happening again; this reconciliation
/// sweep repairs applications already stuck in that state (including any created before the guard
/// existed) by promoting to Approved whenever both required siblings are already Done. Mirrors
/// JobApplicationRepositoryPromotionTests' shape exactly - same real-SQLite-not-mocks convention,
/// same "bulk sweep repository method backing a periodic reconciler service" precedent.
/// </summary>
public class JobApplicationRepositoryApprovalReconciliationTests
{
    [Fact]
    public async Task PromotePendingApproved_promotes_when_both_sibling_tasks_are_Done()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Done, WorkflowStatus.Done, ApplicationState.ReviewReady);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingApprovedApplicationsAsync();

        count.Should().Be(1);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.Approved);
    }

    [Fact]
    public async Task PromotePendingApproved_does_not_promote_when_only_one_sibling_is_Done()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Done, WorkflowStatus.Review, ApplicationState.ReviewReady);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingApprovedApplicationsAsync();

        count.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
    }

    [Fact]
    public async Task PromotePendingApproved_does_not_touch_an_application_already_Approved()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Done, WorkflowStatus.Done, ApplicationState.Approved);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingApprovedApplicationsAsync();

        count.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.Approved);
    }

    [Fact]
    public async Task PromotePendingApproved_promotes_every_qualifying_application_and_returns_the_count()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var stuckA = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Done, WorkflowStatus.Done, ApplicationState.ReviewReady);
        var stuckB = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Done, WorkflowStatus.Done, ApplicationState.Building);
        var notYetDone = await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Done, WorkflowStatus.Review, ApplicationState.ReviewReady);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingApprovedApplicationsAsync();

        count.Should().Be(2);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(stuckA.Id))!.State.Should().Be(ApplicationState.Approved);
        (await repo.GetByIdAsync(stuckB.Id))!.State.Should().Be(ApplicationState.Approved);
        (await repo.GetByIdAsync(notYetDone.Id))!.State.Should().Be(ApplicationState.ReviewReady);
    }

    [Fact]
    public async Task PromotePendingApproved_returns_zero_when_nothing_qualifies()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        await SeedApplicationWithSiblings(db.Context, WorkflowStatus.Todo, WorkflowStatus.Todo, ApplicationState.Building);
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingApprovedApplicationsAsync();

        count.Should().Be(0);
    }

    // Mirrors JobApplicationRepositoryPromotionTests' identical-kind guard: the check must be that
    // the two REQUIRED kinds are each Done, not merely that two Done tasks exist.
    [Fact]
    public async Task PromotePendingApproved_does_not_promote_when_the_two_Done_tasks_are_the_same_kind()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = new JobApplication { State = ApplicationState.ReviewReady };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        db.Context.Tasks.AddRange(
            new TaskItem { Title = "Resume A", Status = WorkflowStatus.Done, Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id },
            new TaskItem { Title = "Resume B", Status = WorkflowStatus.Done, Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id });
        await db.Context.SaveChangesAsync();
        var repo = new JobApplicationRepository(db.Context);

        var count = await repo.PromotePendingApprovedApplicationsAsync();

        count.Should().Be(0);
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
    }

    private static async Task<JobApplication> SeedApplicationWithSiblings(
        AppDbContext db, WorkflowStatus resumeStatus, WorkflowStatus coverLetterStatus, ApplicationState state)
    {
        var application = new JobApplication { State = state };
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
