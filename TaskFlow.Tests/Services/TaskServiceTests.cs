using FluentAssertions;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

public class TaskServiceTests
{
    private readonly Mock<ITaskRepository> _tasks = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAgentNotifier> _notifier = new();
    private readonly Mock<IAgentLogRepository> _logs = new();

    private TaskService CreateSut() => new(_tasks.Object, _users.Object, _notifier.Object, _logs.Object);

    private static TaskItem SampleTask(int id = 1) => new()
    {
        Id = id,
        Title = "Sample",
        Status = WorkflowStatus.Todo,
        Priority = TaskPriority.Medium,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // Convenience: make the repository return a given task (or null) for any GetByIdAsync.
    private void SetupGetById(TaskItem? task) =>
        _tasks.Setup(t => t.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(task);

    // T5.0: an Epic 3 sibling task (ApplicationId set) owned by someone other than the caller.
    // Status defaults to Review so Approve/Reject's own status guard would otherwise let the call
    // through - proving the NotFound comes from the ownership check, not the status check.
    private static TaskItem Epic3TaskOwnedBy(int ownerId, int id = 1) => new()
    {
        Id = id,
        Title = "Sample",
        Status = WorkflowStatus.Review,
        Priority = TaskPriority.Medium,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        ApplicationId = 5,
        Application = new JobApplication { Id = 5, OwnerId = ownerId }
    };

    // ── Create ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Create_fails_validation_when_assignee_does_not_exist()
    {
        _users.Setup(u => u.ExistsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().CreateAsync(new CreateTaskDto { Title = "x", AssignedToId = 99 });

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_succeeds_and_defaults_status_to_Todo()
    {
        _users.Setup(u => u.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().CreateAsync(new CreateTaskDto { Title = "x", AssignedToId = 1 });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Todo));
        _tasks.Verify(t => t.AddAsync(It.IsAny<TaskItem>(), It.IsAny<CancellationToken>()), Times.Once);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Update ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Update_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().UpdateAsync(5, new UpdateTaskDto { Title = "x" }, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_fails_validation_when_new_assignee_missing()
    {
        SetupGetById(SampleTask());
        _users.Setup(u => u.ExistsAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().UpdateAsync(1, new UpdateTaskDto { Title = "x", AssignedToId = 77 }, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_applies_changes_and_saves()
    {
        SetupGetById(SampleTask());

        var result = await CreateSut().UpdateAsync(1, new UpdateTaskDto
        {
            Title = "New title",
            Status = WorkflowStatus.Review,
            Priority = TaskPriority.High
        }, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("New title");
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Review));
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // T5.0: fresh architecture review (Sprint 5) found the round-1 PR #45 ownership-scoping fix only
    // covered GET /api/Tasks (the list). The same six single-item actions never checked ownership at
    // all - any authenticated user could read or mutate another user's Epic 3 sibling task by id.
    [Fact]
    public async Task Update_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        SetupGetById(Epic3TaskOwnedBy(ownerId: 99));

        var result = await CreateSut().UpdateAsync(1, new UpdateTaskDto { Title = "Hacked" }, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── UpdateStatus ──────────────────────────────────────────────────────────
    [Fact]
    public async Task UpdateStatus_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().UpdateStatusAsync(9, new UpdateTaskStatusDto { Status = WorkflowStatus.Done }, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        // No move happened, so nothing is broadcast.
        _notifier.Verify(
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateStatus_moves_the_task_and_saves()
    {
        SetupGetById(SampleTask());

        var result = await CreateSut().UpdateStatusAsync(1, new UpdateTaskStatusDto { Status = WorkflowStatus.Done }, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Done));
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_broadcasts_the_move_so_boards_update_live()
    {
        SetupGetById(SampleTask());

        var result = await CreateSut().UpdateStatusAsync(1, new UpdateTaskStatusDto { Status = WorkflowStatus.InProgress }, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        _notifier.Verify(
            n => n.TaskMovedAsync(1, WorkflowStatus.InProgress, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        SetupGetById(Epic3TaskOwnedBy(ownerId: 99));

        var result = await CreateSut().UpdateStatusAsync(1, new UpdateTaskStatusDto { Status = WorkflowStatus.Done }, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Approve (Review -> Done, human only) ──────────────────────────────────
    [Fact]
    public async Task Approve_moves_a_Review_task_to_Done_and_broadcasts()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Review;
        SetupGetById(task);

        var result = await CreateSut().ApproveAsync(1, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Done));
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TaskMovedAsync(1, WorkflowStatus.Done, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_rejects_a_task_that_is_not_in_Review()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Todo;
        SetupGetById(task);

        var result = await CreateSut().ApproveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().ApproveAsync(9, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Approve_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        // Status is Review (see Epic3TaskOwnedBy), so an unguarded call would otherwise succeed -
        // this proves the block is the ownership check, not the status guard above.
        SetupGetById(Epic3TaskOwnedBy(ownerId: 99));

        var result = await CreateSut().ApproveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Reject (Review -> Todo with a reason) ─────────────────────────────────
    [Fact]
    public async Task Reject_sends_a_Review_task_back_to_Todo_with_the_reason()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Review;
        task.ClaimedBy = "GenericExecutor";
        SetupGetById(task);

        var result = await CreateSut().RejectAsync(1, "Needs a better haiku.", callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Todo));
        task.ClaimedBy.Should().BeNull();   // claim dropped so it can be re-picked
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _logs.Verify(l => l.AddAsync(
            It.Is<AgentLog>(a => a.Action == "Rejected" && a.TaskId == 1 && a.Details == "Needs a better haiku."),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.TaskMovedAsync(1, WorkflowStatus.Todo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_rejects_a_task_that_is_not_in_Review()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Todo;
        SetupGetById(task);

        var result = await CreateSut().RejectAsync(1, "reason", callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reject_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().RejectAsync(9, "reason", callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Reject_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        SetupGetById(Epic3TaskOwnedBy(ownerId: 99));

        var result = await CreateSut().RejectAsync(1, "reason", callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _logs.Verify(l => l.AddAsync(It.IsAny<AgentLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── GetById ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetById_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().GetByIdAsync(3, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetById_returns_the_task()
    {
        SetupGetById(SampleTask(7));

        var result = await CreateSut().GetByIdAsync(7, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(7);
    }

    [Fact]
    public async Task GetById_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        // The IDOR this task exists to close: GET /api/Tasks/{id} was leaking TailoredContent
        // (a personal résumé/cover letter) to any authenticated user who knew or guessed the id.
        SetupGetById(Epic3TaskOwnedBy(ownerId: 99, id: 1));

        var result = await CreateSut().GetByIdAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetAll_rejects_an_invalid_status_string()
    {
        var result = await CreateSut().GetAllAsync("Nonsense", null, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public async Task GetAll_rejects_an_invalid_priority_string()
    {
        var result = await CreateSut().GetAllAsync(null, "Ultra", callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public async Task GetAll_returns_the_mapped_list()
    {
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem> { SampleTask(1), SampleTask(2) });

        var result = await CreateSut().GetAllAsync(null, null, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // PR #45 review finding: GetAllAsync must forward the caller's id to the repository so Epic 3
    // sibling tasks can be scoped by owner there - the service is a thin pass-through for this
    // parameter, not where the filtering logic lives (that's proven at the repository layer, see
    // TaskRepositoryTests.GetAllAsync_hides_another_owners_Epic3_sibling_task...).
    [Fact]
    public async Task GetAll_forwards_the_callerId_to_the_repository()
    {
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), 7, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem>());

        var result = await CreateSut().GetAllAsync(null, null, callerId: 7);

        result.IsSuccess.Should().BeTrue();
        _tasks.Verify(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Sprint 4R, Task 1: TaskResponseDto gains Kind, ApplicationId, TailoredContent so a second
    // team's frontend (ApplicationReviewCard) can render an Epic 3 sibling task's tailored output
    // from the existing GET /api/Tasks payload without a second endpoint.
    [Fact]
    public async Task GetAll_maps_Kind_ApplicationId_and_TailoredContent_for_an_Epic3_sibling_task()
    {
        var task = SampleTask(1);
        task.Kind = TaskKind.ResumeTailoring;
        task.ApplicationId = 5;
        task.TailoredContent = "Tailored resume markdown.";
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem> { task });

        var result = await CreateSut().GetAllAsync(null, null, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Single();
        dto.Kind.Should().Be(nameof(TaskKind.ResumeTailoring));
        dto.ApplicationId.Should().Be(5);
        dto.TailoredContent.Should().Be("Tailored resume markdown.");
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Delete_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().DeleteAsync(4, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.Remove(It.IsAny<TaskItem>()), Times.Never);
    }

    [Fact]
    public async Task Delete_removes_the_task_and_saves()
    {
        var task = SampleTask();
        SetupGetById(task);

        var result = await CreateSut().DeleteAsync(1, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        _tasks.Verify(t => t.Remove(task), Times.Once);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        SetupGetById(Epic3TaskOwnedBy(ownerId: 99));

        var result = await CreateSut().DeleteAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.Remove(It.IsAny<TaskItem>()), Times.Never);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
