namespace TaskFlow.Api.DTOs;

public class ResumeContextSummaryDto
{
    public string Content { get; set; } = string.Empty;
    public string ContentFormat { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
