using Anthropic.SDK.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Configuration;
using TaskFlow.Api.Services;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// Development-only endpoint to verify the Claude integration is configured. Returns 404
/// outside the Development environment so it is never exposed in production.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentDiagnosticsController : ControllerBase
{
    private readonly IClaudeClient _claude;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AgentDiagnosticsController> _logger;

    public AgentDiagnosticsController(
        IClaudeClient claude,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<AgentDiagnosticsController> logger)
    {
        _claude = claude;
        _config = config;
        _env = env;
        _logger = logger;
    }

    /// <summary>Sends a minimal test message to Claude and returns the response.</summary>
    [HttpGet("ping-claude")]
    public async Task<IActionResult> PingClaude(CancellationToken cancellationToken)
    {
        // Dev-only: hide entirely in production so it cannot be probed or spend tokens.
        if (!_env.IsDevelopment())
            return NotFound();

        if (!_claude.IsConfigured)
            return StatusCode(503, new { message = "Anthropic API key is not configured. Run: dotnet user-secrets set \"Anthropic:ApiKey\" \"your-key\"" });

        var model = _config["Anthropic:Model"] ?? AnthropicDefaults.Model;

        try
        {
            var request = new MessageParameters
            {
                Model = model,
                MaxTokens = 64,
                Messages = new List<Message>
                {
                    new Message
                    {
                        Role = RoleType.User,
                        Content = new List<ContentBase>
                        {
                            new TextContent { Text = "Reply with exactly: 'TaskFlow agent connection verified.'" }
                        }
                    }
                }
            };

            var response = await _claude.SendAsync(request, cancellationToken);
            var text = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "(no text)";

            _logger.LogInformation("Claude ping successful: {Response}", text);

            return Ok(new { status = "connected", model, claudeResponse = text });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude ping failed");
            return StatusCode(503, new { message = "Failed to connect to Claude.", error = ex.Message });
        }
    }
}
