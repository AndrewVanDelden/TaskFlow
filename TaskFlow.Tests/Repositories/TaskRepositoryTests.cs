using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class TaskRepositoryTests
{
    // PR #45 review finding: TaskResponseDto (Sprint 4R) added TailoredContent to the payload
    // GetAllAsync backs, but GetAllAsync itself was never scoped by caller - meaning any
    // authenticated user's GET /api/Tasks returned every other user's tailored resume/cover-letter
    // content, since the generic board has always been shared/unscoped by design (fine for
    // arbitrary work items, not fine for personal documents). Generic tasks (no ApplicationId) stay
    // visible to everyone, matching the existing shared-board behavior; Epic 3 sibling tasks
    // (ApplicationId set) are now visible only to the owning JobApplication's OwnerId.
    [Fact]
    public async Task GetAllAsync_hides_another_owners_Epic3_sibling_task_but_shows_generic_tasks_to_everyone()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        await db.Context.JobApplications.ExecuteDeleteAsync();
        var sut = new TaskRepository(db.Context);

        var application = new JobApplication { State = ApplicationState.Building, OwnerId = 1 };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();

        db.Context.Tasks.AddRange(
            new TaskItem { Title = "Generic task", Kind = TaskKind.Generic },
            new TaskItem { Title = "Owner's tailored resume", Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id });
        await db.Context.SaveChangesAsync();

        var asOwner = await sut.GetAllAsync(null, null, callerId: 1);
        var asOtherUser = await sut.GetAllAsync(null, null, callerId: 2);

        asOwner.Select(t => t.Title).Should().Contain(new[] { "Generic task", "Owner's tailored resume" });
        asOtherUser.Select(t => t.Title).Should().Contain("Generic task");
        asOtherUser.Select(t => t.Title).Should().NotContain("Owner's tailored resume");
    }

    // Copilot review finding (PR #48): TaskCardView gates its new export buttons on
    // task.status === 'Done' && applicationId != null, assuming that implies the JobApplication is
    // Approved - but a lone Epic-3 sibling task can reach Done via the individual per-task approve
    // path while its pair never does, leaving the application at ReviewReady/Building. The frontend
    // needs the real ApplicationState to gate correctly instead of guessing from Status alone, which
    // requires GetAllAsync (used by the board's GET /api/Tasks) to actually load the Application
    // navigation - GetByIdAsync already does this (T5.0); GetAllAsync did not.
    [Fact]
    public async Task GetAllAsync_populates_the_Application_navigation_for_an_Epic3_sibling_task()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        await db.Context.JobApplications.ExecuteDeleteAsync();
        var sut = new TaskRepository(db.Context);

        var application = new JobApplication { State = ApplicationState.Approved, OwnerId = 1 };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();

        db.Context.Tasks.Add(new TaskItem { Title = "Tailored resume", Kind = TaskKind.ResumeTailoring, ApplicationId = application.Id, Status = WorkflowStatus.Done });
        await db.Context.SaveChangesAsync();

        var tasks = await sut.GetAllAsync(null, null, callerId: 1);

        var task = tasks.Should().ContainSingle(t => t.Title == "Tailored resume").Subject;
        task.Application.Should().NotBeNull();
        task.Application!.State.Should().Be(ApplicationState.Approved);
    }

    [Fact]
    public async Task AddAsync_then_GetByIdAsync_roundtrips_a_task()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new TaskRepository(db.Context);

        var task = new TaskItem { Title = "Write tests" };
        await sut.AddAsync(task);
        await sut.SaveChangesAsync();

        var found = await sut.GetByIdAsync(task.Id);
        found.Should().NotBeNull();
        found!.Title.Should().Be("Write tests");
    }

    [Fact]
    public async Task GetStaleAsync_returns_only_open_tasks_older_than_cutoff()
    {
        using var db = new SqliteInMemoryContext();
        var sut = new TaskRepository(db.Context);

        var cutoff = DateTime.UtcNow.AddHours(-48);
        await sut.AddAsync(new TaskItem { Title = "fresh", UpdatedAt = DateTime.UtcNow });
        await sut.AddAsync(new TaskItem { Title = "stale", UpdatedAt = cutoff.AddHours(-1) });
        await sut.AddAsync(new TaskItem { Title = "done-stale", Status = WorkflowStatus.Done, UpdatedAt = cutoff.AddHours(-1) });
        await sut.SaveChangesAsync();

        var stale = await sut.GetStaleAsync(cutoff);

        stale.Should().ContainSingle(t => t.Title == "stale");
    }
}