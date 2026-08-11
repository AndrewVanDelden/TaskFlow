using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TaskFlow.Api.Models;

public class TaskItem
{
    // Shared with the DTOs that feed these fields (e.g. JobPostingSummaryDto) so the API
    // boundary's validation caps and this entity's persistence caps can't drift apart.
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int SourceSectionMaxLength = 200;

    public int Id { get; set; }

    [Required]
    [MaxLength(TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Todo;

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Foreign key — nullable so tasks can exist without an assigned user
    public int? AssignedToId { get; set; }

    [ForeignKey(nameof(AssignedToId))]
    public User? AssignedTo { get; set; }

    // Which executor works this task. Defaults to Generic; ingestion stamps it.
    public TaskKind Kind { get; set; } = TaskKind.Generic;

    // Provenance for agent/ingested tasks (null for hand-created ones): which document and
    // which section within it the task came from. No Document entity exists, so these are strings.
    [MaxLength(200)]
    public string? SourceName { get; set; }

    [MaxLength(SourceSectionMaxLength)]
    public string? SourceSection { get; set; }

    // The agent currently working this task; null when unclaimed. Stamped atomically at claim time.
    [MaxLength(200)]
    public string? ClaimedBy { get; set; }

    // Foreign key — nullable so ordinary tasks can exist without a JobApplication parent. Only
    // resume/cover-letter sibling tasks (Epic 3) set this.
    public int? ApplicationId { get; set; }

    [ForeignKey(nameof(ApplicationId))]
    public JobApplication? Application { get; set; }

    // Generated/edited resume or cover-letter body text for Epic 3 tasks. Large free text, so a
    // generous cap rather than the short MaxLength used for other string fields on this entity.
    [MaxLength(20000)]
    public string? TailoredContent { get; set; }
}
