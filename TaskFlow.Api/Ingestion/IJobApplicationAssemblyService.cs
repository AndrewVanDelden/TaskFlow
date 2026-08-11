namespace TaskFlow.Api.Ingestion;

using TaskFlow.Api.Common;
using TaskFlow.Api.DTOs;

/// <summary>
/// Turns one approved job-posting draft into a JobApplication with two Todo sibling tasks (resume
/// + cover letter), both sharing ApplicationId and both linked to the session's ResumeContext.
/// Deliberately NOT an extension of IDraftCommitService: that service maps an arbitrary flat list of
/// drafts 1:1 onto generic tasks with no aggregate concept, which is a structurally different shape
/// from "exactly one JobApplication plus two fixed-kind siblings" (see Sprint 2 doc fork-point
/// decision). DraftCommitService is untouched by this class.
///
/// Returns JobApplicationResponseDto rather than the JobApplication entity: EF Core's relationship
/// fixup makes each sibling TaskItem.Application point back at the same JobApplication instance, and
/// with no reference-cycle handling configured, serializing the raw entity throws (confirmed via a
/// real HTTP-level test) — the same reason TaskService returns TaskResponseDto, not TaskItem.
/// </summary>
public interface IJobApplicationAssemblyService
{
    Task<Result<JobApplicationResponseDto>> AssembleAsync(string ingestionSessionId, int ownerId, TaskDraft posting, CancellationToken ct = default);
}
