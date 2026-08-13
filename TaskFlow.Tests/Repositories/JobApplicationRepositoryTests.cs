using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Repositories;

public class JobApplicationRepositoryTests
{
    [Fact]
    public async Task Creating_an_application_with_two_sibling_tasks_is_fetchable_by_both_repositories()
    {
        using var db = new SqliteInMemoryContext();
        await StartFromEmptyBoard(db.Context);
        var applicationRepo = new JobApplicationRepository(db.Context);
        var taskRepo = new TaskRepository(db.Context);

        var application = new JobApplication();
        await applicationRepo.AddAsync(application);
        await applicationRepo.SaveChangesAsync();

        db.Context.Tasks.Add(new TaskItem
        {
            Title = "Tailor resume",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        });
        db.Context.Tasks.Add(new TaskItem
        {
            Title = "Tailor cover letter",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id
        });
        await db.Context.SaveChangesAsync();

        var fetchedApplication = await applicationRepo.GetByIdAsync(application.Id);
        var siblingTasks = await taskRepo.GetByApplicationIdAsync(application.Id);

        fetchedApplication.Should().NotBeNull();
        fetchedApplication!.State.Should().Be(ApplicationState.Building);

        siblingTasks.Should().HaveCount(2);
        siblingTasks.Should().Contain(t => t.Kind == TaskKind.ResumeTailoring && t.Status == WorkflowStatus.Todo);
        siblingTasks.Should().Contain(t => t.Kind == TaskKind.CoverLetterTailoring && t.Status == WorkflowStatus.Todo);
    }

    // The seeded board has Todo tasks; clear it so this test controls exactly which tasks exist.
    private static Task StartFromEmptyBoard(AppDbContext db) => db.Tasks.ExecuteDeleteAsync();
}
