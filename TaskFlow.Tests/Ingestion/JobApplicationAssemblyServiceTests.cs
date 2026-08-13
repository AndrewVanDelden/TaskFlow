using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Common;
using TaskFlow.Api.Data;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class JobApplicationAssemblyServiceTests
{
    private static (JobApplicationAssemblyService Sut, AppDbContext Db) CreateSut(SqliteInMemoryContext db) =>
        (new JobApplicationAssemblyService(new JobApplicationRepository(db.Context), new ResumeContextRepository(db.Context)), db.Context);

    private static async Task SeedResumeContextAsync(AppDbContext db, string sessionId, int ownerId)
    {
        db.ResumeContexts.Add(new ResumeContext
        {
            IngestionSessionId = sessionId,
            OwnerId = ownerId,
            Content = "Base resume text."
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Assembling_with_an_existing_resume_context_creates_the_application_and_two_sibling_tasks()
    {
        using var db = new SqliteInMemoryContext();
        await SeedResumeContextAsync(db.Context, "session-A", 1);
        var (sut, ctx) = CreateSut(db);
        var posting = new TaskDraft("Backend Engineer", "Great role", TaskKind.ResumeTailoring, "Job Posting");

        var result = await sut.AssembleAsync("session-A", 1, posting);

        result.IsSuccess.Should().BeTrue();
        var application = result.Value!;
        application.State.Should().Be(ApplicationState.Building);
        application.IngestionSessionId.Should().Be("session-A");
        application.OwnerId.Should().Be(1);

        var siblings = await ctx.Tasks.Where(t => t.ApplicationId == application.Id).ToListAsync();
        siblings.Should().HaveCount(2);
        siblings.Should().Contain(t => t.Kind == TaskKind.ResumeTailoring && t.Status == WorkflowStatus.Todo);
        siblings.Should().Contain(t => t.Kind == TaskKind.CoverLetterTailoring && t.Status == WorkflowStatus.Todo);
        siblings.Select(t => t.ApplicationId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Assembling_without_a_resume_context_returns_NotFound_and_persists_nothing()
    {
        using var db = new SqliteInMemoryContext();
        var (sut, ctx) = CreateSut(db);
        var posting = new TaskDraft("Backend Engineer", "Great role", TaskKind.ResumeTailoring, "Job Posting");

        var result = await sut.AssembleAsync("session-with-no-resume", 1, posting);

        result.Status.Should().Be(ResultStatus.NotFound);
        (await ctx.JobApplications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Assembling_with_a_resume_context_owned_by_a_different_owner_returns_NotFound_and_persists_nothing()
    {
        using var db = new SqliteInMemoryContext();
        await SeedResumeContextAsync(db.Context, "session-A", 2); // owned by user 2
        var (sut, ctx) = CreateSut(db);
        var posting = new TaskDraft("Backend Engineer", "Great role", TaskKind.ResumeTailoring, "Job Posting");

        var result = await sut.AssembleAsync("session-A", 1, posting); // requested as user 1

        result.Status.Should().Be(ResultStatus.NotFound);
        (await ctx.JobApplications.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Assembling_with_a_blank_session_id_returns_Invalid_and_persists_nothing(string? sessionId)
    {
        using var db = new SqliteInMemoryContext();
        var (sut, ctx) = CreateSut(db);
        var posting = new TaskDraft("Backend Engineer", "Great role", TaskKind.ResumeTailoring, "Job Posting");

        var result = await sut.AssembleAsync(sessionId!, 1, posting);

        result.Status.Should().Be(ResultStatus.Validation);
        (await ctx.JobApplications.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Assembling_with_a_blank_posting_title_returns_Invalid_and_persists_nothing(string? title)
    {
        using var db = new SqliteInMemoryContext();
        await SeedResumeContextAsync(db.Context, "session-A", 1);
        var (sut, ctx) = CreateSut(db);
        var posting = new TaskDraft(title!, "Great role", TaskKind.ResumeTailoring, "Job Posting");

        var result = await sut.AssembleAsync("session-A", 1, posting);

        result.Status.Should().Be(ResultStatus.Validation);
        (await ctx.JobApplications.CountAsync()).Should().Be(0);
    }

    // PR #40 review (round 2): the cover-letter sibling's title is "Cover letter — " + posting.Title.
    // A posting title at exactly TaskItem.TitleMaxLength would push the combined string past the
    // column's own cap - capping the DTO's Title alone does not fix this, since the prefix adds more
    // on top. The derived title must be truncated to fit, independent of what the input cap is.
    [Fact]
    public async Task Assembling_with_a_max_length_title_produces_a_cover_letter_title_that_still_fits_the_cap()
    {
        using var db = new SqliteInMemoryContext();
        await SeedResumeContextAsync(db.Context, "session-A", 1);
        var (sut, ctx) = CreateSut(db);
        var maxLengthTitle = new string('A', TaskItem.TitleMaxLength);
        var posting = new TaskDraft(maxLengthTitle, "Great role", TaskKind.ResumeTailoring, "Job Posting");

        var result = await sut.AssembleAsync("session-A", 1, posting);

        result.IsSuccess.Should().BeTrue();
        var siblings = await ctx.Tasks.Where(t => t.ApplicationId == result.Value!.Id).ToListAsync();
        siblings.Should().OnlyContain(t => t.Title.Length <= TaskItem.TitleMaxLength);
    }
}
