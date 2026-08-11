using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Controllers;

public class TasksControllerTests
{
    private static ClaimsPrincipal PrincipalFor(int userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    // ── GetAll (PR #45 review finding: caller identity now required to scope Epic 3 tasks) ──────

    [Fact]
    public async Task GetAll_returns_200_and_forwards_the_current_user_id()
    {
        var service = new Mock<ITaskService>();
        service.Setup(s => s.GetAllAsync(null, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskResponseDto>>.Ok(new List<TaskResponseDto>()));
        var sut = new TasksController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = PrincipalFor(3) }
            }
        };

        var result = await sut.GetAll(null, null);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.GetAllAsync(null, null, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_returns_401_when_the_identity_claim_is_missing()
    {
        var service = new Mock<ITaskService>();
        var sut = new TasksController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
            }
        };

        var result = await sut.GetAll(null, null);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        service.Verify(s => s.GetAllAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_returns_400_when_service_reports_validation_error()
    {
        var service = new Mock<ITaskService>();
        service.Setup(s => s.CreateAsync(It.IsAny<CreateTaskDto>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<TaskResponseDto>.Invalid("bad"));
        var sut = new TasksController(service.Object);

        var result = await sut.Create(new CreateTaskDto { Title = "x" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }
    [Fact]
    public async Task GetById_returns_404_when_service_reports_not_found()
    {
        var service = new Mock<ITaskService>();
        service.Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskResponseDto>.NotFound("nope"));
        var sut = new TasksController(service.Object);

        var result = await sut.GetById(1);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Create_returns_200_when_service_succeeds()
    {
        var service = new Mock<ITaskService>();
        service.Setup(s => s.CreateAsync(It.IsAny<CreateTaskDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskResponseDto>.Ok(new TaskResponseDto()));
        var sut = new TasksController(service.Object);

        var result = await sut.Create(new CreateTaskDto { Title = "x" });

        result.Should().BeOfType<OkObjectResult>();
    }
}