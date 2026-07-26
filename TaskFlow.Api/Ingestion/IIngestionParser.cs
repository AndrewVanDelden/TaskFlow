using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Turns raw document text into task drafts. One implementation per input type: rules-based
/// now, and a Claude-assisted parser can be added behind this same seam without touching
/// anything downstream (Dependency Inversion).
/// </summary>
public interface IIngestionParser
{
    Result<IReadOnlyList<TaskDraft>> Parse(string documentText);
}
