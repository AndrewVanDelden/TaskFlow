using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs;

public class SaveResumeContextDto
{
    [Required]
    [MaxLength(200)]
    public string IngestionSessionId { get; set; } = string.Empty;

    [Required]
    [MaxLength(TaskItem.TailoredContentMaxLength)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ContentFormat { get; set; }
}
