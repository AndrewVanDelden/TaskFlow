using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Api.Services;

/// <summary>Business operations for tasks. Returns Result&lt;T&gt; so it stays HTTP-agnostic.</summary>
public interface ITaskService
{
    Task<Result<TaskResponseDto>> CreateAsync(CreateTaskDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> UpdateAsync(int id, UpdateTaskDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> UpdateStatusAsync(int id, UpdateTaskStatusDto dto, CancellationToken ct = default);

    /// <summary>Human sign-off: moves a task from <c>Review</c> to <c>Done</c>. Only valid from Review.</summary>
    Task<Result<TaskResponseDto>> ApproveAsync(int id, CancellationToken ct = default);

    /// <summary>Human rejection: sends a <c>Review</c> task back to <c>Todo</c> with a reason. Only valid from Review.</summary>
    Task<Result<TaskResponseDto>> RejectAsync(int id, string reason, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TaskResponseDto>>> GetAllAsync(string? status, string? priority, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}