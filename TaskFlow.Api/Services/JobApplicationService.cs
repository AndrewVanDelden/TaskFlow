using TaskFlow.Api.Agents;
using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Services;

/// <summary>EF/repository-backed implementation of <see cref="IJobApplicationService"/>.</summary>
public class JobApplicationService : IJobApplicationService
{
    private readonly IJobApplicationRepository _jobApplications;
    private readonly ITaskRepository _tasks;
    private readonly IAgentLogRepository _logs;
    private readonly IAgentNotifier _notifier;

    public JobApplicationService(
        IJobApplicationRepository jobApplications,
        ITaskRepository tasks,
        IAgentLogRepository logs,
        IAgentNotifier notifier)
    {
        _jobApplications = jobApplications;
        _tasks = tasks;
        _logs = logs;
        _notifier = notifier;
    }

    public async Task<Result<JobApplicationResponseDto>> ApproveAsync(int applicationId, int callerId, CancellationToken ct = default)
    {
        var application = await _jobApplications.GetByIdAsync(applicationId, ct);

        // Same NotFound for missing and wrong-owner: a cross-owner probe must be indistinguishable
        // from a genuine 404, matching this project's established IDOR-safe convention (see
        // ResumeContextRepository's doc comments for the same reasoning applied elsewhere).
        if (application is null || application.OwnerId != callerId)
            return Result<JobApplicationResponseDto>.NotFound($"JobApplication {applicationId} not found.");

        if (application.State != ApplicationState.ReviewReady)
            return Result<JobApplicationResponseDto>.Invalid(
                $"JobApplication {applicationId} is {application.State}; only ReviewReady can be approved.");

        var approved = await _jobApplications.TryApprovePairAsync(applicationId, callerId, ct);
        if (!approved)
            return Result<JobApplicationResponseDto>.Conflict(
                $"JobApplication {applicationId} was already approved or rejected by another action.");

        var siblings = await _tasks.GetByApplicationIdAsync(applicationId, ct);
        foreach (var sibling in siblings)
            await _notifier.TaskMovedAsync(sibling.Id, WorkflowStatus.Done, ct);

        var updated = await _jobApplications.GetByIdAsync(applicationId, ct);
        return Result<JobApplicationResponseDto>.Ok(JobApplicationResponseDto.FromEntity(updated!, siblings));
    }

    public async Task<Result<JobApplicationResponseDto>> RejectAsync(int applicationId, int callerId, string reason, CancellationToken ct = default)
    {
        var application = await _jobApplications.GetByIdAsync(applicationId, ct);

        if (application is null || application.OwnerId != callerId)
            return Result<JobApplicationResponseDto>.NotFound($"JobApplication {applicationId} not found.");

        if (application.State != ApplicationState.ReviewReady)
            return Result<JobApplicationResponseDto>.Invalid(
                $"JobApplication {applicationId} is {application.State}; only ReviewReady can be rejected.");

        var rejected = await _jobApplications.TryRejectPairAsync(applicationId, callerId, ct);
        if (!rejected)
            return Result<JobApplicationResponseDto>.Conflict(
                $"JobApplication {applicationId} was already approved or rejected by another action.");

        var siblings = await _tasks.GetByApplicationIdAsync(applicationId, ct);
        foreach (var sibling in siblings)
        {
            // Record the reviewer's reason so there is a trail in the activity feed, matching
            // TaskService.RejectAsync's existing single-task convention, applied once per sibling.
            await _logs.AddAsync(new AgentLog
            {
                AgentName = "Reviewer",
                Action = AgentActions.Rejected,
                TaskId = sibling.Id,
                Details = reason,
                Success = false,
                CreatedAt = DateTime.UtcNow
            }, ct);

            await _notifier.TaskMovedAsync(sibling.Id, WorkflowStatus.Todo, ct);
        }
        await _logs.SaveChangesAsync(ct);

        var updated = await _jobApplications.GetByIdAsync(applicationId, ct);
        return Result<JobApplicationResponseDto>.Ok(JobApplicationResponseDto.FromEntity(updated!, siblings));
    }
}
