using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Models;

public class AgentLog
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string AgentName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;   // e.g. "PriorityUpdated", "NoChangesNeeded"

    [MaxLength(1000)]
    public string? Details { get; set; }                 // Human-readable summary of what happened

    public bool Success { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}