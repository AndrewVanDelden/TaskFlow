using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Models;

public class JobApplication
{
    public int Id { get; set; }

    public ApplicationState State { get; set; } = ApplicationState.Building;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Links this application back to the ResumeContext its sibling tasks read (Sprint 3R): a later
    // agent resolves task.ApplicationId -> this pair -> ResumeContextRepository.GetForOwnerAsync,
    // reusing the same ownership-scoped lookup a controller would use, without needing an HTTP
    // request of its own. Stamped once, at assembly time, from the authenticated caller.
    [MaxLength(200)]
    public string IngestionSessionId { get; set; } = string.Empty;

    public int OwnerId { get; set; }

    public const int CompanyMaxLength = 200;

    // Extracted by both job-posting parsers (Epic 3.1, U3.1). Optional: the free rule-based
    // parser may legitimately fail to find a company heading, and a hand-created application has
    // none at all.
    [MaxLength(CompanyMaxLength)]
    public string? Company { get; set; }

    // Navigation property — one JobApplication has many Tasks (the resume + cover-letter siblings)
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}
