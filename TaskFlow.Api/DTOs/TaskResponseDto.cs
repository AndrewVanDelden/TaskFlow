using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs;

public class TaskResponseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? AssignedToId { get; set; }
    public string? AssignedToName { get; set; }

    // Epic 3, Sprint 4R: lets the frontend's ApplicationReviewCard identify which sibling
    // (resume vs. cover letter) a task is and render its tailored output from the same
    // GET /api/Tasks payload the board already fetches, without a second endpoint.
    public string Kind { get; set; } = string.Empty;
    public int? ApplicationId { get; set; }
    public string? TailoredContent { get; set; }

    // Sprint 5 review finding: a lone Epic-3 sibling task can reach Done via the individual
    // per-task approve path while its own JobApplication never reaches Approved (that only happens
    // when both siblings are approved together). The frontend's export-download gating needs this
    // real state - Status == "Done" alone does not imply the application is Approved.
    public string? ApplicationState { get; set; }

    // Epic 3.1, U3.1: lets the frontend's Board cards show which company a resume/cover-letter
    // sibling task belongs to, sourced from the parent JobApplication the same way ApplicationState is.
    public string? Company { get; set; }

    // Static factory method — converts a TaskItem entity into this DTO
    public static TaskResponseDto FromEntity(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Status = task.Status.ToString(),
        Priority = task.Priority.ToString(),
        DueDate = task.DueDate,
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt,
        AssignedToId = task.AssignedToId,
        AssignedToName = task.AssignedTo?.Name,
        Kind = task.Kind.ToString(),
        ApplicationId = task.ApplicationId,
        TailoredContent = task.TailoredContent,
        ApplicationState = task.Application?.State.ToString(),
        Company = task.Application?.Company
    };
}