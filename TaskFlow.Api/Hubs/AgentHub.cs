using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TaskFlow.Api.Hubs;

/// <summary>
/// Real-time channel for agent activity. The server pushes; clients only listen.
///
/// Every connection is placed in a group scoped to its own user id (see
/// <see cref="GroupForUser"/>) so <see cref="Services.SignalRAgentNotifier"/> can target an
/// owner-scoped event (an Epic 3 sibling task's activity) at just that user instead of every
/// connected client. Fixes Epic 3 Pre-Merge Code Review, finding 1.1.
/// </summary>
[Authorize]
public class AgentHub : Hub
{
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(ILogger<AgentHub> logger) => _logger = logger;

    /// <summary>SignalR group name for a given user's connections.</summary>
    public static string GroupForUser(int userId) => $"user-{userId}";

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("SignalR client connected: {Id}", Context.ConnectionId);

        // [Authorize] guarantees a valid JWT, whose NameIdentifier claim is always the user's
        // numeric id (JwtService.GenerateToken) - int.TryParse rather than Parse defensively,
        // matching this project's established caution around unguarded int.Parse.
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is not null && int.TryParse(userIdClaim, out var userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId));

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("SignalR client disconnected: {Id}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
