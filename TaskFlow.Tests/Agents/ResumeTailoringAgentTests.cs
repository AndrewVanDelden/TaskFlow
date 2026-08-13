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

public class ResumeTailoringAgentTests
{
    private const string SessionId = "session-A";
    private const int OwnerId = 1;
    private const string BaseResumeText = "Base resume: 5 years C# experience, led a small team.";
    private const string SaveTool = "save_tailored_resume";

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test"
        }).Build();

    private static ResumeTailoringAgent CreateSut(
        IClaudeClient claude,
        ITaskRepository tasks,
        IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications,
        IAgentLogRepository logs,
        IAgentNotifier notifier) =>
        new(claude, tasks, resumeContexts, jobApplications, logs, notifier,
            Config(), NullLogger<ResumeTailoringAgent>.Instance);

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
            OwnerId = OwnerId
        };
        db.Context.JobApplications.Add(application);
        await db.Context.SaveChangesAsync();

        var resumeTask = new TaskItem
        {
            Title = "Senior Backend Engineer",
            Description = "5+ years of backend experience required.",
            SourceSection = "Acme Corp",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        };
        var coverLetterTask = new TaskItem
        {
            Title = "Senior Backend Engineer",
            Description = "5+ years of backend experience required.",
            SourceSection = "Acme Corp",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id
        };
        db.Context.Tasks.AddRange(resumeTask, coverLetterTask);
        await db.Context.SaveChangesAsync();

        return (application, resumeTask, coverLetterTask);
    }

    [Fact]
    public async Task Claims_only_ResumeTailoring_tasks()
    {
        using var db = new SqliteInMemoryContext();
        var (_, resumeTask, _) = await SeedApplicationAsync(db);
        // A generic task should never be claimed by this agent.
        db.Context.Tasks.Add(new TaskItem { Title = "Unrelated", Status = WorkflowStatus.Todo, Kind = TaskKind.Generic });
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, "# Tailored resume\n\nContent.");

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(resumeTask.Id);
        updated!.ClaimedBy.Should().Be(AgentNames.ResumeTailoring);
    }

    [Fact]
    public async Task Saves_tailored_content_moves_to_Review_and_leaves_sibling_and_application_untouched()
    {
        using var db = new SqliteInMemoryContext();
        var (application, resumeTask, coverLetterTask) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        const string tailored = "# Tailored resume\n\nRewritten summary and bullets.";
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, tailored);
        var notifier = new Mock<IAgentNotifier>();

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, notifier.Object);
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updatedResumeTask = await tasks.GetByIdAsync(resumeTask.Id);
        updatedResumeTask!.Status.Should().Be(WorkflowStatus.Review);
        updatedResumeTask.TailoredContent.Should().Be(tailored);

        // No shared write target: the sibling cover-letter task is untouched.
        var updatedCoverLetterTask = await tasks.GetByIdAsync(coverLetterTask.Id);
        updatedCoverLetterTask!.Status.Should().Be(WorkflowStatus.Todo);
        updatedCoverLetterTask.TailoredContent.Should().BeNull();

        // The join must not fire early: only one sibling is Review.
        var updatedApplication = await jobApplications.GetByIdAsync(application.Id);
        updatedApplication!.State.Should().Be(ApplicationState.Building);

        var recent = await logs.GetRecentAsync(AgentNames.ResumeTailoring, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.TailoredContentSaved && l.TaskId == resumeTask.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.ApplicationReviewReady);

        // Epic 3 Pre-Merge Code Review, finding 1.1: scoped to the application's owner, not everyone.
        notifier.Verify(n => n.TaskMovedAsync(resumeTask.Id, WorkflowStatus.InProgress, OwnerId, It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.TaskMovedAsync(resumeTask.Id, WorkflowStatus.Review, OwnerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Wraps_the_job_posting_in_the_initial_prompt_and_the_base_resume_in_the_tool_result()
    {
        using var db = new SqliteInMemoryContext();
        var (_, resumeTask, _) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, "# Tailored resume");

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        // The initial prompt (Messages[0], present on every request since history is cumulative)
        // carries the job posting wrapped as untrusted input under a distinct label.
        var initialPrompt = claude.LastRequest!.Messages[0].Content.OfType<TextContent>().FirstOrDefault()?.Text;
        initialPrompt.Should().NotBeNull();
        var jobOpen = initialPrompt!.IndexOf("<job_posting>", StringComparison.Ordinal);
        var jobClose = initialPrompt.IndexOf("</job_posting>", StringComparison.Ordinal);
        jobOpen.Should().BeGreaterThanOrEqualTo(0);
        jobClose.Should().BeGreaterThan(jobOpen);
        initialPrompt.IndexOf(resumeTask.Title, StringComparison.Ordinal).Should().BeInRange(jobOpen, jobClose);

        // The read_base_context tool result (fed back into the conversation) carries the base
        // resume wrapped as untrusted input under its own distinct label, with the real content
        // inside the delimiters.
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
        var (_, resumeTask, _) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var overLong = new string('a', TaskItem.TailoredContentMaxLength + 1);
        var claude = StubClaude.ThatSavesOnly(SaveTool, overLong);

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(resumeTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);   // rolled back: nothing was ever successfully saved
        updated.ClaimedBy.Should().BeNull();
        updated.TailoredContent.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.ResumeTailoring, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == resumeTask.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.TailoredContentSaved);
    }

    [Fact]
    public async Task Rolls_back_to_Todo_when_the_cycle_ends_without_ever_saving()
    {
        using var db = new SqliteInMemoryContext();
        var (_, resumeTask, _) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        // Claude only chats; never calls save_tailored_resume.
        var claude = StubClaude.ThatReturnsText("I looked at the posting but did not save anything.");

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(resumeTask.Id);
        // Deliberate difference from GenericExecutorAgent: NOT auto-finalized to Review.
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.ResumeTailoring, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == resumeTask.Id);
        recent.Should().NotContain(l => l.Action == AgentActions.AutoFinalized);
    }

    [Fact]
    public async Task Rolls_the_task_back_to_Todo_when_the_cycle_throws()
    {
        using var db = new SqliteInMemoryContext();
        var (_, resumeTask, _) = await SeedApplicationAsync(db);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);
        var claude = StubClaude.ThatThrows();

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);   // must not throw

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(resumeTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.ResumeTailoring, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == resumeTask.Id);
    }

    [Fact]
    public async Task Rolls_back_and_never_calls_Claude_when_no_ResumeContext_exists_for_the_application()
    {
        using var db = new SqliteInMemoryContext();
        var (_, resumeTask, _) = await SeedApplicationAsync(db, withResumeContext: false);

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(true);

        var sut = CreateSut(claude.Object, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>());
        await sut.RunAsync(CancellationToken.None);

        // Fail before spending an API call on unusable state.
        claude.Verify(c => c.SendAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()), Times.Never);

        db.Context.ChangeTracker.Clear();
        var updated = await tasks.GetByIdAsync(resumeTask.Id);
        updated!.Status.Should().Be(WorkflowStatus.Todo);
        updated.ClaimedBy.Should().BeNull();

        var recent = await logs.GetRecentAsync(AgentNames.ResumeTailoring, 10, OwnerId);
        recent.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == resumeTask.Id);
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
        var (_, resumeTask, _) = await SeedApplicationAsync(db, withResumeContext: false);

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
        var updated = await tasks.GetByIdAsync(resumeTask.Id);
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
            Title = "Senior Backend Engineer",
            SourceSection = "Acme Corp",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        };
        var coverLetterTask = new TaskItem
        {
            Title = "Senior Backend Engineer",
            SourceSection = "Acme Corp",
            Status = WorkflowStatus.Review, // sibling already done
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id,
            TailoredContent = "Already-tailored cover letter."
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
        const string tailoredResume = "# Tailored resume\n\nRewritten summary and bullets.";
        var claude = StubClaude.ThatReadsContextThenSaves(SaveTool, tailoredResume);

        var sut = CreateSut(claude, tasks, resumeContexts, jobApplications, failingLogs.Object, Mock.Of<IAgentNotifier>());

        await sut.RunAsync(CancellationToken.None); // must not throw

        db.Context.ChangeTracker.Clear();
        var updatedResumeTask = await tasks.GetByIdAsync(resumeTask.Id);
        updatedResumeTask!.Status.Should().Be(WorkflowStatus.Review);
        updatedResumeTask.TailoredContent.Should().Be(tailoredResume);

        var updatedApplication = await jobApplications.GetByIdAsync(application.Id);
        updatedApplication!.State.Should().Be(ApplicationState.ReviewReady);
    }
}
