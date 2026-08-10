using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Ingestion;

namespace TaskFlow.Api.DTOs;

public class AssembleJobApplicationDto
{
    [Required]
    [MaxLength(200)]
    public string IngestionSessionId { get; set; } = string.Empty;

    [Required]
    public TaskDraft Posting { get; set; } = null!;
}
