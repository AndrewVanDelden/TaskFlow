using TaskFlow.Api.Ingestion;

namespace TaskFlow.Api.DTOs;

/// <summary>
/// Approve-and-commit request: the drafts the user accepted from the preview, plus the source
/// name for provenance. Reuses the <see cref="TaskDraft"/> shape the parse endpoint returned.
/// </summary>
public class CommitDraftsDto
{
    public string? SourceName { get; set; }
    public List<TaskDraft> Drafts { get; set; } = new();
}
