using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AgentLogsController(AppDbContext db) => _db = db;

    /// <summary>Returns the most recent agent activity logs.</summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? agentName,
        [FromQuery] int limit = 50)
    {
        var query = _db.AgentLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(agentName))
            query = query.Where(l => l.AgentName == agentName);

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync();

        return Ok(logs);
    }
}