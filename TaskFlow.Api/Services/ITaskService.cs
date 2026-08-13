using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Api.Services;

/// <summary>Business operations for tasks. Returns Result&lt;T&gt; so it stays HTTP-agnostic.</summary>
public interface ITaskService
{
    Task<Result<TaskResponseDto>> CreateAsync(CreateTaskDto dto, CancellationToken ct = default);

    /// <summary>Generic tasks (no ApplicationId) are updatable by any caller; an Epic 3 sibling task
    /// is updatable only by <paramref name="callerId"/> if it owns the parent JobApplication (T5.0)
    /// — a non-owner gets the same NotFound as a missing id (see
    /// <see cref="TaskFlow.Api.Repositories.ITaskRepository.GetAllAsync"/> for the matching list-level
    /// scoping this mirrors).</summary>
    Task<Result<TaskResponseDto>> UpdateAsync(int id, UpdateTaskDto dto, int callerId, CancellationToken ct = default);

    /// <summary>See <see cref="UpdateAsync"/> — same ownership scoping (T5.0).</summary>
    Task<Result<TaskResponseDto>> UpdateStatusAsync(int id, UpdateTaskStatusDto dto, int callerId, CancellationToken ct = default);

    /// <summary>Human sign-off: moves a task from <c>Review</c> to <c>Done</c>. Only valid from Review.
    /// Same ownership scoping as <see cref="UpdateAsync"/> (T5.0).</summary>
    Task<Result<TaskResponseDto>> ApproveAsync(int id, int callerId, CancellationToken ct = default);

    /// <summary>Human rejection: sends a <c>Review</c> task back to <c>Todo</c> with a reason. Only valid
    /// from Review. Same ownership scoping as <see cref="UpdateAsync"/> (T5.0).</summary>
    Task<Result<TaskResponseDto>> RejectAsync(int id, string reason, int callerId, CancellationToken ct = default);

    /// <summary>Same ownership scoping as <see cref="UpdateAsync"/> (T5.0) — this is the single-item
    /// counterpart to the PR #45 fix on <see cref="GetAllAsync"/>.</summary>
    Task<Result<TaskResponseDto>> GetByIdAsync(int id, int callerId, CancellationToken ct = default);

    /// <summary>Lists tasks visible to <paramref name="callerId"/> — generic tasks unconditionally,
    /// Epic 3 sibling tasks only when owned by the caller (see
    /// <see cref="TaskFlow.Api.Repositories.ITaskRepository.GetAllAsync"/>).</summary>
    Task<Result<IReadOnlyList<TaskResponseDto>>> GetAllAsync(string? status, string? priority, int callerId, CancellationToken ct = default);

    /// <summary>Same ownership scoping as <see cref="UpdateAsync"/> (T5.0).</summary>
    Task<Result<bool>> DeleteAsync(int id, int callerId, CancellationToken ct = default);
}