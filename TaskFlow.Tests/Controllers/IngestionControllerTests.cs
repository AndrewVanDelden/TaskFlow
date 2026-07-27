using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Controllers;
using TaskFlow.Api.DTOs;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Controllers;

public class IngestionControllerTests
{
    [Fact]
    public async Task Ingest_returns_200_with_the_parsed_drafts()
    {
        var drafts = new List<TaskDraft> { new("Wire auth", null, TaskKind.Generic, "Backend") };
        var parser = new Mock<IIngestionParser>();
        parser.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraft>>.Ok(drafts));

        var controller = new IngestionController(parser.Object);

        var result = await controller.Ingest(new IngestDocumentDto { Content = "# doc" });

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeEquivalentTo(drafts);
    }
}
