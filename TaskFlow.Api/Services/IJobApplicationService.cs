using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;

namespace TaskFlow.Api.Services;

/// <summary>
/// Sprint 4R: pair-level (JobApplication) approve/reject — the human review gate for a resume +
/// cover-letter pair, mirroring ITaskService's single-task ApproveAsync/RejectAsync convention one
/// level up.
/// </summary>
public interface IJobApplicationService
{
    /// <summary>Human sign-off: moves a ReviewReady JobApplication's siblings to Done and the application to Approved.</summary>
    Task<Result<JobApplicationResponseDto>> ApproveAsync(int applicationId, int callerId, CancellationToken ct = default);

    /// <summary>Human rejection: sends a ReviewReady JobApplication's siblings back to Todo and the application to Building.</summary>
    Task<Result<JobApplicationResponseDto>> RejectAsync(int applicationId, int callerId, string reason, CancellationToken ct = default);
}
