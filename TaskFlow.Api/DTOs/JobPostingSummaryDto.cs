using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs;

/// <summary>
/// The parsed job-posting fields needed to assemble a JobApplication. Deliberately narrower than
/// TaskDraft - no Kind - because the caller never chooses which task kinds get created:
/// JobApplicationAssemblyService always creates exactly one ResumeTailoring and one
/// CoverLetterTailoring sibling, so accepting a client-supplied Kind here would silently be
/// ignored rather than doing anything.
/// </summary>
public class JobPostingSummaryDto
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Section { get; set; } = string.Empty;
}
