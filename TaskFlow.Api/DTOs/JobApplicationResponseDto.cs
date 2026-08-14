using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs;

/// <summary>
/// Response shape for an assembled JobApplication. A raw JobApplication entity cannot be returned
/// directly: EF Core's relationship fixup sets each sibling TaskItem's Application navigation back
/// to this same JobApplication instance, and with no reference-cycle handling configured in
/// Program.cs, System.Text.Json throws on that cycle (confirmed via a real HTTP-level integration
/// test, not assumed). This mirrors why TaskResponseDto exists instead of returning TaskItem
/// directly from TaskService.
/// </summary>
public class JobApplicationResponseDto
{
    public int Id { get; set; }
    public ApplicationState State { get; set; }
    public string IngestionSessionId { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Company { get; set; }
    public List<JobApplicationTaskDto> Tasks { get; set; } = new();

    public static JobApplicationResponseDto FromEntity(JobApplication application) => new()
    {
        Id = application.Id,
        State = application.State,
        IngestionSessionId = application.IngestionSessionId,
        OwnerId = application.OwnerId,
        CreatedAt = application.CreatedAt,
        Company = application.Company,
        Tasks = application.Tasks.Select(JobApplicationTaskDto.FromEntity).ToList()
    };

    /// <summary>
    /// Sprint 4R: builds this DTO with sibling tasks supplied explicitly, for callers (the
    /// approve/reject flow) that fetched the JobApplication via
    /// <see cref="TaskFlow.Api.Repositories.IJobApplicationRepository.GetByIdAsync"/> — which does
    /// not eager-load <see cref="JobApplication.Tasks"/> — and separately fetched the siblings via
    /// <see cref="TaskFlow.Api.Repositories.ITaskRepository.GetByApplicationIdAsync"/>.
    /// </summary>
    public static JobApplicationResponseDto FromEntity(JobApplication application, IEnumerable<TaskItem> tasks) => new()
    {
        Id = application.Id,
        State = application.State,
        IngestionSessionId = application.IngestionSessionId,
        OwnerId = application.OwnerId,
        CreatedAt = application.CreatedAt,
        Company = application.Company,
        Tasks = tasks.Select(JobApplicationTaskDto.FromEntity).ToList()
    };
}

/// <summary>
/// Minimal sibling-task shape for a JobApplication response — enough to identify which of the two
/// siblings (resume vs. cover letter) each entry is, without pulling in TaskItem's navigation
/// properties (Application, AssignedTo) that would reintroduce the same cycle.
/// </summary>
public class JobApplicationTaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public TaskKind Kind { get; set; }
    public WorkflowStatus Status { get; set; }

    public static JobApplicationTaskDto FromEntity(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Kind = task.Kind,
        Status = task.Status
    };
}
