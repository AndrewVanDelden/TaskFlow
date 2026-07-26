using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Services;

/// <summary>
/// Business rules for tasks. Depends on the repositories (not EF directly) and returns
/// a transport-agnostic <see cref="Result{T}"/> so it never references HTTP concepts.
/// </summary>
public class TaskService : ITaskService
{
    private readonly ITaskRepository _tasks;
    private readonly IUserRepository _users;

    public TaskService(ITaskRepository tasks, IUserRepository users)
    {
        _tasks = tasks;
        _users = users;
    }

    public async Task<Result<TaskResponseDto>> CreateAsync(CreateTaskDto dto, CancellationToken ct = default)
    {
        if (dto.AssignedToId.HasValue && !await _users.ExistsAsync(dto.AssignedToId.Value, ct))
            return Result<TaskResponseDto>.Invalid($"User {dto.AssignedToId} does not exist.");

        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            DueDate = dto.DueDate,
            AssignedToId = dto.AssignedToId,
            Status = WorkflowStatus.Todo,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _tasks.AddAsync(task, ct);
        await _tasks.SaveChangesAsync(ct);

        return Result<TaskResponseDto>.Ok(TaskResponseDto.FromEntity(task));
    }

    public async Task<Result<TaskResponseDto>> UpdateAsync(int id, UpdateTaskDto dto, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(id, includeAssignee: true, ct);
        if (task is null)
            return Result<TaskResponseDto>.NotFound($"Task {id} not found.");

        if (dto.AssignedToId.HasValue && !await _users.ExistsAsync(dto.AssignedToId.Value, ct))
            return Result<TaskResponseDto>.Invalid($"User {dto.AssignedToId} does not exist.");

        task.Title = dto.Title;
        task.Description = dto.Description;
        task.Status = dto.Status;
        task.Priority = dto.Priority;
        task.DueDate = dto.DueDate;
        task.AssignedToId = dto.AssignedToId;
        task.UpdatedAt = DateTime.UtcNow;

        await _tasks.SaveChangesAsync(ct);

        var updated = await _tasks.GetByIdAsync(id, includeAssignee: true, ct);
        return Result<TaskResponseDto>.Ok(TaskResponseDto.FromEntity(updated!));
    }

    public async Task<Result<TaskResponseDto>> UpdateStatusAsync(int id, UpdateTaskStatusDto dto, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(id, includeAssignee: true, ct);
        if (task is null)
            return Result<TaskResponseDto>.NotFound($"Task {id} not found.");

        task.Status = dto.Status;
        task.UpdatedAt = DateTime.UtcNow;

        await _tasks.SaveChangesAsync(ct);
        return Result<TaskResponseDto>.Ok(TaskResponseDto.FromEntity(task));
    }

    public async Task<Result<TaskResponseDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(id, includeAssignee: true, ct);
        return task is null
            ? Result<TaskResponseDto>.NotFound($"Task {id} not found.")
            : Result<TaskResponseDto>.Ok(TaskResponseDto.FromEntity(task));
    }

    public async Task<Result<IReadOnlyList<TaskResponseDto>>> GetAllAsync(string? status, string? priority, CancellationToken ct = default)
    {
        WorkflowStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<WorkflowStatus>(status, ignoreCase: true, out var s))
                return Result<IReadOnlyList<TaskResponseDto>>.Invalid(
                    $"Invalid status '{status}'. Valid values: {string.Join(", ", Enum.GetNames<WorkflowStatus>())}.");
            parsedStatus = s;
        }

        TaskPriority? parsedPriority = null;
        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out var p))
                return Result<IReadOnlyList<TaskResponseDto>>.Invalid(
                    $"Invalid priority '{priority}'. Valid values: {string.Join(", ", Enum.GetNames<TaskPriority>())}.");
            parsedPriority = p;
        }

        var tasks = await _tasks.GetAllAsync(parsedStatus, parsedPriority, ct);
        IReadOnlyList<TaskResponseDto> dtos = tasks.Select(TaskResponseDto.FromEntity).ToList();
        return Result<IReadOnlyList<TaskResponseDto>>.Ok(dtos);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var task = await _tasks.GetByIdAsync(id, ct: ct);
        if (task is null)
            return Result<bool>.NotFound($"Task {id} not found.");

        _tasks.Remove(task);
        await _tasks.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }
}
