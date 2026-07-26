using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Controllers;

public class AgentDiagnosticsControllerTests
{
    private static AgentDiagnosticsController CreateSut(string environment, bool claudeConfigured)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);

        var claude = new Mock<IClaudeClient>();
        claude.SetupGet(c => c.IsConfigured).Returns(claudeConfigured);

        return new AgentDiagnosticsController(
            claude.Object,
            Mock.Of<IConfiguration>(),
            env.Object,
            NullLogger<AgentDiagnosticsController>.Instance);
    }

    [Fact]
    public async Task PingClaude_returns_404_outside_Development()
    {
        var sut = CreateSut("Production", claudeConfigured: true);

        var result = await sut.PingClaude(CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task PingClaude_returns_503_when_key_missing_in_Development()
    {
        var sut = CreateSut("Development", claudeConfigured: false);

        var result = await sut.PingClaude(CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
    }
}
