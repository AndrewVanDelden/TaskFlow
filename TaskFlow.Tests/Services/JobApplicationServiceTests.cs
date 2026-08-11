using FluentAssertions;
using Moq;
using TaskFlow.Api.Agents;
using TaskFlow.Api.Common;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

/// <summary>
/// Sprint 4R, Task 4: JobApplicationService drives the pair-level approve/reject flow — ownership
/// and state guards, then the atomic repository call, then per-sibling notify (and, for reject, an
/// AgentLog per sibling). Mocked repositories, mirroring ResumeContextServiceTests' convention (not
/// the real-SQLite convention JobApplicationRepositoryApprovalTests uses) since the atomicity itself
/// is already proven at the repository layer — this is a service test, not a repository test.
/// </summary>
public class JobApplicationServiceTests
{
    private const int OwnerId = 1;
    private const int ApplicationId = 5;

    private readonly Mock<IJobApplicationRepository> _applications = new();
    private readonly Mock<ITaskRepository> _tasks = new();
    private readonly Mock<IAgentLogRepository> _logs = new();
    private readonly Mock<IAgentNotifier> _notifier = new();

    private JobApplicationService CreateSut() => new(_applications.Object, _tasks.Object, _logs.Object, _notifier.Object);

    private static JobApplication ReviewReadyApplication(int ownerId = OwnerId) => new()
    {
        Id = ApplicationId,
        OwnerId = ownerId,
        State = ApplicationState.ReviewReady,
        IngestionSessionId = "session-A"
    };

    private static List<TaskItem> Siblings(WorkflowStatus status) => new()
    {
        new TaskItem { Id = 10, Title = "Tailor resume", Kind = TaskKind.ResumeTailoring, ApplicationId = ApplicationId, Status = status },
        new TaskItem { Id = 11, Title = "Tailor cover letter", Kind = TaskKind.CoverLetterTailoring, ApplicationId = ApplicationId, Status = status }
    };

