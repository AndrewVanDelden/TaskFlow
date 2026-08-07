using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.Models;

// A user's base resume, captured server-side during a future sprint's intake flow so a
// server-side Claude agent can read it later (agents have no access to browser storage).
// Scoped to the ingestion session that captured it AND the owning user, so ownership checks
// have both dimensions to compare against.
public class ResumeContext
{
    public int Id { get; set; }

    public string IngestionSessionId { get; set; } = string.Empty;

    public int OwnerId { get; set; }

    // Base resume text. Large free text, so a generous cap rather than the short MaxLength
    // used for other string fields — matches TaskItem.TailoredContent for the same reason.
    [MaxLength(20000)]
    public string Content { get; set; } = string.Empty;

    public string ContentFormat { get; set; } = "text";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
