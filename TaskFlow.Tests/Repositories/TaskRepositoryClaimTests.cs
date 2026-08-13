using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryClaimTests
{
    [Fact]
    public async Task TryClaimNext_claims_a_todo_task_and_stamps_the_owner()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var repo = new TaskRepository(db.Context);
        db.Context.Tasks.Add(new TaskItem { Title = "Work me", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic });
        await db.Context.SaveChangesAsync();

        var claimed = await repo.TryClaimNextAsync(TaskKind.Generic, "GenericExecutor");

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(WorkflowStatus.InProgress);
        claimed.ClaimedBy.Should().Be("GenericExecutor");
    }

    [Fact]
    public async Task TryClaimNext_returns_null_when_no_todo_task_of_the_kind_exists()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var repo = new TaskRepository(db.Context);

        var claimed = await repo.TryClaimNextAsync(TaskKind.Generic, "GenericExecutor");

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimNext_does_not_claim_the_same_task_twice()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var repo = new TaskRepository(db.Context);
        db.Context.Tasks.Add(new TaskItem { Title = "Only one", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic });
        await db.Context.SaveChangesAsync();

        var first = await repo.TryClaimNextAsync(TaskKind.Generic, "AgentA");
        var second = await repo.TryClaimNextAsync(TaskKind.Generic, "AgentB");

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task TryClaimNext_filters_by_kind_across_generic_resume_and_cover_letter_tasks()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var repo = new TaskRepository(db.Context);
        db.Context.Tasks.Add(new TaskItem { Title = "Generic work", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic });
        db.Context.Tasks.Add(new TaskItem { Title = "Tailor resume", Status = WorkflowStatus.Todo, Kind = TaskKind.ResumeTailoring });
        db.Context.Tasks.Add(new TaskItem { Title = "Tailor cover letter", Status = WorkflowStatus.Todo, Kind = TaskKind.CoverLetterTailoring });
        await db.Context.SaveChangesAsync();

        var claimedResume = await repo.TryClaimNextAsync(TaskKind.ResumeTailoring, "ResumeExecutor");
        var claimedCoverLetter = await repo.TryClaimNextAsync(TaskKind.CoverLetterTailoring, "CoverLetterExecutor");
        var claimedResumeAgain = await repo.TryClaimNextAsync(TaskKind.ResumeTailoring, "ResumeExecutor");
        var claimedGeneric = await repo.TryClaimNextAsync(TaskKind.Generic, "GenericExecutor");

        claimedResume.Should().NotBeNull();
        claimedResume!.Title.Should().Be("Tailor resume");
        claimedCoverLetter.Should().NotBeNull();
        claimedCoverLetter!.Title.Should().Be("Tailor cover letter");
        claimedResumeAgain.Should().BeNull();
        claimedGeneric.Should().NotBeNull();
        claimedGeneric!.Title.Should().Be("Generic work");
    }

    // Epic 3 Pre-Merge Code Review, finding 1.1: agents need the claimed task's owner to scope
    // SignalR broadcasts, so the claim read must include Application the same way GetByIdAsync
    // already does - otherwise TaskItem.OwnerId throws (fails closed) the moment an agent tries
    // to read it off a claimed Epic 3 sibling task.
    [Fact]
    public async Task TryClaimNext_includes_the_application_navigation_so_OwnerId_is_available()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        db.Context.JobApplications.ExecuteDelete();
        var repo = new TaskRepository(db.Context);
        var application = new JobApplication { IngestionSessionId = "s", OwnerId = 42 };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        db.Context.Tasks.Add(new TaskItem
        {
            Title = "Tailor resume",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        });
        await db.Context.SaveChangesAsync();

        var claimed = await repo.TryClaimNextAsync(TaskKind.ResumeTailoring, "ResumeExecutor");

        claimed.Should().NotBeNull();
        claimed!.OwnerId.Should().Be(42);
    }

    // The seeded board has Todo tasks; clear it so each test controls exactly which tasks exist.
    private static Task StartFromEmptyBoard(AppDbContext db) => db.Tasks.ExecuteDeleteAsync();
}