    // ── Approve ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task ApproveAsync_on_a_ReviewReady_application_owned_by_the_caller_returns_Ok_and_notifies_both_siblings_Done()
    {
        var application = ReviewReadyApplication();
        _applications.SetupSequence(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application)
            .ReturnsAsync(new JobApplication { Id = ApplicationId, OwnerId = OwnerId, State = ApplicationState.Approved, IngestionSessionId = "session-A" });
        _applications.Setup(a => a.TryApprovePairAsync(ApplicationId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var siblings = Siblings(WorkflowStatus.Done);
        _tasks.Setup(t => t.GetByApplicationIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync(siblings);

        var result = await CreateSut().ApproveAsync(ApplicationId, OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(ApplicationState.Approved);
        result.Value!.Tasks.Should().HaveCount(2);
        _applications.Verify(a => a.TryApprovePairAsync(ApplicationId, OwnerId, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TaskMovedAsync(10, WorkflowStatus.Done, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TaskMovedAsync(11, WorkflowStatus.Done, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_returns_NotFound_when_the_application_does_not_exist()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync((JobApplication?)null);

        var result = await CreateSut().ApproveAsync(ApplicationId, OwnerId);

        result.Status.Should().Be(ResultStatus.NotFound);
        _applications.Verify(a => a.TryApprovePairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _applications.Verify(a => a.TryRejectPairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // IDOR-safe convention: a cross-owner probe must be indistinguishable from a genuine 404, so
    // this asserts specifically NotFound, not just "not Ok".
    [Fact]
    public async Task ApproveAsync_returns_NotFound_when_the_application_is_owned_by_someone_else()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReviewReadyApplication(ownerId: 999));

        var result = await CreateSut().ApproveAsync(ApplicationId, OwnerId);

        result.Status.Should().Be(ResultStatus.NotFound);
        _applications.Verify(a => a.TryApprovePairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_returns_Invalid_when_the_application_is_not_ReviewReady()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobApplication { Id = ApplicationId, OwnerId = OwnerId, State = ApplicationState.Building });

        var result = await CreateSut().ApproveAsync(ApplicationId, OwnerId);

        result.Status.Should().Be(ResultStatus.Validation);
        _applications.Verify(a => a.TryApprovePairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_returns_Conflict_when_TryApprovePairAsync_loses_the_race()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewReadyApplication());
        _applications.Setup(a => a.TryApprovePairAsync(ApplicationId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().ApproveAsync(ApplicationId, OwnerId);

        result.Status.Should().Be(ResultStatus.Conflict);
        _tasks.Verify(t => t.GetByApplicationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Reject ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RejectAsync_on_a_ReviewReady_application_owned_by_the_caller_returns_Ok_logs_and_notifies_both_siblings_Todo()
    {
        var application = ReviewReadyApplication();
        _applications.SetupSequence(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application)
            .ReturnsAsync(new JobApplication { Id = ApplicationId, OwnerId = OwnerId, State = ApplicationState.Building, IngestionSessionId = "session-A" });
        _applications.Setup(a => a.TryRejectPairAsync(ApplicationId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var siblings = Siblings(WorkflowStatus.Todo);
        _tasks.Setup(t => t.GetByApplicationIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync(siblings);

        var result = await CreateSut().RejectAsync(ApplicationId, OwnerId, "Needs more punch.");

        result.IsSuccess.Should().BeTrue();
        result.Value!.State.Should().Be(ApplicationState.Building);
        _applications.Verify(a => a.TryRejectPairAsync(ApplicationId, OwnerId, It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(l => l.AddAsync(
            It.Is<AgentLog>(log => log.TaskId == 10 && log.Action == AgentActions.Rejected && log.Details == "Needs more punch."),
            It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(l => l.AddAsync(
            It.Is<AgentLog>(log => log.TaskId == 11 && log.Action == AgentActions.Rejected && log.Details == "Needs more punch."),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TaskMovedAsync(10, WorkflowStatus.Todo, It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TaskMovedAsync(11, WorkflowStatus.Todo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_returns_NotFound_when_the_application_does_not_exist()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync((JobApplication?)null);

        var result = await CreateSut().RejectAsync(ApplicationId, OwnerId, "reason");

        result.Status.Should().Be(ResultStatus.NotFound);
        _applications.Verify(a => a.TryRejectPairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_returns_NotFound_when_the_application_is_owned_by_someone_else()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReviewReadyApplication(ownerId: 999));

        var result = await CreateSut().RejectAsync(ApplicationId, OwnerId, "reason");

        result.Status.Should().Be(ResultStatus.NotFound);
        _applications.Verify(a => a.TryRejectPairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_returns_Invalid_when_the_application_is_not_ReviewReady()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobApplication { Id = ApplicationId, OwnerId = OwnerId, State = ApplicationState.Approved });

        var result = await CreateSut().RejectAsync(ApplicationId, OwnerId, "reason");

        result.Status.Should().Be(ResultStatus.Validation);
        _applications.Verify(a => a.TryRejectPairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_returns_Conflict_when_TryRejectPairAsync_loses_the_race()
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewReadyApplication());
        _applications.Setup(a => a.TryRejectPairAsync(ApplicationId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().RejectAsync(ApplicationId, OwnerId, "reason");

        result.Status.Should().Be(ResultStatus.Conflict);
        _tasks.Verify(t => t.GetByApplicationIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _logs.Verify(l => l.AddAsync(It.IsAny<AgentLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Copilot's automated review (PR #45) found: [Required] on RejectTaskDto.Reason rejects null
    // and "" but not whitespace-only strings, so a reason of "   " passed model validation and
    // reached this service, producing a useless audit log entry. Checked explicitly here rather
    // than relying on the DTO annotation alone, matching this project's established pattern
    // (e.g. ResumeContextService.SaveAsync's explicit IsNullOrWhiteSpace checks).
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAsync_returns_Invalid_when_the_reason_is_blank(string reason)
    {
        _applications.Setup(a => a.GetByIdAsync(ApplicationId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewReadyApplication());

        var result = await CreateSut().RejectAsync(ApplicationId, OwnerId, reason);

        result.Status.Should().Be(ResultStatus.Validation);
        _applications.Verify(a => a.TryRejectPairAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
