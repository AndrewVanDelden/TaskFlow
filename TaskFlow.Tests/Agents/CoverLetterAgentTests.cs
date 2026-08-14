using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Anthropic.SDK.Messaging;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Agents;

public class CoverLetterAgentTests
{
    private const string SessionId = "session-B";
    private const int OwnerId = 2;
    private const string BaseResumeText = "Base resume: 8 years product management experience.";
    private const string SaveTool = "save_cover_letter";

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test"
        }).Build();

    private static CoverLetterAgent CreateSut(
        IClaudeClient claude,
        ITaskRepository tasks,
        IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications,
        IAgentLogRepository logs,
        IAgentNotifier notifier) =>
        new(claude, tasks, resumeContexts, jobApplications, logs, notifier,
            Config(), NullLogger<CoverLetterAgent>.Instance);

    // Seeds one JobApplication (Building) with a ResumeContext for its (session, owner) pair, and
    // two Todo sibling tasks (ResumeTailoring, CoverLetterTailoring) sharing its ApplicationId.
    private static async Task<(JobApplication Application, TaskItem ResumeTask, TaskItem CoverLetterTask)>
        SeedApplicationAsync(SqliteInMemoryContext db, bool withResumeContext = true)
    {
        await db.Context.Tasks.ExecuteDeleteAsync();
        await db.Context.JobApplications.ExecuteDeleteAsync();
        await db.Context.ResumeContexts.ExecuteDeleteAsync();

        if (withResumeContext)
        {
            db.Context.ResumeContexts.Add(new ResumeContext
            {
                IngestionSessionId = SessionId,
                OwnerId = OwnerId,
                Content = BaseResumeText
            });
        }

        var application = new JobApplication
        {
            State = ApplicationState.Building,
            IngestionSessionId = SessionId,
            OwnerId = OwnerId,
            // Mirrors the real assembly pipeline (JobApplicationAssemblyService): Company lives on
            // the JobApplication, not on TaskItem.SourceSection, which the real job-posting parsers
            // always leave empty. A prior version of this seed hand-set SourceSection instead - that
            // masked PR #55's regression, where FormatJobPosting still read SourceSection and so
            // silently stopped telling Claude which company a posting was for.
            Company = "Globex Inc"
        };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();

        var resumeTask = new TaskItem
        {
            Title = "Product Manager",
            Description = "8+ years of product experience required.",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        };
        var coverLetterTask = new TaskItem
        {
            Title = "Product Manager",
            Description = "8+ years of product experience required.",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id
        };
        db.Context.Tasks.AddRange(resumeTask, coverLetterTask);
        await db.Context.SaveChangesAsync();

        return (application, resumeTask, coverLetterTask);
    }

    [Fact]
    public async Task Claims_only_CoverLetterTailoring_tasks()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db);
        db.Context.Tasks.Add(new TaskItem { Title = "Unrelated", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic });
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, "# Cover letter\n\nContent.");

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(coverLetterTask.Id);
        updated!.ClaimedBy.Should().Be(AgentNames.CoverLetter);
    }

    [Fact]
    public async Task Saves_cover_letter_moves_to_Review_and_leaves_sibling_and_application_untouched()
    {
        using var db = new SqliteInMemoryContext();
        var (application, resumeTask, coverLetterTask) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        const string coverLetter = "# Cover letter\n\nDear hiring team, ...";
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, coverLetter);
        var notifier = new Mock<IAgentNotifier>();

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, notifier.Object);
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updatedCoverLetterTask = await tasks.GetByIdAsync(coverLetterTask.Id);
        updatedCoverLetterTask!.Status.Should().Be(WorkflowStatus.Review);
        updatedCoverLetterTask.TailoredContent.Should().Be(coverLetter);

        // No shared write target: the sibling resume task is untouched.
        var updatedResumeTask = await tasks.GetByIdAsync(resumeTask.Id);
        updatedResumeTask!.Status.Should().Be(WorkflowStatus.Todo);
        updatedResumeTask.TailoredContent.Should().BeNull();

        // The join must not fire early: only one sibling is Review.
        var updatedApplication = await jobApplications.GetByIdAsync(application.Id);
        updatedApplication!.State.Should().Be(ApplicationState.Building);

        var recent = await logs.GetRecentAsync(AgentNames.CoverLetter, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.TailoredContentSaved && l.TaskId == coverLetterTask.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.ApplicationReviewReady);

        // Epic 3 Pre-Merge Code Review, finding 1.1: scoped to the application's owner, not everyone.
        notifier.Verify(n => n.TaskMovedAsync(coverLetterTask.Id, WorkflowStatus.InProgress, OwnerId, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.TaskMovedAsync(coverLetterTask.Id, WorkflowStatus.Review, OwnerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Wraps_the_job_posting_in_the_initial_prompt_and_the_base_resume_in_the_tool_result()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, "# Cover letter");

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        var initialPrompt = claude.LastRequest!.Messages[0].Content.OfType<TextContent>().FirstOrDefault()?.Text;
        initialPrompt.Should().NotBeNull();
        var jobOpen = initialPrompt!.IndexOf("<job_posting>", StringComparison.Ordinal);
        var jobClose = initialPrompt.IndexOf("</job_posting>", StringComparison.Ordinal);
        jobOpen.Should().BeGreaterThanOrEqualTo(0);
        jobClose.Should().BeGreaterThan(jobOpen);
        initialPrompt.IndexOf(coverLetterTask.Title, StringComparison.Ordinal).Should().BeInRange(jobOpen, jobClose);

        // PR #55 review (finding 1, CONFIRMED): the company must reach the prompt via the
        // JobApplication (application.Company), since TaskItem.SourceSection is always empty for
        // job-posting-sourced tasks in the real pipeline.
        var companyLine = initialPrompt.IndexOf("Company: Globex Inc", StringComparison.Ordinal);
        companyLine.Should().BeInRange(jobOpen, jobClose);

        var allText = claude.LastRequest!.Messages
            .SelectMany(m => m.Content)
            .OfType<ToolResultContent>()
            .SelectMany(tr => tr.Content.OfType<TextContent>())
            .Select(t => t.Text)
            .FirstOrDefault(t => t.Contains("<base_resume>", StringComparison.Ordinal));

        allText.Should().NotBeNull();
        var resumeOpen = allText!.IndexOf("<base_resume>", StringComparison.Ordinal);
        var resumeClose = allText.IndexOf("</base_resume>", StringComparison.Ordinal);
        resumeOpen.Should().BeGreaterThanOrEqualTo(0);
        resumeClose.Should().BeGreaterThan(resumeOpen);
        allText.IndexOf(BaseResumeText, StringComparison.Ordinal).Should().BeInRange(resumeOpen, resumeClose);
    }

    [Fact]
    public async Task Rejects_over_length_content_and_rolls_back_to_Todo_since_nothing_was_saved()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var overLong = new string('b', TaskItem.TailoredContentMaxLength + 1);
        var claude = StubClaude.ThatSavesOnly(SaveTool, overLong);

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(coverLetterTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();
        updated.TailoredContent.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.CoverLetter, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == coverLetterTask.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.TailoredContentSaved);
    }

    [Fact]
    public async Task Rolls_back_to_Todo_when_the_cycle_ends_without_ever_saving()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReturnsText("I looked at the posting but did not save anything.");

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(coverLetterTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.CoverLetter, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == coverLetterTask.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.AutoFinalized);
    }

    [Fact]
    public async Task Rolls_the_task_back_to_Todo_when_the_cycle_throws()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatThrows();

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(coverLetterTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.CoverLetter, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == coverLetterTask.Id);
    }

    [Fact]
    public async Task Rolls_back_and_never_calls_Claude_when_no_ResumeContext_exists_for_the_application()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db, withResumeContext: false);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);

        var sut = CreateSut(claude.Object, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(coverLetterTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.CoverLetter, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == coverLetterTask.Id);
    }

    // Copilot's automated review (PR #43, round 3) found RollBackAsync's own tail
    // (RecordActionAsync/NotifyTaskMovedAsync) is unguarded: if the AgentLog write throws after
    // ReleaseClaimAsync already succeeded, the exception escapes RollBackAsync and the cycle ends
    // via an unhandled exception instead of cleanly - even though the task itself is already
    // correctly released. The claim release must not depend on the log write succeeding.
    [Fact]
    public async Task RollBackAsync_still_releases_the_claim_even_when_recording_the_rollback_log_fails()
    {
        using var db = new SqliteInMemoryContext();
        var (_, _, coverLetterTask) = await SeedApplicationAsync(db, withResumeContext: false);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var failingLogs = new Mock<IAgentLogRepository>();
        failingLogs.Setup(l => l.AddAsync(It.IsAny<AgentLog>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        failingLogs.Setup(l => l.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("log write failed"));

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);

        var sut = CreateSut(claude.Object, tasks, resumeContexts, jobApplications, failingLogs.Object, Mock.Of<IAgentNotifier>());

        await sut.RunAsync(CancellationToken.None); // must not throw

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(coverLetterTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();
    }

    // Copilot's automated review (PR #43, round 4) flagged the same pattern one spot earlier:
    // SaveAsync's own success-path log write (TailoredContentSaved) was unguarded too. If it
    // throws right after the atomic save already succeeded, the exception escapes SaveAsync,
    // misreporting a successful save as a tool error to Claude - and, more importantly, skips the
    // join attempt for this cycle entirely (the reconciliation sweep would eventually catch it, but
    // not immediately). The sibling is already Review here, so a correct fix must still attempt -
    // and succeed at - the join in the same cycle despite the log failure.
    [Fact]
    public async Task Saves_and_still_completes_the_join_in_the_same_cycle_even_when_recording_the_saved_log_fails()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        await db.Context.JobApplications.ExecuteDeleteAsync();
        await db.Context.ResumeContexts.ExecuteDeleteAsync();
        db.Context.ResumeContexts.Add(new ResumeContext { IngestionSessionId = SessionId, OwnerId = OwnerId, Content = BaseResumeText });
        var application = new JobApplication { State = ApplicationState.Building, IngestionSessionId = SessionId, OwnerId = OwnerId };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();
        var resumeTask = new TaskItem
        {
            Title = "Product Manager",
            SourceSection = "Globex Inc",
            Status = WorkflowStatus.Review, // sibling already done
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id,
            TailoredContent = "Already-tailored resume."
        };
        var coverLetterTask = new TaskItem
        {
            Title = "Product Manager",
            SourceSection = "Globex Inc",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id
        };
        db.Context.Tasks.AddRange(resumeTask, coverLetterTask);
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        // Fails only the SaveChangesAsync immediately following a TailoredContentSaved AddAsync -
        // the earlier "Claimed" log (RunAsync) and, if reached, the later "ApplicationReviewReady"
        // log must succeed normally, so this test isolates the exact step Copilot flagged instead
        // of tripping the (separate, acceptable) roll-back-before-any-work path.
        var failNextSave = false;
        var failingLogs = new Mock<IAgentLogRepository>();
        failingLogs.Setup(l => l.AddAsync(It.IsAny<AgentLog>(), It.IsAny<CancellationToken>()))
            .Returns((AgentLog log, CancellationToken _) =>
            {
                failNextSave = log.Action == AgentActions.TailoredContentSaved;
                return Task.CompletedTask;
            });
        failingLogs.Setup(l => l.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => failNextSave
                ? Task.FromException(new InvalidOperationException("log write failed"))
                : Task.CompletedTask);
        const string coverLetter = "# Cover letter\n\nDear hiring team, ...";
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, coverLetter);

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, failingLogs.Object, Mock.Of<IAgentNotifier>());

        await sut.RunAsync(CancellationToken.None); // must not throw

        db.Context.ChangeTracker.Clear();
        var updatedCoverLetterTask = await tasks.GetByIdAsync(coverLetterTask.Id);
        updatedCoverLetterTask!.Status.Should().Be(WorkflowStatus.Review);
        updatedCoverLetterTask.TailoredContent.Should().Be(coverLetter);

        var updatedApplication = await jobApplications.GetByIdAsync(application.Id);
        updatedApplication!.State.Should().Be(ApplicationState.ReviewReady);
    }
}
