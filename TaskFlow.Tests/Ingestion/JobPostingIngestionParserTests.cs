using FluentAssertions;
using Moq;
using TaskFlow.Api.Common;
using TaskFlow.Api.Ingestion;
using TaskFlow.Api.Models;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class JobPostingIngestionParserTests
{
    [Fact]
    public async Task Free_parser_returning_a_non_empty_list_short_circuits_the_paid_parser()
    {
        var free = new Mock<IIngestionParser>();
        free.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraft>>.Ok(
                new[] { new TaskDraft("From free parser", null, TaskKind.ResumeTailoring, "Acme") }));
        var paid = new Mock<IIngestionParser>();
        var parser = new JobPostingIngestionParser(free.Object, paid.Object);

        var result = await parser.ParseAsync("# Title\n## Acme");

        result.Value!.Should().ContainSingle(d => d.Title == "From free parser");
        paid.Verify(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Free_parser_returning_an_empty_list_escalates_to_the_paid_parser()
    {
        var free = new Mock<IIngestionParser>();
        free.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraft>>.Ok(Array.Empty<TaskDraft>()));
        var paid = new Mock<IIngestionParser>();
        paid.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TaskDraft>>.Ok(
                new[] { new TaskDraft("From Claude", "reqs", TaskKind.ResumeTailoring, "Acme") }));
        var parser = new JobPostingIngestionParser(free.Object, paid.Object);

        var result = await parser.ParseAsync("just a paragraph, no headings");

        result.Value!.Should().ContainSingle(d => d.Title == "From Claude");
        paid.Verify(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
