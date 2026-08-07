namespace TaskFlow.Api.Models;

/// <summary>
/// Discriminator that lets different executor agents self-select which tasks they work.
/// New applications (epics) add their own kinds; the shared core never changes.
/// </summary>
public enum TaskKind
{
    Generic,
    ResumeTailoring,
    CoverLetterTailoring
}
