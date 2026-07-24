using FluentAssertions;
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