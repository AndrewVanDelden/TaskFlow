using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Agents;

/// <summary>
/// Proves T3R.3 (parallel/independent), T3R.4 (atomic join), and T3R.5 (failure isolation) as one
/// flow, matching how Sprint 3R's own doc describes them as one scenario rather than three
/// independent unit facts: one JobApplication, two sibling agents, a failure and a retry, and the
/// atomic join firing only once both siblings are actually done.
/// </summary>
public class TailoringAgentsIntegrationTests
{
    private const string SessionId = "session-int";
    private const int OwnerId = 1;
    private const string BaseResumeText = "Base resume: 6 years full-stack experience.";
    private const string ResumeSaveTool = "save_tailored_resume";
    private const string CoverLetterSaveTool = "save_cover_letter";

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "test"
        }).Build();

    private static ResumeTailoringAgent CreateResumeAgent(
        IClaudeClient claude, ITaskRepository tasks, IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications, IAgentLogRepository logs) =>
        new(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>(),
            Config(), NullLogger<ResumeTailoringAgent>.Instance);

    private static CoverLetterAgent CreateCoverLetterAgent(
        IClaudeClient claude, ITaskRepository tasks, IResumeContextRepository resumeContexts,
        IJobApplicationRepository jobApplications, IAgentLogRepository logs) =>
        new(claude, tasks, resumeContexts, jobApplications, logs, Mock.Of<IAgentNotifier>(),
            Config(), NullLogger<CoverLetterAgent>.Instance);

    [Fact]
    public async Task Parallel_agents_join_atomically_and_a_failure_never_destroys_the_others_output()
    {
        using var db = new SqliteInMemoryContext();
        await db.Context.Tasks.ExecuteDeleteAsync();
        await db.Context.JobApplications.ExecuteDeleteAsync();
        await db.Context.ResumeContexts.ExecuteDeleteAsync();

        db.Context.ResumeContexts.Add(new ResumeContext
        {
            IngestionSessionId = SessionId,
            OwnerId = OwnerId,
            Content = BaseResumeText
        });
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
            Title = "Staff Engineer",
            Description = "6+ years full-stack experience.",
            SourceSection = "Initech",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.ResumeTailoring,
            ApplicationId = application.Id
        };
        var coverLetterTask = new TaskItem
        {
            Title = "Staff Engineer",
            Description = "6+ years full-stack experience.",
            SourceSection = "Initech",
            Status = WorkflowStatus.Todo,
            Kind = TaskKind.CoverLetterTailoring,
            ApplicationId = application.Id
        };
        db.Context.Tasks.AddRange(resumeTask, coverLetterTask);
        await db.Context.SaveChangesAsync();

        var tasks = new TaskRepository(db.Context);
        var resumeContexts = new ResumeContextRepository(db.Context);
        var jobApplications = new JobApplicationRepository(db.Context);
        var logs = new AgentLogRepository(db.Context);

        // ── Step 1: ResumeTailoringAgent succeeds. Runs independently of the cover-letter agent
        //    (T3R.3): only the resume task is claimed/written, the application is untouched. ──
        const string tailoredResume = "# Tailored resume\n\nRewritten for Staff Engineer at Initech.";
        var resumeAgent = CreateResumeAgent(
            StubClaude.ThatReadsContextThenSaves(ResumeSaveTool, tailoredResume),
            tasks, resumeContexts, jobApplications, logs);
        await resumeAgent.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        (await tasks.GetByIdAsync(resumeTask.Id))!.Status.Should().Be(WorkflowStatus.Review);
        (await tasks.GetByIdAsync(resumeTask.Id))!.TailoredContent.Should().Be(tailoredResume);
        (await jobApplications.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.Building);

        // ── Step 2: CoverLetterAgent's cycle throws. T3R.5: only its own task rolls back; the
        //    already-saved resume output and the application state are untouched by the failure. ──
        var failingCoverLetterAgent = CreateCoverLetterAgent(
            StubClaude.ThatThrows(), tasks, resumeContexts, jobApplications, logs);
        await failingCoverLetterAgent.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var coverLetterAfterFailure = await tasks.GetByIdAsync(coverLetterTask.Id);
        coverLetterAfterFailure!.Status.Should().Be(WorkflowStatus.Todo);
        coverLetterAfterFailure.ClaimedBy.Should().BeNull();

        // The join must not fire on a partial failure.
        (await jobApplications.GetByIdAsync(application.Id))!.State.Should().Be(ApplicationState.Building);

        // Failure isolation: the resume agent's already-saved output is unchanged by the other
        // agent's failure.
        (await tasks.GetByIdAsync(resumeTask.Id))!.TailoredContent.Should().Be(tailoredResume);
        (await tasks.GetByIdAsync(resumeTask.Id))!.Status.Should().Be(WorkflowStatus.Review);

        var failureLogs = await logs.GetRecentAsync(AgentNames.CoverLetter, 10);
        failureLogs.Should().Contain(l => l.Action == AgentActions.RolledBack && l.TaskId == coverLetterTask.Id);

        // ── Step 3: the failed task is retried (it's back in Todo, so the next cycle re-claims
        //    it). This time CoverLetterAgent succeeds -> both siblings are Review -> the atomic
        //    join fires (T3R.4). ──
        const string coverLetter = "# Cover letter\n\nDear Initech hiring team, ...";
        var succeedingCoverLetterAgent = CreateCoverLetterAgent(
            StubClaude.ThatReadsContextThenSaves(CoverLetterSaveTool, coverLetter),
            tasks, resumeContexts, jobApplications, logs);
        await succeedingCoverLetterAgent.RunAsync(CancellationToken.None);

        db.Context.ChangeTracker.Clear();
        var coverLetterAfterRetry = await tasks.GetByIdAsync(coverLetterTask.Id);
        coverLetterAfterRetry!.Status.Should().Be(WorkflowStatus.Review);
        coverLetterAfterRetry.TailoredContent.Should().Be(coverLetter);

        var finalApplication = await jobApplications.GetByIdAsync(application.Id);
        finalApplication!.State.Should().Be(ApplicationState.ReviewReady);

        var successLogs = await logs.GetRecentAsync(AgentNames.CoverLetter, 10);
        successLogs.Should().Contain(l => l.Action == AgentActions.ApplicationReviewReady && l.TaskId == coverLetterTask.Id);
    }
}
