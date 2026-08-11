using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Common;

/// <summary>
/// Shared caller-identity helpers for controllers. Extracted from
/// <c>JobApplicationsController</c> (Sprint 4R review: PR #45 added a second controller,
/// <c>TasksController</c>, that needs the exact same claim-resolution logic — duplicating it a
/// second time is exactly the DRY violation this project's own review history has already flagged
/// once, so it's extracted here instead of copied again).
/// </summary>
public static class ControllerBaseExtensions
{
    /// <summary>
    /// Resolves the caller's user id from the JWT's NameIdentifier claim. A missing or
    /// non-numeric claim (misconfigured auth, a token from a different issuer) must not throw -
    /// <c>[Authorize]</c> only proves a valid token was presented, not that its claims are shaped
    /// the way the caller expects.
    /// </summary>
    public static bool TryGetCurrentUserId(this ControllerBase controller, out int userId) =>
        int.TryParse(controller.User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    public static UnauthorizedObjectResult UnauthenticatedIdentity(this ControllerBase controller) =>
        controller.Unauthorized(new { message = "The request's identity claim is missing or invalid." });
}
