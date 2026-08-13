using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.Models;
using TaskFlow.Api.Repositories;
using Xunit;

namespace TaskFlow.Tests.Controllers;

// Epic 3 Pre-Merge Code Review, findings 3.3/4.1: AgentLogsController re-implemented
// IAgentLogRepository.GetRecentAsync's exact filter/order/clamp logic against a raw AppDbContext
// instead of calling the repository - the only controller in the codebase to bypass the
// repository layer, and it had zero test coverage (finding 6.1). These tests lock in that the
// controller is now a thin pass-through to the repository.
//
// Copilot's automated review (PR #50) on the first fix: the SignalR ownership fix (1.1) only
// closed the live broadcast path - this endpoint still returned every log to any authenticated
// caller. It now requires and forwards the caller id, mirroring TasksController's convention.
public class AgentLogsControllerTests
{
    private readonly Mock<IAgentLogRepository> _logs = new();

    private static ClaimsPrincipal PrincipalFor(int userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private AgentLogsController CreateSut(ClaimsPrincipal user) => new(_logs.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        }
    };

    [Fact]
    public async Task GetLogs_returns_Ok_with_the_repositorys_result()
    {
        var logs = new List<AgentLog> { new() { Id = 1, AgentName = "GenericExecutor", Action = "Claimed" } };
        _logs.Setup(l => l.GetRecentAsync(null, 50, 3, It.IsAny<CancellationToken>())).ReturnsAsync(logs);

        var result = await CreateSut(PrincipalFor(3)).GetLogs(agentName: null, limit: 50);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(logs);
    }

    [Fact]
    public async Task GetLogs_passes_the_agentName_limit_and_caller_id_through_to_the_repository()
    {
        _logs.Setup(l => l.GetRecentAsync("StaleTaskDetector", 10, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentLog>());

        await CreateSut(PrincipalFor(3)).GetLogs(agentName: "StaleTaskDetector", limit: 10);

        _logs.Verify(l => l.GetRecentAsync("StaleTaskDetector", 10, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLogs_returns_401_when_the_identity_claim_is_missing()
    {
        var result = await CreateSut(new ClaimsPrincipal(new ClaimsIdentity())).GetLogs(agentName: null, limit: 50);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _logs.Verify(l => l.GetRecentAsync(
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
