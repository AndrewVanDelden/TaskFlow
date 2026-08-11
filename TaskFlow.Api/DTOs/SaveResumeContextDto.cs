using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs;

public class SaveResumeContextDto
{
    [Required]
    [MaxLength(200)]
    public string IngestionSessionId { get; set; } = string.Empty;

    [Required]
    [MaxLength(20000)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ContentFormat { get; set; }
}
