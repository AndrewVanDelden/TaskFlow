using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Api.Services;

public interface ITaskService
{
    Task<Result<TaskResponseDto>> CreateAsync(CreateTaskDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> UpdateAsync(int id, UpdateTaskDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> UpdateStatusAsync(int id, UpdateTaskStatusDto dto, CancellationToken ct = default);
    Task<Result<TaskResponseDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TaskResponseDto>>> GetAllAsync(string? status, string? priority, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}