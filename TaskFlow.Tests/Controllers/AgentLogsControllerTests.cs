using FluentAssertions;
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
public class AgentLogsControllerTests
{
    private readonly Mock<IAgentLogRepository> _logs = new();

    private AgentLogsController CreateSut() => new(_logs.Object);

    [Fact]
    public async Task GetLogs_returns_Ok_with_the_repositorys_result()
    {
        var logs = new List<AgentLog> { new() { Id = 1, AgentName = "GenericExecutor", Action = "Claimed" } };
        _logs.Setup(l => l.GetRecentAsync(null, 50, It.IsAny<CancellationToken>())).ReturnsAsync(logs);

        var result = await CreateSut().GetLogs(agentName: null, limit: 50);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(logs);
    }

    [Fact]
    public async Task GetLogs_passes_the_agentName_and_limit_through_to_the_repository()
    {
        _logs.Setup(l => l.GetRecentAsync("StaleTaskDetector", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentLog>());

        await CreateSut().GetLogs(agentName: "StaleTaskDetector", limit: 10);

        _logs.Verify(l => l.GetRecentAsync("StaleTaskDetector", 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
