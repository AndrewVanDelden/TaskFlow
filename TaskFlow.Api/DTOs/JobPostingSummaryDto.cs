using System.ComponentModel.DataAnnotations;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.DTOs;

/// <summary>
/// The parsed job-posting fields needed to assemble a JobApplication. Deliberately narrower than
/// TaskDraft - no Kind - because the caller never chooses which task kinds get created:
/// JobApplicationAssemblyService always creates exactly one ResumeTailoring and one
/// CoverLetterTailoring sibling, so accepting a client-supplied Kind here would silently be
/// ignored rather than doing anything.
///
/// MaxLength caps mirror TaskItem's own persistence limits (PR #40 review, round 2 - both manual
/// and Copilot's automated review independently found these were missing): without them, an
/// oversized value bypasses model validation entirely and only gets caught, if at all, wherever
/// the value is ultimately persisted.
/// </summary>
public class JobPostingSummaryDto
{
    [Required]
    [MaxLength(TaskItem.TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(TaskItem.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Required(AllowEmptyStrings: true) - Section is typed non-nullable, but without this, a
    // client sending an explicit JSON null passes model validation anyway and Section ends up
    // actually null at runtime (PR #40 review, round 4). Empty string is still fine; null isn't.
    [Required(AllowEmptyStrings = true)]
    [MaxLength(TaskItem.SourceSectionMaxLength)]
    public string Section { get; set; } = string.Empty;

    // Optional - the free rule-based parser may legitimately fail to find a company heading.
    [MaxLength(JobApplication.CompanyMaxLength)]
    public string? Company { get; set; }
}
