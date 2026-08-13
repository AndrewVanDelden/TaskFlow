using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Repositories;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentLogsController : ControllerBase
{
    private readonly IAgentLogRepository _logs;

    public AgentLogsController(IAgentLogRepository logs) => _logs = logs;

    /// <summary>Returns the most recent agent activity logs.</summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? agentName,
        [FromQuery] int limit = 50)
    {
        var logs = await _logs.GetRecentAsync(agentName, limit);
        return Ok(logs);
    }
}
