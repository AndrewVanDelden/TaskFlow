using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using TaskFlow.Api.Services;
using Xunit;

namespace TaskFlow.Tests.Controllers;

public class JobApplicationsControllerTests
{
    private readonly Mock<IJobPostingIngestionParser> _parser = new();
    private readonly Mock<IResumeContextService> _resumeContext = new();
    private readonly Mock<IJobApplicationAssemblyService> _assembly = new();

    private JobApplicationsController CreateSut(int? currentUserId = 1)
    {
        var controller = new JobApplicationsController(_parser.Object, _resumeContext.Object, _assembly.Object);
        if (currentUserId is not null)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = PrincipalFor(currentUserId.Value) }
            };
        }
        return controller;
    }

    private static ClaimsPrincipal PrincipalFor(int userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Parse_returns_200_with_the_parsed_drafts()
    {
        var drafts = new List<TaskDraft> { new("Backend Engineer", null, TaskKind.ResumeTailoring, "Job Posting") };
        _parser.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraft>>.Ok(drafts));

        var controller = CreateSut(currentUserId: null);

        var result = await controller.Parse(new IngestDocumentDto { Content = "# Backend Engineer" });

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(drafts);
    }

    [Fact]
    public async Task SaveResumeContext_returns_200_and_forwards_the_current_user_id()
    {
        _resumeContext.Setup(s => s.SaveAsync("session-A", 1, "Base resume text.", "text", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Ok(true));

        var controller = CreateSut(currentUserId: 1);

        var result = await controller.SaveResumeContext(new SaveResumeContextDto
        {
            IngestionSessionId = "session-A",
            Content = "Base resume text.",
            ContentFormat = "text"
        });

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(true);
        _resumeContext.Verify(s => s.SaveAsync("session-A", 1, "Base resume text.", "text", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveResumeContext_returns_400_when_the_service_reports_invalid()
    {
        _resumeContext.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Invalid("bad input"));

        var controller = CreateSut(currentUserId: 1);

        var result = await controller.SaveResumeContext(new SaveResumeContextDto
        {
            IngestionSessionId = "session-A",
            Content = "x"
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Assemble_returns_200_with_the_created_application_and_forwards_the_current_user_id()
    {
        var application = new JobApplicationResponseDto { Id = 5, IngestionSessionId = "session-A", OwnerId = 1 };
        var posting = new TaskDraft("Backend Engineer", null, TaskKind.ResumeTailoring, "Job Posting");
        _assembly.Setup(a => a.AssembleAsync("session-A", 1, posting, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JobApplicationResponseDto>.Ok(application));

        var controller = CreateSut(currentUserId: 1);

        var result = await controller.Assemble(new AssembleJobApplicationDto
        {
            IngestionSessionId = "session-A",
            Posting = posting
        });

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(application);
        _assembly.Verify(a => a.AssembleAsync("session-A", 1, posting, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assemble_returns_404_when_no_resume_context_exists()
    {
        _assembly.Setup(a => a.AssembleAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TaskDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JobApplicationResponseDto>.NotFound("No base resume found for this session."));

        var controller = CreateSut(currentUserId: 1);

        var result = await controller.Assemble(new AssembleJobApplicationDto
        {
            IngestionSessionId = "session-A",
            Posting = new TaskDraft("Backend Engineer", null, TaskKind.ResumeTailoring, "Job Posting")
        });

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
