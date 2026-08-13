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
                    Title = BuildCoverLetterTitle(posting.Title),
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

    private const string CoverLetterTitlePrefix = "Cover letter — ";

    // A posting title at (or near) TaskItem.TitleMaxLength would push this derived title past the
    // column's own cap once the prefix is added (PR #40 review, round 2 - Copilot's automated
    // review caught this: capping the input alone isn't enough, since the prefix adds more on
    // top). Truncate defensively so this can never exceed the cap, regardless of the input cap.
    private static string BuildCoverLetterTitle(string jobTitle)
    {
        var title = CoverLetterTitlePrefix + jobTitle;
        return title.Length > TaskItem.TitleMaxLength ? title[..TaskItem.TitleMaxLength] : title;
    }
}
