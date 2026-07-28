using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

/// <summary>Runtime control of the autonomous executor (enable/pause). Authorized (human only).</summary>
[ApiController]
[Route("api/agents")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IExecutorSwitch _executor;
    public AgentsController(IExecutorSwitch executor) => _executor = executor;

    [HttpGet("executor")]
    public IActionResult GetExecutor() => Ok(new { enabled = _executor.IsEnabled });

    [HttpPost("executor/enable")]
    public IActionResult Enable()
    {
        _executor.Enable();
        return Ok(new { enabled = _executor.IsEnabled });
    }

    [HttpPost("executor/disable")]
    public IActionResult Disable()
    {
        _executor.Disable();
        return Ok(new { enabled = _executor.IsEnabled });
    }
}
