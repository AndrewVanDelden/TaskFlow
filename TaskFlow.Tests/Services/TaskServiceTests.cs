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

    private TaskService CreateSut() => new(_tasks.Object, _users.Object, _notifier.Object);

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

        var result = await CreateSut().UpdateAsync(5, new UpdateTaskDto { Title = "x" });

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_fails_validation_when_new_assignee_missing()
    {
        SetupGetById(SampleTask());
        _users.Setup(u => u.ExistsAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateSut().UpdateAsync(1, new UpdateTaskDto { Title = "x", AssignedToId = 77 });

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
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("New title");
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Review));
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateStatus ──────────────────────────────────────────────────────────
    [Fact]
    public async Task UpdateStatus_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().UpdateStatusAsync(9, new UpdateTaskStatusDto { Status = WorkflowStatus.Done });

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

        var result = await CreateSut().UpdateStatusAsync(1, new UpdateTaskStatusDto { Status = WorkflowStatus.Done });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(WorkflowStatus.Done));
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_broadcasts_the_move_so_boards_update_live()
    {
        SetupGetById(SampleTask());

        var result = await CreateSut().UpdateStatusAsync(1, new UpdateTaskStatusDto { Status = WorkflowStatus.InProgress });

        result.IsSuccess.Should().BeTrue();
        _notifier.Verify(
            n => n.TaskMovedAsync(1, WorkflowStatus.InProgress, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Approve (Review -> Done, human only) ──────────────────────────────────
    [Fact]
    public async Task Approve_moves_a_Review_task_to_Done_and_broadcasts()
    {
        var task = SampleTask();
        task.Status = WorkflowStatus.Review;
        SetupGetById(task);

        var result = await CreateSut().ApproveAsync(1);

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

        var result = await CreateSut().ApproveAsync(1);

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

        var result = await CreateSut().ApproveAsync(9);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ── GetById ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetById_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().GetByIdAsync(3);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetById_returns_the_task()
    {
        SetupGetById(SampleTask(7));

        var result = await CreateSut().GetByIdAsync(7);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(7);
    }

    // ── GetAll ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetAll_rejects_an_invalid_status_string()
    {
        var result = await CreateSut().GetAllAsync("Nonsense", null);

        result.Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public async Task GetAll_rejects_an_invalid_priority_string()
    {
        var result = await CreateSut().GetAllAsync(null, "Ultra");

        result.Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public async Task GetAll_returns_the_mapped_list()
    {
        _tasks.Setup(t => t.GetAllAsync(It.IsAny<WorkflowStatus?>(), It.IsAny<TaskPriority?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<TaskItem> { SampleTask(1), SampleTask(2) });

        var result = await CreateSut().GetAllAsync(null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Delete_returns_NotFound_when_task_missing()
    {
        SetupGetById(null);

        var result = await CreateSut().DeleteAsync(4);

        result.Status.Should().Be(ResultStatus.NotFound);
        _tasks.Verify(t => t.Remove(It.IsAny<TaskItem>()), Times.Never);
    }

    [Fact]
    public async Task Delete_removes_the_task_and_saves()
    {
        var task = SampleTask();
        SetupGetById(task);

        var result = await CreateSut().DeleteAsync(1);

        result.IsSuccess.Should().BeTrue();
        _tasks.Verify(t => t.Remove(task), Times.Once);
        _tasks.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
