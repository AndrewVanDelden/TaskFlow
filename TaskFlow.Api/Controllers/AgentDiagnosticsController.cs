using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Controllers;

/// <summary>
/// Development-only endpoint to verify the Anthropic SDK is configured correctly.
/// Remove or lock this down before production.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentDiagnosticsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AgentDiagnosticsController> _logger;

    public AgentDiagnosticsController(IConfiguration config, ILogger<AgentDiagnosticsController> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Sends a minimal test message to Claude and returns the response.
    /// Use this to confirm your API key and SDK are working.
    /// </summary>
    [HttpGet("ping-claude")]
    public async Task<IActionResult> PingClaude(CancellationToken cancellationToken)
    {
        var apiKey = _config["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { message = "Anthropic API key is not configured. Run: dotnet user-secrets set \"Anthropic:ApiKey\" \"your-key\"" });

        try
        {
            var client = new AnthropicClient(apiKey);

            var request = new MessageParameters
            {
                Model = _config["Anthropic:Model"] ?? "claude-opus-4-5",
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

            var response = await client.Messages.GetClaudeMessageAsync(request, cancellationToken);
            var text = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "(no text)";

            _logger.LogInformation("Claude ping successful: {Response}", text);

            return Ok(new
            {
                status = "connected",
                model = _config["Anthropic:Model"],
                claudeResponse = text
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude ping failed");
            return StatusCode(503, new { message = "Failed to connect to Claude.", error = ex.Message });
        }
    }
}