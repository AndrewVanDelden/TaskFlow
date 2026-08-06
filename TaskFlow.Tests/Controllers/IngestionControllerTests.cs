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

        var controller = new IngestionController(parser.Object, Mock.Of<IDraftCommitService>());

        var result = await controller.Ingest(new IngestDocumentDto { Content = "# doc" });

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeEquivalentTo(drafts);
    }

    [Fact]
    public async Task Commit_returns_200_with_the_committed_count()
    {
        var commit = new Mock<IDraftCommitService>();
        commit.Setup(c => c.CommitAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyList<TaskDraft>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Ok(3));

        var controller = new IngestionController(Mock.Of<IIngestionParser>(), commit.Object);

        var result = await controller.Commit(new CommitDraftsDto
        {
            SourceName = "spec.md",
            Drafts = new List<TaskDraft>()
        });

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(3);
    }
}
