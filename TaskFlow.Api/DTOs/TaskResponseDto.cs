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
        AssignedToName = task.AssignedTo?.Name
    };
}