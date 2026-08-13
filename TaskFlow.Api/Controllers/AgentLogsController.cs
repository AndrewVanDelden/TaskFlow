using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentLogsController : ControllerBase
{
    private readonly IAgentLogRepository _logs;

    public AgentLogsController(IAgentLogRepository logs) => _logs = logs;

    /// <summary>Returns the most recent agent activity logs, scoped to the caller the same way
    /// GET /api/Tasks scopes tasks (see <see cref="IAgentLogRepository.GetRecentAsync"/>).</summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? agentName,
        [FromQuery] int limit = 50)
    {
        if (!this.TryGetCurrentUserId(out var callerId))
            return this.UnauthenticatedIdentity();

        var logs = await _logs.GetRecentAsync(agentName, limit, callerId);
        return Ok(logs);
    }
}
