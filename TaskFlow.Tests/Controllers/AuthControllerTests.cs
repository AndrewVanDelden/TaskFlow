using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Controllers;

public class AuthControllerTests
{
    [Fact]
    public async Task Register_returns_409_when_service_reports_conflict()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.RegisterAsync(It.IsAny<RegisterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponseDto>.Conflict("taken"));
        var sut = new AuthController(auth.Object);

        var result = await sut.Register(new RegisterDto());

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Login_returns_401_when_service_reports_unauthorized()
    {
        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.LoginAsync(It.IsAny<LoginDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AuthResponseDto>.Unauthorized("bad"));
        var sut = new AuthController(auth.Object);

        var result = await sut.Login(new LoginDto());

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}