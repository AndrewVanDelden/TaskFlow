using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Ingestion;

/// <summary>EF-backed implementation of <see cref="IJobApplicationAssemblyService"/>.</summary>
public sealed class JobApplicationAssemblyService : IJobApplicationAssemblyService
{
    private readonly IJobApplicationRepository _jobApplications;
    private readonly IResumeContextRepository _resumeContexts;

    public JobApplicationAssemblyService(IJobApplicationRepository jobApplications, IResumeContextRepository resumeContexts)
    {
        _jobApplications = jobApplications;
        _resumeContexts = resumeContexts;
    }

    public async Task<Result<JobApplicationResponseDto>> AssembleAsync(string ingestionSessionId, int ownerId, TaskDraft posting, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ingestionSessionId))
        {
            return Result<JobApplicationResponseDto>.Invalid("Ingestion session id must not be null, empty, or whitespace-only.");
        }

        if (string.IsNullOrWhiteSpace(posting.Title))
        {
            return Result<JobApplicationResponseDto>.Invalid("Posting title must not be null, empty, or whitespace-only.");
        }

        var resumeContext = await _resumeContexts.GetForOwnerAsync(ingestionSessionId, ownerId, ct);
        if (resumeContext is null)
        {
            return Result<JobApplicationResponseDto>.NotFound(
                "No base resume found for this session. Save a base resume before assembling the application.");
        }

        var application = new JobApplication
        {
            State = ApplicationState.Building,
            IngestionSessionId = ingestionSessionId,
            OwnerId = ownerId,
            Tasks = new List<TaskItem>
            {
                new TaskItem
                {
                    Title = posting.Title,
                    Description = posting.Description,
                    Kind = TaskKind.ResumeTailoring,
                    Status = WorkflowStatus.Todo,
                    SourceSection = posting.Section
                },
                new TaskItem
                {
                    Title = $"Cover letter — {posting.Title}",
                    Description = posting.Description,
                    Kind = TaskKind.CoverLetterTailoring,
                    Status = WorkflowStatus.Todo,
                    SourceSection = posting.Section
                }
            }
        };

        await _jobApplications.AddAsync(application, ct);
        await _jobApplications.SaveChangesAsync(ct);

        return Result<JobApplicationResponseDto>.Ok(JobApplicationResponseDto.FromEntity(application));
    }
}
