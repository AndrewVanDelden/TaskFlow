using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Marker interface for the job-posting ingestion seam. Identical in shape to
/// <see cref="IIngestionParser"/>; it exists purely so a controller can depend on "the
/// job-posting parser" specifically without colliding with the app's default
/// <see cref="IIngestionParser"/> DI registration, which serves a different, already-shipped
/// generic-document ingestion flow.
/// </summary>
public interface IJobPostingIngestionParser : IIngestionParser
{
}
