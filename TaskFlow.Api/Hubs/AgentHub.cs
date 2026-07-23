using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TaskFlow.Api.Hubs;

/// <summary>
/// Real-time channel for agent activity. The server pushes; clients only listen.
/// </summary>
[Authorize]
public class AgentHub : Hub
{
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(ILogger<AgentHub> logger) => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR client connected: {Id}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}