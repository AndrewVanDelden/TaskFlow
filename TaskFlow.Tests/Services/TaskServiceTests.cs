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
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
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
            n => n.TaskMovedAsync(1, WorkflowStatus.InProgress, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Epic 3 Pre-Merge Code Review, finding 1.1: an Epic 3 sibling task's move must be scoped to
    // its owner, not broadcast to every connected client (unlike the shared generic board).
    [Fact]
    public async Task UpdateStatus_scopes_the_broadcast_to_the_applications_owner_for_an_Epic3_sibling_task()
    {
        SetupGetById(Epic3TaskOwnedBy(ownerId: 1));

        var result = await CreateSut().UpdateStatusAsync(1, new UpdateTaskStatusDto { Status = WorkflowStatus.InProgress }, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        _notifier.Verify(
            n => n.TaskMovedAsync(1, WorkflowStatus.InProgress, 1, It.IsAny<CancellationToken>()),
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
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
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
        _notifier.Verify(n => n.TaskMovedAsync(1, WorkflowStatus.Done, null, It.IsAny<CancellationToken>()), Times.Once);
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
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
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
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
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
        _notifier.Verify(n => n.TaskMovedAsync(1, WorkflowStatus.Todo, null, It.IsAny<CancellationToken>()), Times.Once);
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
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
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
            n => n.TaskMovedAsync(It.IsAny<int>(), It.IsAny<WorkflowStatus>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
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

    // Copilot review finding (PR #48): the frontend's export-download gating needs the real
    // JobApplication state, not just Status == "Done", since a lone Epic-3 sibling can reach Done
    // via the individual per-task approve path while its own application never reaches Approved.
    [Fact]
    public async Task GetById_returns_the_owning_applications_real_ApplicationState_for_an_Epic3_sibling_task()
    {
        var task = Epic3TaskOwnedBy(ownerId: 1, id: 1);
        task.Application!.State = ApplicationState.Approved;
        SetupGetById(task);

        var result = await CreateSut().GetByIdAsync(1, callerId: 1);

        result.Value!.ApplicationState.Should().Be("Approved");
    }

    [Fact]
    public async Task GetById_returns_null_ApplicationState_for_a_generic_task()
    {
        SetupGetById(SampleTask());

        var result = await CreateSut().GetByIdAsync(1, callerId: 1);

        result.Value!.ApplicationState.Should().BeNull();
    }

    // ── GetAll ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetAll_rejects_an_invalid_status_string()
    {
        var result = await CreateSut().GetAllAsync("Nonsense", null, archived: false, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public async Task GetAll_rejects_an_invalid_priority_string()
    {
        var result = await CreateSut().GetAllAsync(null, "Ultra", archived: false, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public async Task GetAll_returns_the_mapped_list()
    {
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem> { SampleTask(1), SampleTask(2) });

        var result = await CreateSut().GetAllAsync(null, null, archived: false, callerId: 1);

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
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), It.IsAny<bool>(), 7, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem>());

        var result = await CreateSut().GetAllAsync(null, null, archived: false, callerId: 7);

        result.IsSuccess.Should().BeTrue();
        _tasks.Verify(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), false, 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Board archive feature: the archived flag itself must reach the repository unmolested - proven
    // separately from callerId forwarding above, so a bug that hardcodes/ignores the flag can't hide
    // behind the other passing test.
    [Fact]
    public async Task GetAll_forwards_the_archived_flag_to_the_repository()
    {
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), true, It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem>());

        var result = await CreateSut().GetAllAsync(null, null, archived: true, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        _tasks.Verify(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), true, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
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
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem> { task });

        var result = await CreateSut().GetAllAsync(null, null, archived: false, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!.Single();
        dto.Kind.Should().Be(nameof(TaskKind.ResumeTailoring));
        dto.ApplicationId.Should().Be(5);
        dto.TailoredContent.Should().Be("Tailored resume markdown.");
    }

    // ── Archive (Done -> soft-archived, restorable via the Archive view) ───────
    [Fact]
    public async Task Archive_archives_a_Done_task_and_returns_the_updated_dto()
    {
        var doneTask = SampleTask();
        doneTask.Status = WorkflowStatus.Done;
        var archivedTask = SampleTask();
        archivedTask.Status = WorkflowStatus.Done;
        archivedTask.ArchivedAt = DateTime.UtcNow;
        // Matches UpdateAsync's re-fetch pattern: ArchiveAsync is a repository ExecuteUpdateAsync
        // bypass, so the service must not trust the pre-mutation in-memory instance it already holds.
        _tasks.SetupSequence(t => t.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(doneTask)
              .ReturnsAsync(archivedTask);
        _tasks.Setup(t => t.ArchiveAsync(1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().ArchiveAsync(1, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ArchivedAt.Should().NotBeNull();
        _tasks.Verify(t => t.ArchiveAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Archive_rejects_a_task_that_is_not_Done()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Todo;
        SetupGetById(task);

        var result = await CreateSut().ArchiveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.ArchiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Archive_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().ArchiveAsync(9, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Archive_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        // Status is Done (unlike Epic3TaskOwnedBy's Review default), so an unguarded call would
        // otherwise pass the status guard too - this proves the block is the ownership check.
        var task = Epic3TaskOwnedBy(ownerId: 99);
        task.Status = WorkflowStatus.Done;
        SetupGetById(task);

        var result = await CreateSut().ArchiveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.ArchiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // PR #59 review finding (conventions, PLAUSIBLE): UnarchiveAsync explicitly guards its mirror
    // precondition (task.ArchivedAt is null -> Invalid), but ArchiveAsync had no equivalent
    // (task.ArchivedAt is not null) check - only task.Status != Done, which stays true for an
    // already-archived task. A second archive call fell through to the repository (whose guarded
    // update is a true no-op) but the service still returned 200 with the current state, instead of
    // the symmetric 400 Unarchive gives for the mirrored case. Not a data-corruption risk, but an
    // inconsistent response code for the same class of "already in target state" call.
    [Fact]
    public async Task Archive_rejects_a_task_that_is_already_archived()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Done;
        task.ArchivedAt = DateTime.UtcNow;
        SetupGetById(task);

        var result = await CreateSut().ArchiveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.ArchiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Unarchive (restore) ──────────────────────────────────────────────────
    [Fact]
    public async Task Unarchive_restores_an_archived_task_and_returns_the_updated_dto()
    {
        var archivedTask = SampleTask();
        archivedTask.Status = WorkflowStatus.Done;
        archivedTask.ArchivedAt = DateTime.UtcNow;
        var restoredTask = SampleTask();
        restoredTask.Status = WorkflowStatus.Done;
        _tasks.SetupSequence(t => t.GetByIdAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(archivedTask)
              .ReturnsAsync(restoredTask);
        _tasks.Setup(t => t.UnarchiveAsync(1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().UnarchiveAsync(1, callerId: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ArchivedAt.Should().BeNull();
        _tasks.Verify(t => t.UnarchiveAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unarchive_rejects_a_task_that_is_not_archived()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Done;
        SetupGetById(task);

        var result = await CreateSut().UnarchiveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.Validation);
        _tasks.Verify(t => t.UnarchiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unarchive_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().UnarchiveAsync(9, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Unarchive_returns_NotFound_when_caller_is_not_the_owner_of_an_Epic3_sibling_task()
    {
        // ArchivedAt is set (unlike Epic3TaskOwnedBy's default null), so an unguarded call would
        // otherwise pass the "is archived" guard too - this proves the block is the ownership check.
        var task = Epic3TaskOwnedBy(ownerId: 99);
        task.ArchivedAt = DateTime.UtcNow;
        SetupGetById(task);

        var result = await CreateSut().UnarchiveAsync(1, callerId: 1);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.UnarchiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ArchiveAllDone (bulk "clear all Done") ──────────────────────────────
    [Fact]
    public async Task ArchiveAllDone_forwards_the_callerId_and_returns_the_repositorys_count()
    {
        _tasks.Setup(t => t.ArchiveAllDoneAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await CreateSut().ArchiveAllDoneAsync(callerId: 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
        _tasks.Verify(t => t.ArchiveAllDoneAsync(7, It.IsAny<CancellationToken>()), Times.Once);
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
