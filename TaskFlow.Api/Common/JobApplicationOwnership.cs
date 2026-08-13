using System.Diagnostics.CodeAnalysis;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Common;

/// <summary>
/// IDOR-safe ownership check shared by every service that scopes a JobApplication to its owner: a
/// missing application and one that exists but belongs to someone else must be indistinguishable
/// to the caller (both become the same NotFound), so both conditions are tested together. Extracted
/// from JobApplicationService and ResumeContextService, which had this exact check duplicated three
/// times (Epic 3 Pre-Merge Code Review, finding 3.2) — the same duplication pattern
/// TaskService.IsOwnedByAnotherUser was extracted to fix for tasks.
/// </summary>
public static class JobApplicationOwnership
{
    public static bool IsMissingOrOwnedByAnotherUser(
        [NotNullWhen(false)] this JobApplication? application, int callerId) =>
        application is null || application.OwnerId != callerId;
}
