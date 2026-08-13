using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskFlow.Api.Hubs;
using Xunit;

namespace TaskFlow.Tests.Hubs;

// Epic 3 Pre-Merge Code Review, finding 1.1 (Critical - Security): AgentHub was [Authorize] only,
// with no per-user grouping, so SignalRAgentNotifier's Clients.All broadcasts leaked every user's
// Epic 3 activity (including job-application titles) to every other authenticated user. These
// tests lock in the fix: every connection is placed in a group scoped to its own user id, so the
// notifier can target just the owner instead of everyone.
public class AgentHubTests
{
    private static (AgentHub Hub, Mock<IGroupManager> Groups, string ConnectionId) CreateSut(ClaimsPrincipal? user)
    {
        var hub = new AgentHub(NullLogger<AgentHub>.Instance);
        var groups = new Mock<IGroupManager>();
        const string connectionId = "conn-1";

        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns(connectionId);
        context.SetupGet(c => c.User).Returns(user);
        context.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);

        hub.Context = context.Object;
        hub.Groups = groups.Object;

        return (hub, groups, connectionId);
    }

    private static ClaimsPrincipal AuthenticatedUser(int userId)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task OnConnectedAsync_adds_the_connection_to_its_own_users_group()
    {
        var (hub, groups, connectionId) = CreateSut(AuthenticatedUser(42));

        await hub.OnConnectedAsync();

        groups.Verify(
            g => g.AddToGroupAsync(connectionId, AgentHub.GroupForUser(42), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_scopes_different_users_to_different_groups()
    {
        var (hubA, groupsA, connA) = CreateSut(AuthenticatedUser(1));
        var (hubB, groupsB, connB) = CreateSut(AuthenticatedUser(2));

        await hubA.OnConnectedAsync();
        await hubB.OnConnectedAsync();

        groupsA.Verify(g => g.AddToGroupAsync(connA, "user-1", It.IsAny<CancellationToken>()), Times.Once);
        groupsB.Verify(g => g.AddToGroupAsync(connB, "user-2", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Defensive: [Authorize] on the hub should make this unreachable in production, but the
    // connection handler must not throw if the identity is ever missing the claim.
    [Fact]
    public async Task OnConnectedAsync_does_not_throw_and_joins_no_group_when_there_is_no_user_id_claim()
    {
        var (hub, groups, _) = CreateSut(new ClaimsPrincipal(new ClaimsIdentity()));

        var act = async () => await hub.OnConnectedAsync();

        await act.Should().NotThrowAsync();
        groups.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GroupForUser_is_a_stable_deterministic_name()
    {
        AgentHub.GroupForUser(42).Should().Be(AgentHub.GroupForUser(42));
        AgentHub.GroupForUser(1).Should().NotBe(AgentHub.GroupForUser(2));
    }
}
