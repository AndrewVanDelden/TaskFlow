using TaskFlow.Api.Common;

namespace TaskFlow.Api.Export;

/// <summary>
/// Turns an Approved JobApplication's tailored resume/cover-letter content into a downloadable
/// PDF or Markdown file. Ownership and state guards mirror JobApplicationService.ApproveAsync's
/// exact convention (see Sprint 5 "Decisions owned here" in TaskFlow_Epic3_ResumeBuilder.md):
/// missing and wrong-owner both collapse into NotFound; a non-Approved application is Invalid.
/// </summary>
public interface IExportService
{
    Task<Result<ExportedFile>> ExportResumeAsync(int applicationId, int callerId, string callerName, ExportFormat format, CancellationToken ct = default);
    Task<Result<ExportedFile>> ExportCoverLetterAsync(int applicationId, int callerId, string callerName, ExportFormat format, CancellationToken ct = default);
}
