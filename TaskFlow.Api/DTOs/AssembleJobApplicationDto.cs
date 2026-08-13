using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs;

public class AssembleJobApplicationDto
{
    [Required]
    [MaxLength(200)]
    public string IngestionSessionId { get; set; } = string.Empty;

    [Required]
    public JobPostingSummaryDto Posting { get; set; } = null!;
}
