using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs;

/// <summary>
/// Ingestion request. Carries only the content; how it was obtained (file, paste, link) is the
/// caller's concern, which keeps the endpoint source-agnostic. A source name/id is provenance and
/// is added in Sprint 3, so it is intentionally not a field here yet.
/// </summary>
public class IngestDocumentDto
{
    [Required]
    public string Content { get; set; } = string.Empty;
}
