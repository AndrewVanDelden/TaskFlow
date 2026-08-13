using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskFlow.Api.Hubs;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Services;

// Epic 3 Pre-Merge Code Review, finding 1.1 (Critical - Security): every broadcast method used
// to always send via Clients.All, so any authenticated user received every other user's Epic 3
// activity. These tests lock in the fix - AgentAction and TaskMoved route to the owner's own
// SignalR group (AgentHub.GroupForUser) when an ownerId is supplied, and only fall back to
// Clients.All for the shared generic board (ownerId == null). AgentCycle carries no per-task data
// (confirmed in the review), so it is intentionally always broadcast-wide.
public class SignalRAgentNotifierTests
{
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IClientProxy> _allProxy = new();
    private readonly Mock<IClientProxy> _groupProxy = new();
    private readonly Mock<IHubContext<AgentHub>> _hubContext = new();

    private SignalRAgentNotifier CreateSut()
    {
        _hubContext.SetupGet(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.All).Returns(_allProxy.Object);
        _clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupProxy.Object);
        return new SignalRAgentNotifier(_hubContext.Object, NullLogger<SignalRAgentNotifier>.Instance);
    }

    private static AgentLog SampleLog() => new()
    {
        Id = 1,
        TaskId = 5,
        AgentName = "ResumeTailoring",
        Action = "Claimed",
        Details = "Claimed 'Senior Backend Engineer' for tailoring.",
        Success = true,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task AgentActionAsync_broadcasts_to_everyone_when_ownerId_is_null()
    {
        var sut = CreateSut();

        await sut.AgentActionAsync(SampleLog(), ownerId: null, CancellationToken.None);

        _allProxy.Verify(p => p.SendCoreAsync(HubEvents.AgentAction, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AgentActionAsync_scopes_to_the_owners_group_when_ownerId_is_set()
    {
        var sut = CreateSut();

        await sut.AgentActionAsync(SampleLog(), ownerId: 42, CancellationToken.None);

        _clients.Verify(c => c.Group(AgentHub.GroupForUser(42)), Times.Once);
        _groupProxy.Verify(p => p.SendCoreAsync(HubEvents.AgentAction, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _allProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AgentActionAsync_swallows_a_broadcast_failure_instead_of_throwing()
    {
        var sut = CreateSut();
        _allProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection dropped"));

        var act = async () => await sut.AgentActionAsync(SampleLog(), ownerId: null, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TaskMovedAsync_broadcasts_to_everyone_when_ownerId_is_null()
    {
        var sut = CreateSut();

        await sut.TaskMovedAsync(5, WorkflowStatus.InProgress, ownerId: null, CancellationToken.None);

        _allProxy.Verify(p => p.SendCoreAsync(HubEvents.TaskMoved, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TaskMovedAsync_scopes_to_the_owners_group_when_ownerId_is_set()
    {
        var sut = CreateSut();

        await sut.TaskMovedAsync(5, WorkflowStatus.Review, ownerId: 7, CancellationToken.None);

        _clients.Verify(c => c.Group(AgentHub.GroupForUser(7)), Times.Once);
        _groupProxy.Verify(p => p.SendCoreAsync(HubEvents.TaskMoved, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TaskMovedAsync_swallows_a_broadcast_failure_instead_of_throwing()
    {
        var sut = CreateSut();
        _groupProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection dropped"));

        var act = async () => await sut.TaskMovedAsync(5, WorkflowStatus.Todo, ownerId: 1, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AgentCycleAsync_always_broadcasts_to_everyone()
    {
        var sut = CreateSut();

        await sut.AgentCycleAsync("StaleTaskDetector", "started", CancellationToken.None);

        _allProxy.Verify(p => p.SendCoreAsync(HubEvents.AgentCycle, It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AgentCycleAsync_swallows_a_broadcast_failure_instead_of_throwing()
    {
        var sut = CreateSut();
        _allProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection dropped"));

        var act = async () => await sut.AgentCycleAsync("StaleTaskDetector", "started", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
