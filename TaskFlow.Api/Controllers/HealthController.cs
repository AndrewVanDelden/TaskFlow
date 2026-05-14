using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the current health status of the API.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        _logger.LogInformation("Health check called at {Time}", DateTime.UtcNow);

        return Ok(new
        {
            status = "healthy",
            app = "TaskFlow API",
            version = "1.0.0",
            timestamp = DateTime.UtcNow
        });
    }
}