using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

/// <summary>
/// Sprint 4R, Task 2: TryApprovePairAsync/TryRejectPairAsync move a ReviewReady JobApplication and
/// both its sibling tasks together in one explicit DB transaction — ExecuteUpdateAsync commits
/// immediately per call (unlike SaveChangesAsync's deferred-write model), so two separate guarded
/// updates sharing a DbContext are not atomic together on their own; the transaction is what makes
/// them all-or-nothing. Same real-SQLite-not-mocks convention as
/// JobApplicationRepositoryPromotionTests, since these prove atomicity/race behavior a mock cannot.
/// </summary>
public class JobApplicationRepositoryApprovalTests
{
    private const int OwnerId = 1;

    [Fact]
    public async Task TryApprovePair_on_a_ReviewReady_application_owned_by_the_caller_approves_both_siblings_and_the_application()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var approved = await repo.TryApprovePairAsync(application.Id, OwnerId);

        approved.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.Approved);
        var siblings = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        siblings.Should().OnlyContain(t => t.Status == WorkflowStatus.Done);
    }

    [Fact]
    public async Task TryRejectPair_on_a_ReviewReady_application_owned_by_the_caller_returns_both_siblings_to_Todo_and_the_application_to_Building()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var rejected = await repo.TryRejectPairAsync(application.Id, OwnerId);

        rejected.Should().BeTrue();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.Building);
        var siblings = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        siblings.Should().OnlyContain(t => t.Status == WorkflowStatus.Todo && t.ClaimedBy == null);
    }

    [Fact]
    public async Task TryApprovePair_with_the_wrong_owner_returns_false_and_changes_nothing()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var approved = await repo.TryApprovePairAsync(application.Id, ownerId: 999);

        approved.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
        var siblings = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        siblings.Should().OnlyContain(t => t.Status == WorkflowStatus.Review);
    }

    [Fact]
    public async Task TryRejectPair_with_the_wrong_owner_returns_false_and_changes_nothing()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var rejected = await repo.TryRejectPairAsync(application.Id, ownerId: 999);

        rejected.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
        var siblings = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        siblings.Should().OnlyContain(t => t.Status == WorkflowStatus.Review);
    }

    [Theory]
    [InlineData(ApplicationState.Building)]
    [InlineData(ApplicationState.Approved)]
    public async Task TryApprovePair_when_the_application_is_not_ReviewReady_returns_false_and_changes_nothing(ApplicationState state)
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationInState(db.Context, OwnerId, state);
        var repo = new JobApplicationRepository(db.Context);

        var approved = await repo.TryApprovePairAsync(application.Id, OwnerId);

        approved.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(state);
    }

    [Theory]
    [InlineData(ApplicationState.Building)]
    [InlineData(ApplicationState.Approved)]
    public async Task TryRejectPair_when_the_application_is_not_ReviewReady_returns_false_and_changes_nothing(ApplicationState state)
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedApplicationInState(db.Context, OwnerId, state);
        var repo = new JobApplicationRepository(db.Context);

        var rejected = await repo.TryRejectPairAsync(application.Id, OwnerId);

        rejected.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(state);
    }

    // Mirrors JobApplicationRepositoryPromotionTests' double-call proof: the guard is
    // State == ReviewReady, so once the first caller's guarded update flips the row away from
    // ReviewReady, a second attempt structurally cannot succeed — no separate SELECT to race
    // against.
    [Fact]
    public async Task TryApprovePair_called_twice_in_a_row_only_the_first_call_succeeds()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var firstCall = await repo.TryApprovePairAsync(application.Id, OwnerId);
        var secondCall = await repo.TryApprovePairAsync(application.Id, OwnerId);

        firstCall.Should().BeTrue();
        secondCall.Should().BeFalse();
    }

    // Real production usage (JobApplicationService.ApproveAsync/RejectAsync) calls GetByIdAsync,
    // then TryApprovePairAsync/TryRejectPairAsync, then GetByIdAsync again on the SAME repository/
    // DbContext instance, with no ChangeTracker.Clear() in between (that's a test-only workaround,
    // not something a service can reasonably be expected to do). ExecuteUpdateAsync bypasses EF's
    // change tracker entirely, so without AsNoTracking, the second GetByIdAsync would return the
    // first call's already-tracked (and now stale) instance via EF's identity map, silently
    // reporting the pre-update State. Proves the fix, not just the transaction's atomicity.
    [Fact]
    public async Task GetByIdAsync_reflects_TryApprovePairAsync_without_a_manual_ChangeTracker_Clear()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var beforeApprove = await repo.GetByIdAsync(application.Id);
        beforeApprove!.State.Should().Be(ApplicationState.ReviewReady);

        var approved = await repo.TryApprovePairAsync(application.Id, OwnerId);
        var afterApprove = await repo.GetByIdAsync(application.Id);

        approved.Should().BeTrue();
        afterApprove!.State.Should().Be(ApplicationState.Approved);
    }

    [Fact]
    public async Task TryRejectPair_called_twice_in_a_row_only_the_first_call_succeeds()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var firstCall = await repo.TryRejectPairAsync(application.Id, OwnerId);
        var secondCall = await repo.TryRejectPairAsync(application.Id, OwnerId);

        firstCall.Should().BeTrue();
        secondCall.Should().BeFalse();
    }

    // Copilot's automated review (PR #45) found: the Tasks-side ExecuteUpdateAsync's affected-row
    // count was never checked, so the transaction committed even if only one sibling actually
    // transitioned. Reachable via the existing, unrestricted PATCH /api/Tasks/{id}/status endpoint,
    // which lets any authenticated user move any task to any status independently of the pair
    // flow - simulated here directly against the DB, matching what that endpoint would do.
    [Fact]
    public async Task TryApprovePair_rolls_back_everything_when_a_sibling_was_moved_away_from_Review_independently()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var siblings = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        await db.Context.Tasks
            .Where(t => t.Id == siblings[0].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, WorkflowStatus.Todo));

        var approved = await repo.TryApprovePairAsync(application.Id, OwnerId);

        approved.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
        var reloaded = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        // Neither sibling moved to Done - not even the one still sitting in Review, which the old
        // code would have wrongly advanced despite the application never actually being approved.
        reloaded.Should().NotContain(t => t.Status == WorkflowStatus.Done);
    }

    [Fact]
    public async Task TryRejectPair_rolls_back_everything_when_a_sibling_was_moved_away_from_Review_independently()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var application = await SeedReviewReadyApplication(db.Context, OwnerId);
        var repo = new JobApplicationRepository(db.Context);

        var siblings = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        await db.Context.Tasks
            .Where(t => t.Id == siblings[0].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, WorkflowStatus.Done));

        var rejected = await repo.TryRejectPairAsync(application.Id, OwnerId);

        rejected.Should().BeFalse();
        db.Context.ChangeTracker.Clear();
        (await repo.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.ReviewReady);
        var reloaded = await db.Context.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        // The sibling still in Review must not be wrongly sent back to Todo by a reject the
        // application itself never actually completed.
        reloaded.Should().NotContain(t => t.Status == WorkflowStatus.Todo);
    }

    private static async Task<JobApplication> SeedReviewReadyApplication(AppDbContext db, int ownerId) =>
        await SeedApplicationInState(db, ownerId, ApplicationState.ReviewReady, WorkflowStatus.Review, WorkflowStatus.Review);

    private static async Task<JobApplication> SeedApplicationInState(
        AppDbContext db, int ownerId, ApplicationState state,
        WorkflowStatus resumeStatus = WorkflowStatus.Todo, WorkflowStatus coverLetterStatus = WorkflowStatus.Todo)
    {
        var application = new JobApplication { State = state, OwnerId = ownerId };
        db.JobApplications.Add(application);
        await db.SaveChangesAsync();

        db.Tasks.Add(new TaskItem
        {
            Title = "Tailor resume",
            Status = resumeStatus,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id,
            ClaimedBy = "ResumeTailoringAgent"
        });
        db.Tasks.Add(new TaskItem
        {
            Title = "Tailor cover letter",
            Status = coverLetterStatus,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id,
            ClaimedBy = "CoverLetterAgent"
        });
        await db.SaveChangesAsync();

        return application;
    }

    // The seeded board has Todo tasks; clear it so each test controls exactly which tasks exist.
    private static Task StartFromEmptyBoard(AppDbContext db) => db.Tasks.ExecuteDeleteAsync();
}
